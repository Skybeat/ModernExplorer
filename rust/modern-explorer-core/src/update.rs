use regex::Regex;
use serde::{Deserialize, Deserializer, Serialize};
use std::fs;
use std::io;
use std::path::PathBuf;
use std::process::Command;

fn null_to_empty<'de, D>(deserializer: D) -> Result<String, D::Error>
where
    D: Deserializer<'de>,
{
    let opt = Option::<String>::deserialize(deserializer)?;
    Ok(opt.unwrap_or_default())
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct GitHubAssetInfo {
    #[serde(default, deserialize_with = "null_to_empty")]
    pub name: String,
    #[serde(default, deserialize_with = "null_to_empty")]
    pub browser_download_url: String,
    #[serde(default)]
    pub size: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct GitHubReleaseInfo {
    #[serde(default, deserialize_with = "null_to_empty")]
    pub tag_name: String,
    #[serde(default, deserialize_with = "null_to_empty")]
    pub name: String,
    #[serde(default, deserialize_with = "null_to_empty")]
    pub body: String,
    #[serde(default, deserialize_with = "null_to_empty")]
    pub html_url: String,
    #[serde(default, deserialize_with = "null_to_empty")]
    pub published_at: String,
    #[serde(default)]
    pub assets: Vec<GitHubAssetInfo>,
}

pub struct GitHubUpdateService;

impl GitHubUpdateService {
    pub const CURRENT_VERSION: &'static str = "1.9.5";
    pub const RELEASES_URL: &'static str = "https://github.com/Skybeat/ModernExplorer/releases";
    pub const LATEST_API_URL: &'static str =
        "https://api.github.com/repos/Skybeat/ModernExplorer/releases/latest";
    pub const RELEASES_LIST_API_URL: &'static str =
        "https://api.github.com/repos/Skybeat/ModernExplorer/releases?per_page=10";

    pub fn parse_semver(tag: &str) -> (u32, u32, u32) {
        let cleaned = tag
            .trim()
            .trim_start_matches(|c: char| !c.is_ascii_digit());
        let core = cleaned.split(['-', '+', ' ']).next().unwrap_or(cleaned);
        let mut parts = core.split('.').filter_map(|p| p.parse::<u32>().ok());
        (
            parts.next().unwrap_or(0),
            parts.next().unwrap_or(0),
            parts.next().unwrap_or(0),
        )
    }

    pub fn is_newer_version(remote_tag: &str, current_version: &str) -> bool {
        Self::parse_semver(remote_tag) > Self::parse_semver(current_version)
    }

    pub fn fetch_latest_release() -> Result<GitHubReleaseInfo, String> {
        let latest_res = ureq::get(Self::LATEST_API_URL)
            .set("User-Agent", "ModernExplorer-Rust-Updater")
            .set("Accept", "application/vnd.github+json")
            .call();

        let mut best_release: Option<GitHubReleaseInfo> = None;

        if let Ok(resp) = latest_res {
            if let Ok(rel) = resp.into_json::<GitHubReleaseInfo>() {
                if !rel.tag_name.is_empty() {
                    best_release = Some(rel);
                }
            }
        }

        // Also check the releases list endpoint in case a newer release was published or latest returned 404
        if let Ok(list_resp) = ureq::get(Self::RELEASES_LIST_API_URL)
            .set("User-Agent", "ModernExplorer-Rust-Updater")
            .set("Accept", "application/vnd.github+json")
            .call()
        {
            if let Ok(list) = list_resp.into_json::<Vec<GitHubReleaseInfo>>() {
                for candidate in list {
                    if candidate.tag_name.is_empty() {
                        continue;
                    }
                    let replace = match &best_release {
                        None => true,
                        Some(curr) => {
                            Self::parse_semver(&candidate.tag_name)
                                > Self::parse_semver(&curr.tag_name)
                        }
                    };
                    if replace {
                        best_release = Some(candidate);
                    }
                }
            }
        }

        best_release.ok_or_else(|| {
            "Keine GitHub-Release-Informationen konnten abgerufen werden.".to_string()
        })
    }

    pub fn check_for_update() -> Result<Option<GitHubReleaseInfo>, String> {
        let release = Self::fetch_latest_release()?;
        if Self::is_newer_version(&release.tag_name, Self::CURRENT_VERSION) {
            Ok(Some(release))
        } else {
            Ok(None)
        }
    }

    pub fn check_for_update_full() -> Result<(bool, GitHubReleaseInfo), String> {
        let release = Self::fetch_latest_release()?;
        let is_newer = Self::is_newer_version(&release.tag_name, Self::CURRENT_VERSION);
        Ok((is_newer, release))
    }

    pub fn select_best_rust_asset(release: &GitHubReleaseInfo) -> Option<&GitHubAssetInfo> {
        if release.assets.is_empty() {
            return None;
        }

        let arch = if cfg!(target_arch = "aarch64") {
            "arm64"
        } else {
            "x64"
        };

        let rust_assets: Vec<&GitHubAssetInfo> = release
            .assets
            .iter()
            .filter(|a| {
                a.name
                    .to_ascii_lowercase()
                    .starts_with("modernexplorer-rust-")
            })
            .collect();

        let pool: Vec<&GitHubAssetInfo> = if !rust_assets.is_empty() {
            rust_assets
        } else {
            release.assets.iter().collect()
        };

        if cfg!(target_os = "windows") {
            // Prefer Portable .exe for direct in-place replacement of ModernExplorer-Rust.exe,
            // or Setup.exe if Portable.exe is not found
            let portable_exe = format!("modernexplorer-rust-windows-{arch}-portable.exe");
            if let Some(a) = pool
                .iter()
                .find(|a| a.name.to_ascii_lowercase() == portable_exe)
            {
                return Some(*a);
            }
            let setup_exe = format!("modernexplorer-rust-setup-win-{arch}.exe");
            if let Some(a) = pool
                .iter()
                .find(|a| a.name.to_ascii_lowercase() == setup_exe)
            {
                return Some(*a);
            }
        } else if cfg!(target_os = "macos") {
            let pkg_name = format!("modernexplorer-rust-setup-macos-{arch}.pkg");
            let dmg_name = format!("modernexplorer-rust-setup-macos-{arch}.dmg");
            let tar_name = format!("modernexplorer-rust-macos-{arch}-portable.tar.gz");
            for want in [pkg_name, dmg_name, tar_name] {
                if let Some(a) = pool.iter().find(|a| a.name.to_ascii_lowercase() == want) {
                    return Some(*a);
                }
            }
        } else if cfg!(target_os = "linux") {
            let sh_name = format!("modernexplorer-rust-installer-linux-{arch}.sh");
            let appimage_name = format!("modernexplorer-rust-linux-{arch}-portable.appimage");
            let deb_name = format!("modernexplorer-rust-setup-linux-{arch}.deb");
            for want in [sh_name, appimage_name, deb_name] {
                if let Some(a) = pool.iter().find(|a| a.name.to_ascii_lowercase() == want) {
                    return Some(*a);
                }
            }
        }

        pool.first().copied()
    }

    pub fn download_and_install_update(release: &GitHubReleaseInfo) -> Result<String, String> {
        let asset = Self::select_best_rust_asset(release).ok_or_else(|| {
            "Kein passendes Update-Paket für dieses Betriebssystem im Release gefunden.".to_string()
        })?;

        let update_dir: PathBuf = std::env::temp_dir()
            .join("ModernExplorer_Rust_Update")
            .join(release.tag_name.trim_start_matches(['v', 'V']));
        let _ = fs::remove_dir_all(&update_dir);
        fs::create_dir_all(&update_dir).map_err(|e| format!("Temp-Ordner Fehler: {e}"))?;

        let download_path = update_dir.join(&asset.name);
        let resp = ureq::get(&asset.browser_download_url)
            .set("User-Agent", "ModernExplorer-Rust-Updater")
            .call()
            .map_err(|e| format!("Download fehlgeschlagen: {e}"))?;

        let mut reader = resp.into_reader();
        let mut out_file =
            fs::File::create(&download_path).map_err(|e| format!("Datei-Schreibfehler: {e}"))?;
        io::copy(&mut reader, &mut out_file)
            .map_err(|e| format!("Fehler beim Speichern des Updates: {e}"))?;
        drop(out_file);

        let current_exe =
            std::env::current_exe().map_err(|e| format!("Aktueller Pfad nicht lesbar: {e}"))?;
        let current_dir = current_exe
            .parent()
            .map(|p| p.to_path_buf())
            .unwrap_or_else(|| PathBuf::from("."));
        let pid = std::process::id();
        let ver_clean = release.tag_name.trim_start_matches(['v', 'V']).to_string();

        #[cfg(target_os = "windows")]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x08000000;

            let script_path = update_dir.join("apply_rust_update.ps1");
            let dl_str = download_path.to_string_lossy().replace('\'', "''");
            let exe_str = current_exe.to_string_lossy().replace('\'', "''");
            let dir_str = current_dir.to_string_lossy().replace('\'', "''");

            let ps_script = format!(
                r#"$ErrorActionPreference = 'Continue'
$dlFile = '{dl_str}'
$targetExe = '{exe_str}'
$targetDir = '{dir_str}'
$newVer = '{ver_clean}'
$pidToWait = {pid}

$logFile = Join-Path (Split-Path -Parent $dlFile) 'update.log'
function Write-Log($msg) {{
    $ts = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')
    Add-Content -LiteralPath $logFile -Value "[$ts] $msg" -ErrorAction SilentlyContinue
}}

Write-Log "=== ModernExplorer Updater v$newVer gestartet ==="
Write-Log "PID to wait: $pidToWait"
Write-Log "Downloaded file: $dlFile"
Write-Log "Target exe: $targetExe"
Write-Log "Target dir: $targetDir"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = "ModernExplorer Studio (Rust) - Update auf v$newVer"
$form.Size = New-Object System.Drawing.Size(500, 195)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ControlBox = $false
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(15, 23, 42)
$form.ForeColor = [System.Drawing.Color]::FromArgb(241, 245, 249)

$hdr = New-Object System.Windows.Forms.Label
$hdr.Text = "ModernExplorer Studio (Rust) v$newVer wird installiert..."
$hdr.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
$hdr.ForeColor = [System.Drawing.Color]::FromArgb(56, 189, 248)
$hdr.Location = New-Object System.Drawing.Point(22, 18)
$hdr.Size = New-Object System.Drawing.Size(445, 26)
$form.Controls.Add($hdr)

$lbl = New-Object System.Windows.Forms.Label
$lbl.Text = "Warte auf das Beenden von ModernExplorer..."
$lbl.Font = New-Object System.Drawing.Font('Segoe UI', 9.5)
$lbl.Location = New-Object System.Drawing.Point(22, 50)
$lbl.Size = New-Object System.Drawing.Size(445, 22)
$form.Controls.Add($lbl)

$bar = New-Object System.Windows.Forms.ProgressBar
$bar.Location = New-Object System.Drawing.Point(22, 80)
$bar.Size = New-Object System.Drawing.Size(445, 24)
$bar.Minimum = 0
$bar.Maximum = 100
$bar.Value = 15
$form.Controls.Add($bar)

$det = New-Object System.Windows.Forms.Label
$det.Text = $targetExe
$det.Font = New-Object System.Drawing.Font('Consolas', 8.5)
$det.ForeColor = [System.Drawing.Color]::FromArgb(148, 163, 184)
$det.AutoEllipsis = $true
$det.Location = New-Object System.Drawing.Point(22, 114)
$det.Size = New-Object System.Drawing.Size(445, 20)
$form.Controls.Add($det)

$form.Show()
$form.Activate()
[System.Windows.Forms.Application]::DoEvents()

# 1. Warte auf Beendigung des Hauptprozesses
for ($i = 0; $i -lt 50; $i++) {{
    $p = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
    if (-not $p) {{ break }}
    if ($i -ge 15) {{ 
        Write-Log "Beende Prozess PID $pidToWait gewaltsam..."
        Stop-Process -Id $pidToWait -Force -ErrorAction SilentlyContinue 
    }}
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 120
}}

Get-Process -Name 'ModernExplorer-Rust' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 350
Write-Log "Hauptprozess beendet."

$bar.Value = 50
$lbl.Text = "Aktualisiere Programmdatei auf v$newVer..."
[System.Windows.Forms.Application]::DoEvents()

# 2. Pruefen, ob Setup-Installer oder Portable-Exe
$isSetup = ($dlFile -match '(?i)setup.*\.exe$')
$copySuccess = $false

if ($isSetup) {{
    Write-Log "Fuehre Setup-Installer aus: $dlFile"
    $lbl.Text = "Fuehre Setup-Installer fuer v$newVer aus..."
    [System.Windows.Forms.Application]::DoEvents()
    $setupProc = Start-Process -FilePath $dlFile -ArgumentList "/SILENT /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS" -PassThru -Wait
    Write-Log "Setup beendet mit ExitCode: $($setupProc.ExitCode)"
    $copySuccess = ($setupProc.ExitCode -eq 0)
}} else {{
    $targets = [System.Collections.Generic.List[string]]::new()
    $targets.Add($targetExe)
    $progFilesExe = Join-Path $env:ProgramFiles 'ModernExplorer-Rust\ModernExplorer-Rust.exe'
    if ((Test-Path -LiteralPath (Split-Path -Parent $progFilesExe)) -and ($progFilesExe.ToLower() -ne $targetExe.ToLower())) {{
        $targets.Add($progFilesExe)
    }}

    foreach ($dest in $targets) {{
        Write-Log "Aktualisiere Zieldatei: $dest"
        $oldBak = "$dest.old"
        if (Test-Path -LiteralPath $oldBak) {{ Remove-Item -LiteralPath $oldBak -Force -ErrorAction SilentlyContinue }}
        
        $destCopied = $false
        for ($attempt = 1; $attempt -le 15; $attempt++) {{
            try {{
                Copy-Item -LiteralPath $dlFile -Destination $dest -Force -ErrorAction Stop
                $destCopied = $true
                Write-Log "Copy-Item erfolgreich fuer $dest bei Versuch $attempt"
                break
            }} catch {{
                Write-Log "Versuch $attempt fehlgeschlagen fuer $dest: $($_.Exception.Message)"
                try {{
                    if (Test-Path -LiteralPath $dest) {{
                        Move-Item -LiteralPath $dest -Destination $oldBak -Force -ErrorAction Stop
                        Write-Log "Move-Item nach $oldBak erfolgreich"
                    }}
                    Copy-Item -LiteralPath $dlFile -Destination $dest -Force -ErrorAction Stop
                    $destCopied = $true
                    Write-Log "Copy-Item nach Verschieben erfolgreich fuer $dest"
                    break
                }} catch {{
                    Write-Log "Move/Copy Fallback fehlgeschlagen: $($_.Exception.Message)"
                    [System.Windows.Forms.Application]::DoEvents()
                    Start-Sleep -Milliseconds 300
                }}
            }}
        }}
        if ($dest.ToLower() -eq $targetExe.ToLower() -and $destCopied) {{
            $copySuccess = $true
        }}
    }}
}}

# 3. Registrierungseintraege aktualisieren
try {{
    foreach ($hive in @('HKLM:', 'HKCU:')) {{
        $regPath = "$hive\Software\Microsoft\Windows\CurrentVersion\Uninstall\{{C94E7A12-8D41-4F3A-9B25-7E1A3D94F6B2}}_is1"
        if (Test-Path -LiteralPath $regPath) {{
            Set-ItemProperty -LiteralPath $regPath -Name 'DisplayVersion' -Value $newVer -ErrorAction SilentlyContinue
            Set-ItemProperty -LiteralPath $regPath -Name 'DisplayName' -Value "Modern Explorer Studio (Rust) $newVer (win-x64)" -ErrorAction SilentlyContinue
            Write-Log "Registry aktualisiert in $hive"
        }}
    }}
}} catch {{
    Write-Log "Registry-Aktualisierung uebersprungen: $($_.Exception.Message)"
}}

$bar.Value = 100
$lbl.Text = "Update abgeschlossen! Starte ModernExplorer Studio v$newVer..."
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 400

# 4. Anwendung neu starten
if (-not $isSetup) {{
    Write-Log "Starte Zielanwendung neu: $targetExe in $targetDir"
    try {{
        Start-Process -FilePath $targetExe -WorkingDirectory $targetDir -ErrorAction Stop
        Write-Log "Start-Process erfolgreich."
    }} catch {{
        Write-Log "Start-Process fehlgeschlagen: $($_.Exception.Message) - Starte via Explorer..."
        Start-Process -FilePath "explorer.exe" -ArgumentList "`"$targetExe`""
    }}
}}

Write-Log "Update abgeschlossen."
$form.Close()
$form.Dispose()
"#
            );

            // Write with UTF-8 BOM so Windows PowerShell 5.1 parses strings reliably on any locale
            let mut script_bytes = vec![0xEF, 0xBB, 0xBF];
            script_bytes.extend_from_slice(ps_script.as_bytes());
            fs::write(&script_path, script_bytes)
                .map_err(|e| format!("Update-Skript konnte nicht erstellt werden: {e}"))?;

            Command::new("powershell.exe")
                .args([
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-STA",
                    "-WindowStyle",
                    "Hidden",
                    "-File",
                    &script_path.to_string_lossy(),
                ])
                .creation_flags(CREATE_NO_WINDOW)
                .spawn()
                .map_err(|e| format!("Fehler beim Starten des Updaters: {e}"))?;

            return Ok(download_path.to_string_lossy().to_string());
        }

        #[cfg(not(target_os = "windows"))]
        {
            let name_lower = asset.name.to_ascii_lowercase();
            if name_lower.ends_with(".dmg")
                || name_lower.ends_with(".pkg")
                || name_lower.ends_with(".deb")
                || name_lower.ends_with(".rpm")
            {
                let _ = open::that(&download_path);
                return Ok(download_path.to_string_lossy().to_string());
            }

            let script_path = update_dir.join("apply_rust_update.sh");
            let dl_str = download_path.to_string_lossy();
            let exe_str = current_exe.to_string_lossy();
            let sh_script = format!(
                "#!/bin/sh\nwhile kill -0 {pid} 2>/dev/null; do sleep 0.2; done\nsleep 0.3\ncp -f \"{dl_str}\" \"{exe_str}\"\nchmod +x \"{exe_str}\"\nnohup \"{exe_str}\" >/dev/null 2>&1 &\n"
            );
            let _ = fs::write(&script_path, sh_script);
            let _ = Command::new("/bin/sh").arg(&script_path).spawn();
            Ok(download_path.to_string_lossy().to_string())
        }
    }

    /// Formats raw GitHub Markdown release notes into a clean, multi-line readable text
    /// suitable for the multi-line scrollable Release Notes control in the Update & About dialogs.
    pub fn format_release_notes_for_display(raw_body: &str) -> String {
        let trimmed = raw_body.trim();
        if trimmed.is_empty() {
            return String::new();
        }
        let link_re = Regex::new(r"\[([^\]]+)\]\(([^)]+)\)").unwrap();
        let normalized = trimmed.replace("\r\n", "\n").replace('\r', "\n");
        let mut lines_out = Vec::new();
        let mut prev_blank = false;

        for raw_line in normalized.lines() {
            let mut line = raw_line.trim_end().to_string();
            let t = line.trim();
            if t.is_empty() {
                if !prev_blank && !lines_out.is_empty() {
                    lines_out.push(String::new());
                    prev_blank = true;
                }
                continue;
            }
            if t == "---" || t == "***" || t == "___" {
                continue;
            }
            prev_blank = false;

            // Strip Markdown links [label](url) -> label
            line = link_re.replace_all(&line, "$1").to_string();

            let t_clean = line.trim();
            if let Some(h3) = t_clean.strip_prefix("### ") {
                lines_out.push(format!("▸ {}", h3.replace("**", "").replace('`', "")));
            } else if let Some(h2) = t_clean.strip_prefix("## ") {
                lines_out.push(format!("━━ {} ━━", h2.replace("**", "").replace('`', "")));
            } else if let Some(h1) = t_clean.strip_prefix("# ") {
                lines_out.push(format!("━━ {} ━━", h1.replace("**", "").replace('`', "")));
            } else if t_clean.starts_with("- ") || t_clean.starts_with("* ") {
                let indent = line.len() - line.trim_start().len();
                let bullet = if indent >= 2 { "   ◦ " } else { "• " };
                let body = t_clean[2..].replace("**", "").replace('`', "");
                lines_out.push(format!("{}{}", bullet, body));
            } else {
                lines_out.push(t_clean.replace("**", "").replace('`', ""));
            }
        }

        lines_out.join("\n").trim().to_string()
    }

    pub fn open_release_in_browser(url: Option<&str>) {
        let target = url
            .filter(|s| !s.trim().is_empty())
            .unwrap_or(Self::RELEASES_URL);
        let _ = open::that(target);
    }
}


