param(
    [string]$Version = "auto",
    [ValidateSet("Auto", "Patch", "Minor", "Major")]
    [string]$BumpType = "Auto"
)

$ErrorActionPreference = "Stop"

$credOutput = "protocol=https`nhost=github.com`n`n" | git credential fill
$tokenLine = ($credOutput | Where-Object { $_ -match '^password=' })
if (-not $tokenLine) { throw "Could not retrieve GitHub token from git credential manager." }
$token = $tokenLine.Substring(9).Trim()

$headers = @{
    "Authorization"        = "Bearer $token"
    "Accept"               = "application/vnd.github+json"
    "User-Agent"           = "ModernExplorer-Rust-ReleasePublisher"
    "X-GitHub-Api-Version" = "2022-11-28"
}

if ([string]::IsNullOrWhiteSpace($Version) -or $Version -eq "auto") {
    $latestVer = [version]"1.9.6"
    $latestTag = $null
    try {
        $latestRel = Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/latest" -Method Get -Headers $headers
        if ($latestRel -and $latestRel.tag_name) {
            $latestTag = $latestRel.tag_name
            $cleanTag = $latestRel.tag_name.TrimStart('v', 'V')
            $parsed = $null
            if ([version]::TryParse($cleanTag, [ref]$parsed)) {
                $latestVer = $parsed
            }
        }
    } catch { }

    $effectiveBump = $BumpType
    if ($effectiveBump -eq "Auto") {
        $effectiveBump = "Patch"
        try {
            $logRange = if ($latestTag) { "$latestTag..HEAD" } else { "-n 10" }
            $commitLines = git log $logRange --pretty=format:"%s %b" 2>$null
            foreach ($line in $commitLines) {
                if ($line -match 'BREAKING[\s-]CHANGE' -or $line -match '^[a-zA-Z]+(\([^)]+\))?!:') {
                    $effectiveBump = "Major"
                    break
                }
                if ($line -match '^(feat|feature|add)(\([^)]+\))?:') {
                    $effectiveBump = "Minor"
                }
            }
        } catch { }
    }

    $major = [Math]::Max(0, $latestVer.Major)
    $minor = [Math]::Max(0, $latestVer.Minor)
    $patch = [Math]::Max(0, $latestVer.Build)

    switch ($effectiveBump) {
        "Major" {
            $major += 1
            $minor = 0
            $patch = 0
        }
        "Minor" {
            $minor += 1
            $patch = 0
        }
        default {
            $patch += 1
        }
    }

    $Version = "$major.$minor.$patch"
    Write-Output "Calculated Semantic Version ($effectiveBump bump): $latestVer -> $Version"
} else {
    $Version = $Version.TrimStart('v', 'V')
}

$TagName = "v$Version"

Write-Output "=== 1. Synchronizing Rust Version to $Version ==="

$cargoFiles = @(
    "rust/Cargo.toml",
    "rust/modern-explorer-core/Cargo.toml",
    "rust/modern-explorer-app/Cargo.toml"
)
foreach ($cf in $cargoFiles) {
    if (Test-Path $cf) {
        $content = Get-Content $cf -Raw
        $updated = [regex]::Replace($content, '(?m)^version\s*=\s*"[^"]+"', "version = `"$Version`"")
        if ($updated -ne $content) {
            Set-Content -Path $cf -Value $updated -Encoding UTF8 -NoNewline
        }
    }
}

$tauriConf = "rust/modern-explorer-app/tauri.conf.json"
if (Test-Path $tauriConf) {
    $content = Get-Content $tauriConf -Raw -Encoding UTF8
    $updated = [regex]::Replace($content, '"version":\s*"[^"]+"', "`"version`": `"$Version`"")
    $updated = [regex]::Replace($updated, 'Dual-Pane File Manager v\d+\.\d+\.\d+', "Dual-Pane File Manager v$Version")
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText((Resolve-Path $tauriConf).Path, $updated, (New-Object System.Text.UTF8Encoding($false)))
    }
}

$indexHtml = "rust/modern-explorer-app/ui/index.html"
if (Test-Path $indexHtml) {
    $content = Get-Content $indexHtml -Raw -Encoding UTF8
    $updated = [regex]::Replace($content, 'v\d+\.\d+\.\d+ \(Rust Edition\)', "v$Version (Rust Edition)")
    $updated = [regex]::Replace($updated, 'v\d+\.\d+\.\d+ \(Rust Studio\)', "v$Version (Rust Studio)")
    $updated = [regex]::Replace($updated, 'Installierte Version: v\d+\.\d+\.\d+', "Installierte Version: v$Version")
    $updated = [regex]::Replace($updated, 'id="aboutInstalledVer">v\d+\.\d+\.\d+<', "id=`"aboutInstalledVer`">v$Version<")
    $updated = [regex]::Replace($updated, 'id="aboutLatestVer">v\d+\.\d+\.\d+<', "id=`"aboutLatestVer`">v$Version<")
    $updated = [regex]::Replace($updated, 'aktuelle Version v\d+\.\d+\.\d+\.', "aktuelle Version v$Version.")
    $updated = [regex]::Replace($updated, "version:\s*'\d+\.\d+\.\d+'", "version: '$Version'")
    $updated = [regex]::Replace($updated, "data\.version\s*\|\|\s*'\d+\.\d+\.\d+'", "data.version || '$Version'")
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText((Resolve-Path $indexHtml).Path, $updated, (New-Object System.Text.UTF8Encoding($false)))
    }
}

$updateRs = "rust/modern-explorer-core/src/update.rs"
if (Test-Path $updateRs) {
    $content = Get-Content $updateRs -Raw
    $updated = [regex]::Replace($content, 'pub const CURRENT_VERSION: &''static str = "[^"]+";', "pub const CURRENT_VERSION: &'static str = `"$Version`";")
    if ($updated -ne $content) {
        Set-Content -Path $updateRs -Value $updated -Encoding UTF8 -NoNewline
    }
}

$issRust = "installers/rust/ModernExplorer-Rust.iss"
if (Test-Path $issRust) {
    $content = Get-Content $issRust -Raw
    $updated = [regex]::Replace($content, '#define AppVersion "[^"]+"', "#define AppVersion `"$Version`"")
    if ($updated -ne $content) {
        Set-Content -Path $issRust -Value $updated -Encoding UTF8 -NoNewline
    }
}

Write-Output "=== 2. Running Rust Unit Tests ==="
cargo test --manifest-path rust/Cargo.toml
if ($LASTEXITCODE -ne 0) { throw "Rust unit tests failed with exit code $LASTEXITCODE" }

Write-Output "=== 3. Committing Version Bump & Pushing Tag $TagName to Trigger Multi-OS Rust Release ==="
git add rust/ installers/rust/ .github/workflows/release.yml README.md scripts/publish-releases.ps1
$status = git status --porcelain
if ($status) {
    git commit -m "chore(release): bump Rust version to $TagName"
}
git tag -f $TagName
git push origin main
git push origin $TagName --force

Write-Output "=== Rust Multi-OS Release $TagName Triggered on GitHub Actions! ==="
