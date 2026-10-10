param(
    [string]$Version = "auto",
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"

$credOutput = @("protocol=https", "host=github.com", "") | git credential fill
$tokenLine = ($credOutput | Where-Object { $_ -match '^password=' })
if (-not $tokenLine) { throw "Could not retrieve GitHub token from git credential manager." }
$token = $tokenLine.Substring(9).Trim()

$headers = @{
    "Authorization"        = "Bearer $token"
    "Accept"               = "application/vnd.github+json"
    "User-Agent"           = "ModernExplorer-ReleasePublisher"
    "X-GitHub-Api-Version" = "2022-11-28"
}

if ([string]::IsNullOrWhiteSpace($Version) -or $Version -eq "auto") {
    $latestVer = [version]"1.0.0"
    try {
        $latestRel = Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/latest" -Method Get -Headers $headers
        if ($latestRel -and $latestRel.tag_name) {
            $cleanTag = $latestRel.tag_name.TrimStart('v', 'V')
            $parsed = $null
            if ([version]::TryParse($cleanTag, [ref]$parsed)) {
                $latestVer = $parsed
            }
        }
    } catch { }

    $nextPatch = [Math]::Max(0, $latestVer.Build) + 1
    $Version = "$($latestVer.Major).$($latestVer.Minor).$nextPatch"
    Write-Output "Auto-incremented release version: $latestVer -> $Version"
} else {
    $Version = $Version.TrimStart('v', 'V')
}

$TagName = "v$Version"

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "Multi-OS release of Modern Explorer $TagName for Windows (win-x64, win-arm64, win-x86), Linux (linux-x64, linux-arm64), and macOS (osx-x64, osx-arm64)."
}

# Sync project version numbers
$csprojFiles = @(
    "src/ModernExplorer.App/ModernExplorer.App.csproj",
    "src/ModernExplorer.CrossPlatform/ModernExplorer.CrossPlatform.csproj",
    "src/ModernExplorer.Core/ModernExplorer.Core.csproj"
)
foreach ($proj in $csprojFiles) {
    if (Test-Path $proj) {
        $content = Get-Content $proj -Raw
        $updated = [regex]::Replace($content, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
        if ($updated -ne $content) {
            Set-Content -Path $proj -Value $updated -Encoding UTF8 -NoNewline
        }
    }
}

$DistDir = Join-Path $env:TEMP "ModernExplorer_Release_$TagName"
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Write-Output "=== 1. Building Windows Native WinUI 3 Releases (win-x64, win-arm64) ==="
dotnet build src/ModernExplorer.App/ModernExplorer.App.csproj -c Release -p:Platform=x64 -r win-x64 -p:Version=$Version
Compress-Archive -Path "src\ModernExplorer.App\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\*" -DestinationPath "$DistDir\ModernExplorer-win-x64.zip" -Force

dotnet build src/ModernExplorer.App/ModernExplorer.App.csproj -c Release -p:Platform=ARM64 -r win-arm64 -p:Version=$Version
Compress-Archive -Path "src\ModernExplorer.App\bin\ARM64\Release\net9.0-windows10.0.19041.0\win-arm64\*" -DestinationPath "$DistDir\ModernExplorer-win-arm64.zip" -Force

Write-Output "=== 2. Building Cross-Platform Desktop Releases (win-x86, linux-x64, linux-arm64, osx-x64, osx-arm64) ==="
$CrossPlatformRids = @("win-x86", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
foreach ($rid in $CrossPlatformRids) {
    Write-Output "Publishing $rid..."
    $pubDir = Join-Path $DistDir "pub-$rid"
    dotnet publish src/ModernExplorer.CrossPlatform/ModernExplorer.CrossPlatform.csproj `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -o $pubDir
    Compress-Archive -Path "$pubDir\*" -DestinationPath "$DistDir\ModernExplorer-$rid.zip" -Force
    Remove-Item $pubDir -Recurse -Force
}

Write-Output "=== 3. Pushing Git Tag & Uploading All Assets to GitHub Release $TagName ==="
git tag -f $TagName
git push origin main
git push origin $TagName --force

$release = $null
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/tags/$TagName" -Method Get -Headers $headers
} catch {
    $release = $null
}

if (-not $release) {
    $relBody = @{
        tag_name   = $TagName
        name       = "Modern Explorer $TagName"
        body       = $ReleaseNotes
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json -Compress
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases" -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($relBody)) -ContentType "application/json; charset=utf-8"
} else {
    $patchBody = @{
        name = "Modern Explorer $TagName"
        body = $ReleaseNotes
    } | ConvertTo-Json -Compress
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/$($release.id)" -Method Patch -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($patchBody)) -ContentType "application/json; charset=utf-8"
}

foreach ($zipFile in Get-ChildItem -Path $DistDir -Filter "*.zip") {
    $assetName = $zipFile.Name
    $existing = $release.assets | Where-Object { $_.name -eq $assetName }
    if ($existing) {
        Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/assets/$($existing.id)" -Method Delete -Headers $headers | Out-Null
    }
    $uploadUrl = "https://uploads.github.com/repos/Skybeat/ModernExplorer/releases/$($release.id)/assets?name=$assetName"
    $uploaded = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -InFile $zipFile.FullName -ContentType "application/zip"
    Write-Output "Uploaded: $($uploaded.name) -> $($uploaded.browser_download_url)"
}

Remove-Item $DistDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "=== Multi-OS Release $TagName Complete! ==="
