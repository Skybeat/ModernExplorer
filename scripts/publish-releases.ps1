param(
    [string]$Version = "auto",
    [ValidateSet("Auto", "Patch", "Minor", "Major")]
    [string]$BumpType = "Auto",
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
$verParts = $Version.Split('.')
$verMajor = if ($verParts.Length -gt 0) { $verParts[0] } else { "1" }
$verMinor = if ($verParts.Length -gt 1) { $verParts[1] } else { "0" }
$verPatch = if ($verParts.Length -gt 2) { $verParts[2] } else { "0" }

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "Multi-OS release of Modern Explorer $TagName for Windows (win-x64, win-arm64, win-x86), Linux (linux-x64, linux-arm64), and macOS (osx-x64, osx-arm64)."
}

# Sync project & installer version numbers according to SemVer
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

$issFile = "installers/windows/ModernExplorer.iss"
if (Test-Path $issFile) {
    $issContent = Get-Content $issFile -Raw
    $issUpdated = [regex]::Replace($issContent, '#define AppVersion "[^"]+"', "#define AppVersion `"$Version`"")
    if ($issUpdated -ne $issContent) {
        Set-Content -Path $issFile -Value $issUpdated -Encoding UTF8 -NoNewline
    }
}

$updateServiceFile = "src/ModernExplorer.Core/Update/GitHubUpdateService.cs"
if (Test-Path $updateServiceFile) {
    $usContent = Get-Content $updateServiceFile -Raw
    $usUpdated = [regex]::Replace($usContent, 'return new Version\(\d+,\s*\d+,\s*\d+\);', "return new Version($verMajor, $verMinor, $verPatch);")
    if ($usUpdated -ne $usContent) {
        Set-Content -Path $updateServiceFile -Value $usUpdated -Encoding UTF8 -NoNewline
    }
}

$DistDir = Join-Path $env:TEMP "ModernExplorer_Release_$TagName"
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

# Locate local Inno Setup 6 compiler if available
$isccCandidates = @(
    "C:\Users\nicog\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

Write-Output "=== 1. Building Self-Contained Windows Native WinUI 3 Releases (win-x64, win-arm64) ==="
dotnet build src/ModernExplorer.App/ModernExplorer.App.csproj -c Release -p:Platform=x64 -r win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:Version=$Version
$x64OutDir = (Resolve-Path "src\ModernExplorer.App\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64").Path
Compress-Archive -Path "$x64OutDir\*" -DestinationPath "$DistDir\ModernExplorer-windows-x64.zip" -Force
if ($iscc) {
    & $iscc "/DAppVersion=$Version" "/DTargetRid=win-x64" "/DSourceDir=$x64OutDir" "/DOutputDir=$DistDir" "installers/windows/ModernExplorer.iss"
}

dotnet build src/ModernExplorer.App/ModernExplorer.App.csproj -c Release -p:Platform=ARM64 -r win-arm64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:Version=$Version
$arm64OutDir = (Resolve-Path "src\ModernExplorer.App\bin\ARM64\Release\net9.0-windows10.0.19041.0\win-arm64").Path
Compress-Archive -Path "$arm64OutDir\*" -DestinationPath "$DistDir\ModernExplorer-windows-arm64.zip" -Force
if ($iscc) {
    & $iscc "/DAppVersion=$Version" "/DTargetRid=win-arm64" "/DSourceDir=$arm64OutDir" "/DOutputDir=$DistDir" "installers/windows/ModernExplorer.iss"
}

Write-Output "=== 2. Building Self-Contained Cross-Platform Desktop Releases (win-x86, linux-x64, linux-arm64, osx-x64, osx-arm64) ==="
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
        -p:IncludeAllContentForSelfExtract=true `
        -p:Version=$Version `
        -o $pubDir
    $zipName = if ($rid -like "win-*") { "ModernExplorer-windows-$($rid.Substring(4)).zip" } else { "ModernExplorer-$rid.zip" }
    Compress-Archive -Path "$pubDir\*" -DestinationPath "$DistDir\$zipName" -Force
    if ($rid -eq "win-x86" -and $iscc) {
        & $iscc "/DAppVersion=$Version" "/DTargetRid=win-x86" "/DSourceDir=$pubDir" "/DOutputDir=$DistDir" "installers/windows/ModernExplorer.iss"
    }
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

$releaseAssets = Get-ChildItem -Path $DistDir -File | Where-Object { $_.Extension -in @(".zip", ".exe") }
foreach ($assetFile in $releaseAssets) {
    $assetName = $assetFile.Name
    $contentType = if ($assetFile.Extension -eq ".exe") { "application/vnd.microsoft.portable-executable" } else { "application/zip" }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $currentRel = Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/$($release.id)" -Method Get -Headers $headers
            $existing = $currentRel.assets | Where-Object { $_.name -eq $assetName }
            if ($existing) {
                Invoke-RestMethod -Uri "https://api.github.com/repos/Skybeat/ModernExplorer/releases/assets/$($existing.id)" -Method Delete -Headers $headers | Out-Null
            }
            $uploadUrl = "https://uploads.github.com/repos/Skybeat/ModernExplorer/releases/$($release.id)/assets?name=$assetName"
            $uploaded = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -InFile $assetFile.FullName -ContentType $contentType
            Write-Output "Uploaded: $($uploaded.name) -> $($uploaded.browser_download_url)"
            break
        } catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds 2
        }
    }
}

Remove-Item $DistDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Output "=== Multi-OS Release $TagName Complete! ==="
