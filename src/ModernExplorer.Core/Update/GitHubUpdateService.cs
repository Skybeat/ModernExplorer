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
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
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

        return new Version(1, 0, 0);
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
    /// Selects the most appropriate binary asset (.zip, .exe, .msi, .msix) for the current architecture.
    /// </summary>
    public static GitHubReleaseAsset? SelectBestAsset(GitHubReleaseInfo release)
    {
        if (release.Assets.Count == 0)
            return null;

        // Prefer portable .zip or .exe installer for win-x64
        var x64Zip = release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            (a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase) || !a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)));
        if (x64Zip is not null)
            return x64Zip;

        var exeOrMsi = release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));
        if (exeOrMsi is not null)
            return exeOrMsi;

        return release.Assets[0];
    }

    /// <summary>
    /// Builds the PowerShell self-updater script that waits for the running process to exit,
    /// copies extracted update files into the application directory, and restarts the executable.
    /// </summary>
    public static string BuildSelfUpdatePowerShellScript(int processId, string stagingDir, string targetAppDir, string exePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine($"$proc = Get-Process -Id {processId} -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($proc) { $proc.WaitForExit(15000) | Out-Null }");
        sb.AppendLine("Start-Sleep -Milliseconds 600");
        sb.AppendLine($"Copy-Item -Path \"{stagingDir}\\*\" -Destination \"{targetAppDir}\" -Recurse -Force");
        sb.AppendLine($"Start-Process -FilePath \"{exePath}\"");
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
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadFile,
                UseShellExecute = true,
            });
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

            var exePath = currentExePath ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ModernExplorer.exe");
            var targetDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory.TrimEnd('\\', '/');

            var scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
            var scriptContent = BuildSelfUpdatePowerShellScript(Environment.ProcessId, stagingDir, targetDir, exePath);
            await File.WriteAllTextAsync(scriptPath, scriptContent, Encoding.UTF8, ct).ConfigureAwait(false);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            return true;
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

