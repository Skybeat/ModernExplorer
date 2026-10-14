use regex::Regex;
use serde::{Deserialize, Deserializer, Serialize};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
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
            // Priority 1: Portable Standalone Executable (.exe) for direct in-place replacement
            let portable_exe = format!("modernexplorer-rust-windows-{arch}-portable.exe");
            if let Some(a) = pool
                .iter()
                .find(|a| a.name.to_ascii_lowercase() == portable_exe)
            {
                return Some(*a);
            }
            // Priority 2: Portable ZIP Archive (contains ModernExplorer-Rust.exe)
            let portable_zip = format!("modernexplorer-rust-windows-{arch}-portable.zip");
            if let Some(a) = pool
                .iter()
                .find(|a| a.name.to_ascii_lowercase() == portable_zip)
            {
                return Some(*a);
            }
            // Priority 3: Fallback Setup.exe
            let setup_exe = format!("modernexplorer-rust-setup-win-{arch}.exe");
            if let Some(a) = pool
                .iter()
                .find(|a| a.name.to_ascii_lowercase() == setup_exe)
            {
                return Some(*a);
            }
        } else if cfg!(target_os = "macos") {
            let tar_name = format!("modernexplorer-rust-macos-{arch}-portable.tar.gz");
            let zip_name = format!("modernexplorer-rust-macos-{arch}-portable.zip");
            let pkg_name = format!("modernexplorer-rust-setup-macos-{arch}.pkg");
            let dmg_name = format!("modernexplorer-rust-setup-macos-{arch}.dmg");
            for want in [tar_name, zip_name, pkg_name, dmg_name] {
                if let Some(a) = pool.iter().find(|a| a.name.to_ascii_lowercase() == want) {
                    return Some(*a);
                }
            }
        } else if cfg!(target_os = "linux") {
            let tar_name = format!("modernexplorer-rust-linux-{arch}-portable.tar.gz");
            let zip_name = format!("modernexplorer-rust-linux-{arch}-portable.zip");
            let appimage_name = format!("modernexplorer-rust-linux-{arch}-portable.appimage");
            let sh_name = format!("modernexplorer-rust-installer-linux-{arch}.sh");
            let deb_name = format!("modernexplorer-rust-setup-linux-{arch}.deb");
            for want in [tar_name, zip_name, appimage_name, sh_name, deb_name] {
                if let Some(a) = pool.iter().find(|a| a.name.to_ascii_lowercase() == want) {
                    return Some(*a);
                }
            }
        }

        pool.first().copied()
    }

    pub fn unpack_binary_if_archive(
        downloaded_file: &Path,
        target_dir: &Path,
    ) -> Result<PathBuf, String> {
        let name_lower = downloaded_file
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or("")
            .to_ascii_lowercase();

        if name_lower.ends_with(".zip") {
            let file = fs::File::open(downloaded_file)
                .map_err(|e| format!("ZIP-Datei konnte nicht geöffnet werden: {e}"))?;
            let mut archive =
                zip::ZipArchive::new(file).map_err(|e| format!("Ungültiges ZIP-Archiv: {e}"))?;

            let mut match_index: Option<usize> = None;
            for i in 0..archive.len() {
                if let Ok(entry) = archive.by_index(i) {
                    let entry_name = entry.name().to_ascii_lowercase();
                    if entry_name.ends_with("modernexplorer-rust.exe")
                        || entry_name.ends_with("modernexplorer-rust")
                    {
                        match_index = Some(i);
                        break;
                    }
                }
            }

            let idx = match_index.ok_or_else(|| {
                "Keine ModernExplorer-Rust-Binärdatei im ZIP-Archiv gefunden.".to_string()
            })?;

            let mut entry = archive
                .by_index(idx)
                .map_err(|e| format!("Fehler beim Zugriff auf ZIP-Eintrag: {e}"))?;

            let out_ext = if cfg!(target_os = "windows") {
                ".exe"
            } else {
                ""
            };
            let extracted_path = target_dir.join(format!("ModernExplorer-Rust-extracted{out_ext}"));
            let mut out = fs::File::create(&extracted_path)
                .map_err(|e| format!("Extrahierte Datei konnte nicht erstellt werden: {e}"))?;
            io::copy(&mut entry, &mut out)
                .map_err(|e| format!("Fehler beim Entpacken der Binärdatei: {e}"))?;
            drop(out);

            #[cfg(unix)]
            {
                use std::os::unix::fs::PermissionsExt;
                let _ = fs::set_permissions(&extracted_path, fs::Permissions::from_mode(0o755));
            }

            Ok(extracted_path)
        } else if name_lower.ends_with(".tar.gz") || name_lower.ends_with(".tgz") {
            let file = fs::File::open(downloaded_file)
                .map_err(|e| format!("Tar.gz-Datei konnte nicht geöffnet werden: {e}"))?;
            let gz = flate2::read::GzDecoder::new(file);
            let mut archive = tar::Archive::new(gz);

            let mut extracted_path: Option<PathBuf> = None;
            for entry_res in archive.entries().map_err(|e| format!("Tar-Fehler: {e}"))? {
                let mut entry = entry_res.map_err(|e| format!("Tar-Eintrag Fehler: {e}"))?;
                let path = entry.path().map_err(|e| format!("Pfadfehler: {e}"))?;
                let path_str = path.to_string_lossy().to_ascii_lowercase();
                if path_str.ends_with("modernexplorer-rust")
                    || path_str.ends_with("modernexplorer-rust.exe")
                {
                    let out_path = target_dir.join("ModernExplorer-Rust-extracted");
                    let mut out = fs::File::create(&out_path).map_err(|e| {
                        format!("Extrahierte Datei konnte nicht erstellt werden: {e}")
                    })?;
                    io::copy(&mut entry, &mut out)
                        .map_err(|e| format!("Fehler beim Entpacken der Tar-Binärdatei: {e}"))?;
                    drop(out);

                    #[cfg(unix)]
                    {
                        use std::os::unix::fs::PermissionsExt;
                        let _ = fs::set_permissions(&out_path, fs::Permissions::from_mode(0o755));
                    }
                    extracted_path = Some(out_path);
                    break;
                }
            }

            extracted_path.ok_or_else(|| {
                "Keine ModernExplorer-Rust-Binärdatei im Tar.gz-Archiv gefunden.".to_string()
            })
        } else {
            #[cfg(unix)]
            {
                use std::os::unix::fs::PermissionsExt;
                let _ = fs::set_permissions(downloaded_file, fs::Permissions::from_mode(0o755));
            }
            Ok(downloaded_file.to_path_buf())
        }
    }

    pub fn restart_application() -> Result<(), String> {
        let current_exe = std::env::current_exe()
            .map_err(|e| format!("Aktueller Anwendungs-Pfad nicht ermittelbar: {e}"))?;
        let current_dir = current_exe
            .parent()
            .map(|p| p.to_path_buf())
            .unwrap_or_else(|| PathBuf::from("."));

        #[cfg(target_os = "windows")]
        {
            use std::os::windows::process::CommandExt;
            const DETACHED_PROCESS: u32 = 0x00000008;
            const CREATE_NEW_PROCESS_GROUP: u32 = 0x00000200;

            Command::new(&current_exe)
                .current_dir(&current_dir)
                .creation_flags(DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP)
                .spawn()
                .map_err(|e| format!("Fehler beim Neustart der Anwendung: {e}"))?;
        }

        #[cfg(not(target_os = "windows"))]
        {
            Command::new(&current_exe)
                .current_dir(&current_dir)
                .spawn()
                .map_err(|e| format!("Fehler beim Neustart der Anwendung: {e}"))?;
        }

        Ok(())
    }

    #[cfg(target_os = "windows")]
    pub fn update_windows_registry(new_ver: &str) {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;

        let hives = ["HKLM", "HKCU"];
        for hive in hives {
            let key = format!(
                r"{hive}\Software\Microsoft\Windows\CurrentVersion\Uninstall\{{C94E7A12-8D41-4F3A-9B25-7E1A3D94F6B2}}_is1"
            );
            let _ = Command::new("reg.exe")
                .args(["add", &key, "/v", "DisplayVersion", "/t", "REG_SZ", "/d", new_ver, "/f"])
                .creation_flags(CREATE_NO_WINDOW)
                .output();
            let display_name = format!("Modern Explorer Studio (Rust) {new_ver} (win-x64)");
            let _ = Command::new("reg.exe")
                .args(["add", &key, "/v", "DisplayName", "/t", "REG_SZ", "/d", &display_name, "/f"])
                .creation_flags(CREATE_NO_WINDOW)
                .output();
        }
    }

    pub fn cleanup_old_versions() {
        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(parent) = current_exe.parent() {
                if let Ok(entries) = fs::read_dir(parent) {
                    for entry in entries.flatten() {
                        let path = entry.path();
                        if let Some(file_name) = path.file_name().and_then(|n| n.to_str()) {
                            let lower = file_name.to_ascii_lowercase();
                            if lower.ends_with(".old")
                                || lower.ends_with(".relocated")
                                || lower.ends_with(".selfdelete")
                                || lower.ends_with(".testrename")
                                || (file_name.starts_with('.') && lower.ends_with(".temp"))
                            {
                                let _ = fs::remove_file(&path);
                            }
                        }
                    }
                }
            }
        }

        let temp_base = std::env::temp_dir().join("ModernExplorer_Rust_Update");
        let _ = fs::remove_dir_all(&temp_base);
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
        // 1. Ggf. Binärdatei aus Archiv (.zip, .tar.gz) entpacken
        let new_binary = Self::unpack_binary_if_archive(&download_path, &update_dir)?;

        // 2. In-Place-Austausch der laufenden Binärdatei via self_replace
        self_replace::self_replace(&new_binary).map_err(|e| {
            format!("Fehler beim In-Place-Austausch der Anwendungsdatei via self-replace: {e}")
        })?;

        // 3. Auf Windows: Registry DisplayVersion aktualisieren (best-effort)
        #[cfg(target_os = "windows")]
        {
            let ver_clean = release.tag_name.trim_start_matches(['v', 'V']).to_string();
            Self::update_windows_registry(&ver_clean);
        }

        Ok(download_path.to_string_lossy().to_string())
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


