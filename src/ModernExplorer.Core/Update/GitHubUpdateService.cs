using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ModernExplorer.Core.Update;

/// <summary>
/// Represents a downloadable asset attached to a GitHub release.
/// </summary>
public sealed record GitHubReleaseAsset(
    string Name,
    string BrowserDownloadUrl,
    long Size,
    string ContentType);

/// <summary>
/// Contains metadata about a GitHub release and whether it is newer than the currently running version.
/// </summary>
public sealed record GitHubReleaseInfo(
    string TagName,
    Version ParsedVersion,
    string Title,
    string Body,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<GitHubReleaseAsset> Assets);

/// <summary>
/// Semantic Versioning (SemVer 2.0.0) increment level: MAJOR.MINOR.PATCH.
/// </summary>
public enum SemVerBumpType
{
    /// <summary>Backwards-compatible bug fixes (PATCH: x.y.Z+1).</summary>
    Patch = 0,
    /// <summary>Backwards-compatible new functionality (MINOR: x.Y+1.0).</summary>
    Minor = 1,
    /// <summary>Incompatible / breaking API or architectural changes (MAJOR: X+1.0.0).</summary>
    Major = 2,
}

/// <summary>
/// Result of checking GitHub Releases for an application update.
/// </summary>
public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    Version CurrentVersion,
    GitHubReleaseInfo? LatestRelease,
    string? ErrorMessage = null);

/// <summary>
/// Checks GitHub Releases (https://github.com/Skybeat/ModernExplorer) on startup or on demand,
/// compares semantic versions, and performs one-click download and self-update installation.
/// </summary>
public sealed class GitHubUpdateService
{
    public const string RepositoryOwner = "Skybeat";
    public const string RepositoryName = "ModernExplorer";
    public const string RepositoryWebUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
    public const string ReleasesListApiUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page=5";
    public const string RepositoryReleasesWebUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";

    private readonly HttpClient _httpClient;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModernExplorer", GetCurrentVersionString()));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    /// <summary>
    /// Returns the currently running application version.
    /// </summary>
    public static Version GetCurrentVersion()
    {
        var entry = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateService).Assembly;
        var v = entry.GetName().Version;
        if (v is not null && (v.Major > 0 || v.Minor > 0))
            return new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

        return new Version(1, 8, 0);
    }

    public static string GetCurrentVersionString()
    {
        var v = GetCurrentVersion();
        return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    /// <summary>
    /// Parses a version tag such as "v1.2.3", "1.2.3-beta1", or "release-2.0" into a normalized 3-part <see cref="Version"/>.
    /// </summary>
    public static bool TryParseVersion(string? rawTag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(rawTag))
            return false;

        var cleaned = rawTag.Trim();
        while (cleaned.Length > 0 && !char.IsDigit(cleaned[0]))
            cleaned = cleaned[1..];

        int dashIdx = cleaned.IndexOfAny(['-', '+', ' ']);
        if (dashIdx > 0)
            cleaned = cleaned[..dashIdx];

        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int major))
            return false;

        int minor = parts.Length > 1 && int.TryParse(parts[1], out int m) ? m : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out int b) ? b : 0;
        version = new Version(Math.Max(0, major), Math.Max(0, minor), Math.Max(0, build));
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="candidateVersion"/> is strictly newer than <paramref name="currentVersion"/>.
    /// </summary>
    public static bool IsNewerVersion(Version currentVersion, Version candidateVersion)
    {
        var normCurrent = new Version(
            Math.Max(0, currentVersion.Major),
            Math.Max(0, currentVersion.Minor),
            Math.Max(0, currentVersion.Build));

        var normCandidate = new Version(
            Math.Max(0, candidateVersion.Major),
            Math.Max(0, candidateVersion.Minor),
            Math.Max(0, candidateVersion.Build));

        return normCandidate > normCurrent;
    }

    /// <summary>
    /// Calculates the next Semantic Version (SemVer 2.0.0: MAJOR.MINOR.PATCH) according to <paramref name="bumpType"/>.
    /// </summary>
    public static Version CalculateNextSemanticVersion(Version currentVersion, SemVerBumpType bumpType)
    {
        int major = Math.Max(0, currentVersion.Major);
        int minor = Math.Max(0, currentVersion.Minor);
        int patch = Math.Max(0, currentVersion.Build);

        return bumpType switch
        {
            SemVerBumpType.Major => new Version(major + 1, 0, 0),
            SemVerBumpType.Minor => new Version(major, minor + 1, 0),
            _                    => new Version(major, minor, patch + 1),
        };
    }

    /// <summary>
    /// Determines the appropriate Semantic Version bump level from commit messages using Conventional Commits / SemVer rules.
    /// </summary>
    public static SemVerBumpType DetermineSemanticVersionBump(IEnumerable<string>? commitMessages)
    {
        if (commitMessages is null)
            return SemVerBumpType.Patch;

        bool hasMinor = false;
        foreach (var raw in commitMessages)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var msg = raw.Trim();
            if (msg.Contains("BREAKING CHANGE", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("BREAKING-CHANGE", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(msg, @"^[a-zA-Z]+(\([^)]+\))?!:"))
            {
                return SemVerBumpType.Major;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(msg, @"^(feat|feature|add)(\([^)]+\))?:", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                hasMinor = true;
            }
        }

        return hasMinor ? SemVerBumpType.Minor : SemVerBumpType.Patch;
    }

    /// <summary>
    /// Parses a GitHub Release JSON object into <see cref="GitHubReleaseInfo"/>.
    /// </summary>
    public static GitHubReleaseInfo? ParseReleaseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0)
                return null;
            root = root[0];
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        if (!TryParseVersion(tagName, out var parsedVersion))
            return null;

        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
        if (string.IsNullOrWhiteSpace(name))
            name = tagName;

        var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? RepositoryReleasesWebUrl : RepositoryReleasesWebUrl;

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out var pubProp) &&
            pubProp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(pubProp.GetString(), out var dto))
        {
            publishedAt = dto;
        }

        var assets = new List<GitHubReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetEl in assetsProp.EnumerateArray())
            {
                var assetName = assetEl.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                var downloadUrl = assetEl.TryGetProperty("browser_download_url", out var du) ? du.GetString() ?? "" : "";
                var size = assetEl.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long sVal) ? sVal : 0L;
                var contentType = assetEl.TryGetProperty("content_type", out var ct) ? ct.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(assetName) && !string.IsNullOrWhiteSpace(downloadUrl))
                    assets.Add(new GitHubReleaseAsset(assetName, downloadUrl, size, contentType));
            }
        }

        return new GitHubReleaseInfo(tagName, parsedVersion, name, body, htmlUrl, publishedAt, assets);
    }

    /// <summary>
    /// Queries GitHub for the latest release and determines whether an update is available.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(Version? currentVersionOverride = null, CancellationToken ct = default)
    {
        var current = currentVersionOverride ?? GetCurrentVersion();
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Fallback to the releases list endpoint if no formal 'latest' release is marked yet
                using var listResp = await _httpClient.GetAsync(ReleasesListApiUrl, ct).ConfigureAwait(false);
                if (!listResp.IsSuccessStatusCode)
                    return new UpdateCheckResult(false, current, null);

                var listJson = await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var listRelease = ParseReleaseJson(listJson);
                if (listRelease is null)
                    return new UpdateCheckResult(false, current, null);

                return new UpdateCheckResult(IsNewerVersion(current, listRelease.ParsedVersion), current, listRelease);
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var release = ParseReleaseJson(json);
            if (release is null)
                return new UpdateCheckResult(false, current, null);

            bool isNewer = IsNewerVersion(current, release.ParsedVersion);
            return new UpdateCheckResult(isNewer, current, release);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, current, null, ex.Message);
        }
    }

    /// <summary>
    /// All target runtime identifiers built and published for GitHub Releases across Windows, Linux, and macOS.
    /// </summary>
    public static IReadOnlyList<string> SupportedRuntimeIdentifiers { get; } =
    [
        "win-x64",
        "win-arm64",
        "win-x86",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64",
    ];

    /// <summary>
    /// Detects the Runtime Identifier (RID) of the currently running operating system and CPU architecture.
    /// </summary>
    public static string GetCurrentRuntimeIdentifier()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            _ => "x64",
        };

        if (OperatingSystem.IsWindows())
            return $"win-{arch}";
        if (OperatingSystem.IsMacOS())
            return $"osx-{arch}";
        if (OperatingSystem.IsLinux())
            return $"linux-{arch}";

        return $"win-{arch}";
    }

    /// <summary>
    /// Selects the most appropriate binary asset (.zip, .tar.gz, .exe, .msi, .msix, .dmg, .AppImage)
    /// for the specified or currently running operating system and CPU architecture.
    /// </summary>
    public static GitHubReleaseAsset? SelectBestAsset(GitHubReleaseInfo release, string? targetRid = null)
    {
        if (release.Assets.Count == 0)
            return null;

        var rid = (targetRid ?? GetCurrentRuntimeIdentifier()).ToLowerInvariant();
        var parts = rid.Split('-', 2);
        var osPrefix = parts[0]; // "win", "linux", "osx"
        var arch = parts.Length > 1 ? parts[1] : "x64";

        // When running from an All-Users / Inno Setup installation on Windows, prefer the Setup.exe updater
        if (targetRid is null && osPrefix == "win")
        {
            var baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            bool isInstalledApp = File.Exists(Path.Combine(baseDir, "unins000.exe")) || !CanWriteToDirectory(baseDir);
            if (isInstalledApp)
            {
                var setupExe = release.Assets.FirstOrDefault(a =>
                    a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (setupExe is not null)
                    return setupExe;
            }
        }

        // 1. Exact RID match (e.g., "ModernExplorer-win-x64.zip", "ModernExplorer-linux-x64.zip", "ModernExplorer-osx-arm64.zip")
        var exactArchive = release.Assets.FirstOrDefault(a =>
            a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
            (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)));
        if (exactArchive is not null)
            return exactArchive;

        // 2. OS alias + architecture match (e.g., "macos-arm64", "windows-x64")
        string[] osAliases = osPrefix switch
        {
            "osx" => ["osx", "macos", "darwin", "mac"],
            "linux" => ["linux"],
            _ => ["win", "windows"],
        };

        var aliasMatch = release.Assets.FirstOrDefault(a =>
            osAliases.Any(alias => a.Name.Contains(alias, StringComparison.OrdinalIgnoreCase)) &&
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase));
        if (aliasMatch is not null)
            return aliasMatch;

        // 3. Fallback for Windows installers or generic x64 zip when running on Windows
        if (osPrefix == "win")
        {
            var winZip = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("osx", StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase));
            if (winZip is not null)
                return winZip;

            var exeOrMsi = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));
            if (exeOrMsi is not null)
                return exeOrMsi;
        }

        return release.Assets[0];
    }

    /// <summary>
    /// Checks whether the current user process has write and modify permissions in the specified directory.
    /// </summary>
    public static bool CanWriteToDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        try
        {
            var probeFile = Path.Combine(directoryPath, $".me_update_probe_{Guid.NewGuid():N}.tmp");
            using (var fs = new FileStream(probeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0);
            }

            var existingConfig = Path.Combine(directoryPath, "ModernExplorer.runtimeconfig.json");
            if (File.Exists(existingConfig))
            {
                using var existingFs = new FileStream(existingConfig, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the PowerShell self-updater script that opens a dedicated GUI Update Progress Window
    /// with a ProgressBar, waits for the running ModernExplorer process to exit, ensures write permissions
    /// for All-Users installations (e.g., C:\Program Files\ModernExplorer), copies extracted update files
    /// with live progress feedback and retry logic, and restarts the updated executable.
    /// </summary>
    public static string BuildSelfUpdatePowerShellScript(int processId, string stagingDir, string targetAppDir, string exePath)
    {
        var cleanStaging = stagingDir.TrimEnd('\\', '/');
        var cleanTarget = targetAppDir.TrimEnd('\\', '/');
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$stagingDir = \"{cleanStaging}\"");
        sb.AppendLine($"$targetAppDir = \"{cleanTarget}\"");
        sb.AppendLine($"$exePath = \"{exePath}\"");
        sb.AppendLine("$logFile = Join-Path (Split-Path -Parent $stagingDir) 'update.log'");
        sb.AppendLine("\"Update started at $(Get-Date -Format o)\" | Out-File -FilePath $logFile -Encoding UTF8 -Force");
        sb.AppendLine("$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)");
        sb.AppendLine("$progFilesDir = Join-Path $env:ProgramFiles 'ModernExplorer'");
        sb.AppendLine("$targetDirs = [System.Collections.Generic.List[string]]::new()");
        sb.AppendLine("$targetDirs.Add($targetAppDir)");
        sb.AppendLine("if ((Test-Path -LiteralPath $progFilesDir) -and ($progFilesDir.TrimEnd('\\') -ne $targetAppDir.TrimEnd('\\'))) {");
        sb.AppendLine("    $targetDirs.Add($progFilesDir)");
        sb.AppendLine("}");
        sb.AppendLine("$needsElevation = $false");
        sb.AppendLine("foreach ($checkDir in $targetDirs) {");
        sb.AppendLine("    try {");
        sb.AppendLine("        if (-not (Test-Path -LiteralPath $checkDir)) { New-Item -ItemType Directory -Path $checkDir -Force -ErrorAction Stop | Out-Null }");
        sb.AppendLine("        $probe = Join-Path $checkDir (\".me_probe_\" + [Guid]::NewGuid().ToString('N') + \".tmp\")");
        sb.AppendLine("        [System.IO.File]::WriteAllText($probe, '1')");
        sb.AppendLine("        Remove-Item -LiteralPath $probe -Force -ErrorAction Stop");
        sb.AppendLine("        $cfg = Join-Path $checkDir 'ModernExplorer.runtimeconfig.json'");
        sb.AppendLine("        if (Test-Path -LiteralPath $cfg) {");
        sb.AppendLine("            $fs = [System.IO.File]::Open($cfg, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::ReadWrite)");
        sb.AppendLine("            $fs.Dispose()");
        sb.AppendLine("        }");
        sb.AppendLine("    } catch {");
        sb.AppendLine("        $needsElevation = $true");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("if ($needsElevation -and (-not $isAdmin) -and $PSCommandPath) {");
        sb.AppendLine("    \"Elevating updater script via RunAs...\" | Out-File -FilePath $logFile -Append -Encoding UTF8");
        sb.AppendLine("    try {");
        sb.AppendLine("        Start-Process -FilePath 'powershell.exe' -WorkingDirectory $env:SystemRoot -ArgumentList \"-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `\"$PSCommandPath`\"\" -Verb RunAs -ErrorAction Stop");
        sb.AppendLine("    } catch {");
        sb.AppendLine("        $shell = New-Object -ComObject Shell.Application");
        sb.AppendLine("        $shell.ShellExecute('powershell.exe', \"-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `\"$PSCommandPath`\"\", $env:SystemRoot, 'runas', 0)");
        sb.AppendLine("    }");
        sb.AppendLine("    exit");
        sb.AppendLine("}");
        sb.AppendLine("if ($isAdmin) {");
        sb.AppendLine("    foreach ($dir in $targetDirs) {");
        sb.AppendLine("        icacls $dir /grant '*S-1-5-32-545:(OI)(CI)M' /T /C /Q | Out-Null");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("Add-Type -AssemblyName System.Windows.Forms");
        sb.AppendLine("Add-Type -AssemblyName System.Drawing");
        sb.AppendLine("[System.Windows.Forms.Application]::EnableVisualStyles()");
        sb.AppendLine("$isDe = ([System.Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'de')");
        sb.AppendLine("$titleText = if ($isDe) { 'Modern Explorer â€“ Update wird installiert' } else { 'Modern Explorer â€“ Installing Update' }");
        sb.AppendLine("$waitText  = if ($isDe) { 'Warte auf das SchlieÃŸen von Modern Explorer...' } else { 'Waiting for Modern Explorer to close...' }");
        sb.AppendLine("$copyText  = if ($isDe) { 'Aktualisiere Programmdateien' } else { 'Updating application files' }");
        sb.AppendLine("$doneText  = if ($isDe) { 'Update abgeschlossen! Starte Modern Explorer neu...' } else { 'Update complete! Restarting Modern Explorer...' }");
        sb.AppendLine("$form = New-Object System.Windows.Forms.Form");
        sb.AppendLine("$form.Text = $titleText");
        sb.AppendLine("$form.Size = New-Object System.Drawing.Size(520, 215)");
        sb.AppendLine("$form.StartPosition = 'CenterScreen'");
        sb.AppendLine("$form.FormBorderStyle = 'FixedDialog'");
        sb.AppendLine("$form.MaximizeBox = $false");
        sb.AppendLine("$form.MinimizeBox = $false");
        sb.AppendLine("$form.ControlBox = $false");
        sb.AppendLine("$form.TopMost = $true");
        sb.AppendLine("$form.BackColor = [System.Drawing.Color]::FromArgb(24, 24, 32)");
        sb.AppendLine("$form.ForeColor = [System.Drawing.Color]::FromArgb(240, 244, 248)");
        sb.AppendLine("$headerLabel = New-Object System.Windows.Forms.Label");
        sb.AppendLine("$headerLabel.Text = $titleText");
        sb.AppendLine("$headerLabel.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)");
        sb.AppendLine("$headerLabel.ForeColor = [System.Drawing.Color]::FromArgb(56, 189, 248)");
        sb.AppendLine("$headerLabel.Location = New-Object System.Drawing.Point(24, 20)");
        sb.AppendLine("$headerLabel.Size = New-Object System.Drawing.Size(460, 28)");
        sb.AppendLine("$form.Controls.Add($headerLabel)");
        sb.AppendLine("$statusLabel = New-Object System.Windows.Forms.Label");
        sb.AppendLine("$statusLabel.Text = $waitText");
        sb.AppendLine("$statusLabel.Font = New-Object System.Drawing.Font('Segoe UI', 9.5)");
        sb.AppendLine("$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(210, 216, 226)");
        sb.AppendLine("$statusLabel.Location = New-Object System.Drawing.Point(24, 54)");
        sb.AppendLine("$statusLabel.Size = New-Object System.Drawing.Size(390, 22)");
        sb.AppendLine("$form.Controls.Add($statusLabel)");
        sb.AppendLine("$percentLabel = New-Object System.Windows.Forms.Label");
        sb.AppendLine("$percentLabel.Text = '0 %'");
        sb.AppendLine("$percentLabel.Font = New-Object System.Drawing.Font('Segoe UI', 9.5, [System.Drawing.FontStyle]::Bold)");
        sb.AppendLine("$percentLabel.ForeColor = [System.Drawing.Color]::FromArgb(56, 189, 248)");
        sb.AppendLine("$percentLabel.TextAlign = 'TopRight'");
        sb.AppendLine("$percentLabel.Location = New-Object System.Drawing.Point(414, 54)");
        sb.AppendLine("$percentLabel.Size = New-Object System.Drawing.Size(70, 22)");
        sb.AppendLine("$form.Controls.Add($percentLabel)");
        sb.AppendLine("$progressBar = New-Object System.Windows.Forms.ProgressBar");
        sb.AppendLine("$progressBar.Location = New-Object System.Drawing.Point(24, 84)");
        sb.AppendLine("$progressBar.Size = New-Object System.Drawing.Size(460, 26)");
        sb.AppendLine("$progressBar.Minimum = 0");
        sb.AppendLine("$progressBar.Maximum = 100");
        sb.AppendLine("$progressBar.Value = 0");
        sb.AppendLine("$progressBar.Style = 'Continuous'");
        sb.AppendLine("$form.Controls.Add($progressBar)");
        sb.AppendLine("$detailLabel = New-Object System.Windows.Forms.Label");
        sb.AppendLine("$detailLabel.Text = $targetAppDir");
        sb.AppendLine("$detailLabel.Font = New-Object System.Drawing.Font('Consolas', 8.5)");
        sb.AppendLine("$detailLabel.ForeColor = [System.Drawing.Color]::FromArgb(148, 163, 184)");
        sb.AppendLine("$detailLabel.AutoEllipsis = $true");
        sb.AppendLine("$detailLabel.Location = New-Object System.Drawing.Point(24, 122)");
        sb.AppendLine("$detailLabel.Size = New-Object System.Drawing.Size(460, 22)");
        sb.AppendLine("$form.Controls.Add($detailLabel)");
        sb.AppendLine("function Set-UpdateProgress([int]$pct, [string]$status, [string]$detail) {");
        sb.AppendLine("    $clamped = [Math]::Max(0, [Math]::Min(100, $pct))");
        sb.AppendLine("    $progressBar.Value = $clamped");
        sb.AppendLine("    $percentLabel.Text = \"$clamped %\"");
        sb.AppendLine("    if ($status) { $statusLabel.Text = $status }");
        sb.AppendLine("    if ($detail) { $detailLabel.Text = $detail }");
        sb.AppendLine("    [System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("}");
        sb.AppendLine("$form.Show()");
        sb.AppendLine("$form.Activate()");
        sb.AppendLine("Set-UpdateProgress 5 $waitText $exePath");
        sb.AppendLine($"$proc = Get-Process -Id {processId} -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($proc) {");
        sb.AppendLine("    for ($t = 0; $t -lt 60; $t++) {");
        sb.AppendLine("        if ($proc.HasExited) { break }");
        sb.AppendLine("        [System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("        Start-Sleep -Milliseconds 100");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("for ($w = 0; $w -lt 15; $w++) {");
        sb.AppendLine("    $running = @(Get-Process -Name 'ModernExplorer' -ErrorAction SilentlyContinue)");
        sb.AppendLine("    if ($running.Count -eq 0) { break }");
        sb.AppendLine("    if ($w -ge 4) { $running | Stop-Process -Force -ErrorAction SilentlyContinue }");
        sb.AppendLine("    [System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("    Start-Sleep -Milliseconds 200");
        sb.AppendLine("}");
        sb.AppendLine("Start-Sleep -Milliseconds 300");
        sb.AppendLine("Set-UpdateProgress 12 \"$copyText...\" $targetAppDir");
        sb.AppendLine("$items = @(Get-ChildItem -LiteralPath $stagingDir -Recurse -Force -ErrorAction Stop)");
        sb.AppendLine("$totalItems = [Math]::Max(1, $items.Count)");
        sb.AppendLine("$idx = 0");
        sb.AppendLine("foreach ($item in $items) {");
        sb.AppendLine("    $idx++");
        sb.AppendLine("    $relPath = $item.FullName.Substring($stagingDir.Length).TrimStart('\\', '/')");
        sb.AppendLine("    $pct = 12 + [int](($idx * 83) / $totalItems)");
        sb.AppendLine("    Set-UpdateProgress $pct \"$copyText ($idx / $totalItems)...\" $relPath");
        sb.AppendLine("    foreach ($dir in $targetDirs) {");
        sb.AppendLine("        $destPath = Join-Path $dir $relPath");
        sb.AppendLine("        for ($attempt = 1; $attempt -le 15; $attempt++) {");
        sb.AppendLine("            try {");
        sb.AppendLine("                if ($item.PSIsContainer) {");
        sb.AppendLine("                    if (-not (Test-Path -LiteralPath $destPath)) {");
        sb.AppendLine("                        New-Item -ItemType Directory -Path $destPath -Force -ErrorAction Stop | Out-Null");
        sb.AppendLine("                    }");
        sb.AppendLine("                } else {");
        sb.AppendLine("                    $destParent = Split-Path -Parent $destPath");
        sb.AppendLine("                    if (-not (Test-Path -LiteralPath $destParent)) {");
        sb.AppendLine("                        New-Item -ItemType Directory -Path $destParent -Force -ErrorAction Stop | Out-Null");
        sb.AppendLine("                    }");
        sb.AppendLine("                    Copy-Item -LiteralPath $item.FullName -Destination $destPath -Force -ErrorAction Stop");
        sb.AppendLine("                }");
        sb.AppendLine("                break");
        sb.AppendLine("            } catch [System.UnauthorizedAccessException] {");
        sb.AppendLine("                \"Attempt $attempt unauthorized on ${destPath} - $($_.Exception.Message)\" | Out-File -FilePath $logFile -Append -Encoding UTF8");
        sb.AppendLine("                if (-not $isAdmin -and $PSCommandPath) {");
        sb.AppendLine("                    $form.Close()");
        sb.AppendLine("                    try {");
        sb.AppendLine("                        Start-Process -FilePath 'powershell.exe' -WorkingDirectory $env:SystemRoot -ArgumentList \"-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `\"$PSCommandPath`\"\" -Verb RunAs -ErrorAction Stop");
        sb.AppendLine("                    } catch {");
        sb.AppendLine("                        $shell = New-Object -ComObject Shell.Application");
        sb.AppendLine("                        $shell.ShellExecute('powershell.exe', \"-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `\"$PSCommandPath`\"\", $env:SystemRoot, 'runas', 0)");
        sb.AppendLine("                    }");
        sb.AppendLine("                    exit");
        sb.AppendLine("                }");
        sb.AppendLine("                [System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("                Start-Sleep -Milliseconds 300");
        sb.AppendLine("            } catch {");
        sb.AppendLine("                \"Attempt $attempt failed on ${destPath} - $($_.Exception.Message)\" | Out-File -FilePath $logFile -Append -Encoding UTF8");
        sb.AppendLine("                [System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("                Start-Sleep -Milliseconds 300");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    if (($idx % 3) -eq 0) { Start-Sleep -Milliseconds 15 }");
        sb.AppendLine("}");
        sb.AppendLine("\"Update copy succeeded for $($items.Count) items\" | Out-File -FilePath $logFile -Append -Encoding UTF8");
        sb.AppendLine("try {");
        sb.AppendLine("    $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion");
        sb.AppendLine("    if ($ver) {");
        sb.AppendLine("        $shortVer = ($ver -split '\\.')[0..2] -join '.'");
        sb.AppendLine("        foreach ($hive in @('HKLM:', 'HKCU:')) {");
        sb.AppendLine("            $regPath = \"$hive\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{8F4B9A2E-5C31-4E9B-A712-9D6E4B12C8F0}_is1\"");
        sb.AppendLine("            if (Test-Path -LiteralPath $regPath) {");
        sb.AppendLine("                Set-ItemProperty -LiteralPath $regPath -Name 'DisplayVersion' -Value $shortVer -ErrorAction SilentlyContinue");
        sb.AppendLine("                Set-ItemProperty -LiteralPath $regPath -Name 'DisplayName' -Value \"Modern Explorer $shortVer\" -ErrorAction SilentlyContinue");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("} catch { }");
        sb.AppendLine("Set-UpdateProgress 100 $doneText $exePath");
        sb.AppendLine("Add-Type -TypeDefinition @'");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("public static class MeWin32Fg {");
        sb.AppendLine("    [DllImport(\"user32.dll\")] public static extern bool AllowSetForegroundWindow(int dwProcessId);");
        sb.AppendLine("    [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);");
        sb.AppendLine("    [DllImport(\"user32.dll\")] public static extern bool BringWindowToTop(IntPtr hWnd);");
        sb.AppendLine("    [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);");
        sb.AppendLine("    [DllImport(\"user32.dll\")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);");
        sb.AppendLine("}");
        sb.AppendLine("'@ -ErrorAction SilentlyContinue");
        sb.AppendLine("try { [MeWin32Fg]::AllowSetForegroundWindow(-1) | Out-Null } catch { }");
        sb.AppendLine("$form.Activate()");
        sb.AppendLine("[System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("$launched = Start-Process -FilePath \"$exePath\" -WorkingDirectory \"$targetAppDir\" -PassThru");
        sb.AppendLine("if ($launched) {");
        sb.AppendLine("    try { [MeWin32Fg]::AllowSetForegroundWindow($launched.Id) | Out-Null } catch { }");
        sb.AppendLine("    $hWnd = [IntPtr]::Zero");
        sb.AppendLine("    for ($k = 0; $k -lt 40; $k++) {");
        sb.AppendLine("        Start-Sleep -Milliseconds 100");
        sb.AppendLine("        [System.Windows.Forms.Application]::DoEvents()");
        sb.AppendLine("        try {");
        sb.AppendLine("            $launched.Refresh()");
        sb.AppendLine("            if ($launched.MainWindowHandle -ne [IntPtr]::Zero) {");
        sb.AppendLine("                $hWnd = $launched.MainWindowHandle");
        sb.AppendLine("                break");
        sb.AppendLine("            }");
        sb.AppendLine("        } catch { }");
        sb.AppendLine("    }");
        sb.AppendLine("    $form.Close()");
        sb.AppendLine("    $form.Dispose()");
        sb.AppendLine("    try {");
        sb.AppendLine("        $wsh = New-Object -ComObject WScript.Shell");
        sb.AppendLine("        $wsh.AppActivate($launched.Id) | Out-Null");
        sb.AppendLine("    } catch { }");
        sb.AppendLine("    if ($hWnd -ne [IntPtr]::Zero) {");
        sb.AppendLine("        try {");
        sb.AppendLine("            [MeWin32Fg]::ShowWindow($hWnd, 9) | Out-Null");
        sb.AppendLine("            [MeWin32Fg]::BringWindowToTop($hWnd) | Out-Null");
        sb.AppendLine("            [MeWin32Fg]::SetForegroundWindow($hWnd) | Out-Null");
        sb.AppendLine("            [MeWin32Fg]::SwitchToThisWindow($hWnd, $true)");
        sb.AppendLine("        } catch { }");
        sb.AppendLine("    }");
        sb.AppendLine("} else {");
        sb.AppendLine("    $form.Close()");
        sb.AppendLine("    $form.Dispose()");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Builds a POSIX shell self-updater script for Linux and macOS that waits for the running process to exit,
    /// copies extracted update files into the application directory, restores executable permissions, and restarts the binary.
    /// </summary>
    public static string BuildSelfUpdateShellScript(int processId, string stagingDir, string targetAppDir, string exePath)
    {
        var sb = new StringBuilder();
        sb.Append("#!/bin/sh\n");
        sb.Append($"while kill -0 {processId} 2>/dev/null; do sleep 0.3; done\n");
        sb.Append("sleep 0.5\n");
        sb.Append($"cp -Rf \"{stagingDir}/.\" \"{targetAppDir}/\"\n");
        sb.Append($"chmod +x \"{exePath}\"\n");
        sb.Append($"nohup \"{exePath}\" >/dev/null 2>&1 &\n");
        return sb.ToString();
    }

    /// <summary>
    /// Downloads the release asset (or opens the GitHub release page if no binary asset is attached)
    /// and initiates the self-update installation.
    /// Returns true if the application should close to complete the update.
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync(
        GitHubReleaseInfo release,
        string? currentExePath = null,
        CancellationToken ct = default)
    {
        var asset = SelectBestAsset(release);
        if (asset is null)
        {
            OpenUrlInBrowser(release.HtmlUrl);
            return false;
        }

        var updateRoot = Path.Combine(Path.GetTempPath(), "ModernExplorer_Update", release.TagName);
        if (Directory.Exists(updateRoot))
        {
            try { Directory.Delete(updateRoot, recursive: true); } catch { }
        }
        Directory.CreateDirectory(updateRoot);

        var downloadFile = Path.Combine(updateRoot, asset.Name);
        using (var resp = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var netStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = File.Create(downloadFile);
            await netStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        }

        if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
        {
            var installerPsi = new ProcessStartInfo
            {
                FileName = downloadFile,
                UseShellExecute = true,
            };
            if (asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                installerPsi.Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
            }
            Process.Start(installerPsi);
            return true;
        }

        if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var stagingDir = Path.Combine(updateRoot, "staging");
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(downloadFile, stagingDir, overwriteFiles: true);

            // If the zip contains a single top-level folder, unwrap it
            var subDirs = Directory.GetDirectories(stagingDir);
            var subFiles = Directory.GetFiles(stagingDir);
            if (subFiles.Length == 0 && subDirs.Length == 1)
                stagingDir = subDirs[0];

            var defaultExeName = OperatingSystem.IsWindows() ? "ModernExplorer.exe" : "ModernExplorer";
            var resolvedProcessPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(resolvedProcessPath) &&
                string.Equals(Path.GetFileNameWithoutExtension(resolvedProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                resolvedProcessPath = null;
            }

            var exePath = currentExePath
                ?? resolvedProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, defaultExeName);
            var targetDir = (Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory).TrimEnd('\\', '/');

            if (OperatingSystem.IsWindows())
            {
                var scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
                var scriptContent = BuildSelfUpdatePowerShellScript(Environment.ProcessId, stagingDir, targetDir, exePath);
                await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct).ConfigureAwait(false);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File \"{scriptPath}\"",
                    WorkingDirectory = Environment.SystemDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                var started = Process.Start(psi);
                return started is not null;
            }
            else
            {
                var scriptPath = Path.Combine(updateRoot, "apply-update.sh");
                var scriptContent = BuildSelfUpdateShellScript(Environment.ProcessId, stagingDir, targetDir, exePath);
                await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(false), ct).ConfigureAwait(false);

                var started = Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    ArgumentList = { scriptPath },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return started is not null;
            }
        }

        OpenUrlInBrowser(release.HtmlUrl);
        return false;
    }

    public static void OpenUrlInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }
}

