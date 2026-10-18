use crate::power_tools_v2::UserCommandItem;
use crate::session::FavoriteEntry;
use crate::settings::{AppSettings, FileTypeColorRule};
use chrono::{DateTime, Local};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};

use std::collections::HashMap;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CloudProviderInfo {
    pub id: String,
    pub name: String,
    pub icon: String,
    pub detected_root: Option<String>,
    pub backup_dir: Option<String>,
    pub is_available: bool,
    #[serde(default)]
    pub detected: bool,
    #[serde(default)]
    pub is_custom_configured: bool,
    pub backup_count: usize,
    pub latest_backup_date: Option<String>,
    #[serde(default)]
    pub status_message: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BackupFileInfo {
    pub file_name: String,
    pub file_path: String,
    pub provider_id: String,
    pub provider_name: String,
    pub size_bytes: u64,
    pub created_at: String,
    pub app_version: String,
    pub device_name: String,
    pub description: Option<String>,
    pub has_favorites: bool,
    pub has_user_commands: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SettingsBackupPackage {
    pub schema_type: String, // "modern_explorer_settings_backup"
    pub schema_version: u32,
    pub app_version: String,
    pub created_at: String,
    pub device_name: String,
    pub backup_type: String, // "manual", "cloud_auto", "export_file"
    pub description: Option<String>,
    pub settings: AppSettings,
    pub favorites: Option<Vec<FavoriteEntry>>,
    pub user_commands: Option<Vec<UserCommandItem>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ColorThemePackage {
    pub schema_type: String, // "modern_explorer_color_theme"
    pub schema_version: u32,
    pub name: String,
    pub description: Option<String>,
    pub author: Option<String>,
    pub app_version: String,
    pub created_at: String,

    // File List & Focus Colors
    pub custom_file_list_colors_enabled: bool,
    pub active_panel_border_color_hex: String,
    pub invert_cursor: bool,
    pub selection_border_color_hex: String,
    pub selection_bg_color_hex: String,
    pub selection_text_color_hex: String,
    pub marked_item_text_color_hex: String,
    pub marked_item_bg_color_hex: String,

    pub file_list_bg_color_hex: String,
    pub enable_zebra_stripes: bool,
    pub file_list_alt_bg_color_hex: String,
    pub show_grid_lines: bool,
    pub grid_line_color_hex: String,
    pub file_list_text_color_hex: String,
    pub folder_text_color_hex: String,
    pub hidden_item_text_color_hex: String,
    pub readonly_item_text_color_hex: String,

    // File Type Colors & Rules
    pub enable_filetype_colors: bool,
    pub filetype_color_rules: Vec<FileTypeColorRule>,

    // Treemap Heatmap Colors
    pub treemap_video_media_color_hex: String,
    pub treemap_archive_image_color_hex: String,
    pub treemap_virtual_disk_system_color_hex: String,
    pub treemap_code_document_color_hex: String,
    pub treemap_picture_graphic_color_hex: String,
    pub treemap_binary_executable_color_hex: String,
    pub treemap_other_temp_color_hex: String,
}

pub struct CloudBackupService;

impl CloudBackupService {
    pub const SCHEMA_TYPE_BACKUP: &'static str = "modern_explorer_settings_backup";
    pub const SCHEMA_TYPE_THEME: &'static str = "modern_explorer_color_theme";
    pub const CURRENT_SCHEMA_VERSION: u32 = 1;

    /// Detect all available Cloud Storage providers on the current OS
    #[cfg(windows)]
    fn query_registry_sz(key: &str, value_name: &str) -> Option<String> {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        let output = std::process::Command::new("reg")
            .args(["query", key, "/v", value_name])
            .creation_flags(CREATE_NO_WINDOW)
            .output()
            .ok()?;
        if output.status.success() {
            let text = String::from_utf8_lossy(&output.stdout);
            for line in text.lines() {
                if let Some(pos) = line.find("REG_SZ") {
                    let val = line[pos + 6..].trim();
                    if !val.is_empty() {
                        return Some(val.to_string());
                    }
                } else if let Some(pos) = line.find("REG_EXPAND_SZ") {
                    let val = line[pos + 13..].trim();
                    if !val.is_empty() {
                        return Some(val.to_string());
                    }
                }
            }
        }
        None
    }

    #[cfg(not(windows))]
    fn query_registry_sz(_key: &str, _value_name: &str) -> Option<String> {
        None
    }

    /// Detect all available Cloud Storage providers on the current OS
    pub fn detect_providers(
        custom_path: Option<&str>,
        configured_paths: Option<&HashMap<String, String>>,
    ) -> Vec<CloudProviderInfo> {
        let mut list = Vec::new();

        let get_cfg = |id: &str| -> Option<&str> {
            configured_paths.and_then(|cp| cp.get(id)).map(|s| s.as_str())
        };

        // 1. Microsoft OneDrive
        let onedrive_cfg = get_cfg("onedrive");
        list.push(Self::probe_provider(
            "onedrive",
            "Microsoft OneDrive",
            "☁️",
            Self::find_onedrive_root(onedrive_cfg),
            onedrive_cfg.is_some(),
        ));

        // 2. Google Drive
        let googledrive_cfg = get_cfg("googledrive");
        list.push(Self::probe_provider(
            "googledrive",
            "Google Drive",
            "🔷",
            Self::find_google_drive_root(googledrive_cfg),
            googledrive_cfg.is_some(),
        ));

        // 3. Apple iCloud Drive
        let icloud_cfg = get_cfg("iclouddrive");
        list.push(Self::probe_provider(
            "iclouddrive",
            "Apple iCloud Drive",
            "🍏",
            Self::find_icloud_drive_root(icloud_cfg),
            icloud_cfg.is_some(),
        ));

        // 4. Dropbox
        let dropbox_cfg = get_cfg("dropbox");
        list.push(Self::probe_provider(
            "dropbox",
            "Dropbox",
            "📦",
            Self::find_dropbox_root(dropbox_cfg),
            dropbox_cfg.is_some(),
        ));

        // 5. Nextcloud / ownCloud
        let nextcloud_cfg = get_cfg("nextcloud");
        list.push(Self::probe_provider(
            "nextcloud",
            "Nextcloud / ownCloud",
            "🖧",
            Self::find_nextcloud_root(nextcloud_cfg),
            nextcloud_cfg.is_some(),
        ));

        // 6. Custom Cloud / Network directory
        let effective_custom = custom_path.or_else(|| get_cfg("custom"));
        let custom_root = effective_custom.and_then(|p| {
            let pb = PathBuf::from(p);
            if pb.is_dir() {
                Some(pb)
            } else {
                None
            }
        });
        list.push(Self::probe_provider(
            "custom",
            "Benutzerdefinierter Cloud- / Netzwerk-Ordner",
            "📁",
            custom_root,
            effective_custom.is_some(),
        ));

        list
    }

    fn probe_provider(
        id: &str,
        name: &str,
        icon: &str,
        root_opt: Option<PathBuf>,
        is_custom_configured: bool,
    ) -> CloudProviderInfo {
        let (is_available, detected_root, backup_dir, backup_count, latest_backup_date, status_message) =
            if let Some(root) = root_opt {
                let b_dir = root.join("ModernExplorer").join("Backups");
                let mut count = 0;
                let mut latest_date = None;

                if b_dir.is_dir() {
                    if let Ok(entries) = fs::read_dir(&b_dir) {
                        let mut newest_time: Option<std::time::SystemTime> = None;
                        for entry in entries.flatten() {
                            let p = entry.path();
                            if p.is_file() && p.extension().map_or(false, |e| e == "json") {
                                count += 1;
                                if let Ok(meta) = entry.metadata() {
                                    if let Ok(mtime) = meta.modified() {
                                        if newest_time.map_or(true, |t| mtime > t) {
                                            newest_time = Some(mtime);
                                            let dt: DateTime<Local> = DateTime::from(mtime);
                                            latest_date = Some(dt.format("%d.%m.%Y %H:%M").to_string());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                let status = if is_custom_configured {
                    format!("Manuell eingerichtet: {} ({} Backups)", root.display(), count)
                } else {
                    format!("Automatisch erkannt: {} ({} Backups)", root.display(), count)
                };

                (
                    true,
                    Some(root.to_string_lossy().to_string()),
                    Some(b_dir.to_string_lossy().to_string()),
                    count,
                    latest_date,
                    status,
                )
            } else {
                (
                    false,
                    None,
                    None,
                    0,
                    None,
                    "Nicht auf diesem PC gefunden (Pfad manuell zuweisen)".to_string(),
                )
            };

        CloudProviderInfo {
            id: id.to_string(),
            name: name.to_string(),
            icon: icon.to_string(),
            detected_root,
            backup_dir,
            is_available,
            detected: is_available,
            is_custom_configured,
            backup_count,
            latest_backup_date,
            status_message,
        }
    }

    pub fn find_onedrive_root(custom_override: Option<&str>) -> Option<PathBuf> {
        // 1. Check custom override if provided
        if let Some(c) = custom_override {
            let p = PathBuf::from(c);
            if p.is_dir() {
                return Some(p);
            }
        }

        // 2. Environment variables standard on Windows
        for env_var in &["OneDrive", "OneDriveConsumer", "OneDriveCommercial"] {
            if let Ok(val) = std::env::var(env_var) {
                let p = PathBuf::from(val);
                if p.is_dir() {
                    return Some(p);
                }
            }
        }

        // 3. Windows Registry lookup
        #[cfg(windows)]
        {
            let reg_keys = [
                r"HKCU\Software\Microsoft\OneDrive\Accounts\Personal",
                r"HKCU\Software\Microsoft\OneDrive\Accounts\Business1",
                r"HKCU\Software\Microsoft\OneDrive\Accounts\Business2",
                r"HKCU\Software\Microsoft\OneDrive",
            ];
            for rk in &reg_keys {
                if let Some(val) = Self::query_registry_sz(rk, "UserFolder") {
                    let p = PathBuf::from(&val);
                    if p.is_dir() {
                        return Some(p);
                    }
                }
            }
        }

        // 4. User profile home directory
        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("OneDrive"),
                home.join("OneDrive - Personal"),
                home.join("OneDrive - Consumer"),
            ];
            for c in &candidates {
                if c.is_dir() {
                    return Some(c.clone());
                }
            }
            // Scan home for any directory starting with "OneDrive"
            if let Ok(entries) = fs::read_dir(&home) {
                for entry in entries.flatten() {
                    let p = entry.path();
                    if p.is_dir() {
                        if let Some(name) = p.file_name().and_then(|n| n.to_str()) {
                            if name.to_lowercase().starts_with("onedrive") {
                                return Some(p);
                            }
                        }
                    }
                }
            }
        }

        // 5. Root drives (e.g. C:\OneDrive, D:\OneDrive)
        for drive in &["C:\\OneDrive", "D:\\OneDrive", "E:\\OneDrive"] {
            let p = PathBuf::from(drive);
            if p.is_dir() {
                return Some(p);
            }
        }

        None
    }

    pub fn find_google_drive_root(custom_override: Option<&str>) -> Option<PathBuf> {
        if let Some(c) = custom_override {
            let p = PathBuf::from(c);
            if p.is_dir() {
                return Some(p);
            }
        }

        // 1. Home directory
        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("Google Drive").join("My Drive"),
                home.join("Google Drive").join("Meine Ablage"),
                home.join("Google Drive"),
                home.join("GoogleDrive"),
            ];
            for c in &candidates {
                if c.is_dir() {
                    return Some(c.clone());
                }
            }
        }

        // 2. Google Drive mounted virtual drives on Windows (G:\, H:\, F:\)
        for letter in &["G", "H", "F", "D", "E"] {
            let my_drive = format!("{}:\\My Drive", letter);
            let meine_ablage = format!("{}:\\Meine Ablage", letter);
            for p_str in &[&my_drive, &meine_ablage] {
                let p = PathBuf::from(p_str);
                if p.is_dir() {
                    return Some(p);
                }
            }
            let root_p = PathBuf::from(format!("{}:\\", letter));
            if root_p.is_dir()
                && (root_p.join(".tmp.drivedownload").exists()
                    || root_p.join("My Drive").exists()
                    || root_p.join("Meine Ablage").exists())
            {
                return Some(root_p);
            }
        }

        None
    }

    pub fn find_icloud_drive_root(custom_override: Option<&str>) -> Option<PathBuf> {
        if let Some(c) = custom_override {
            let p = PathBuf::from(c);
            if p.is_dir() {
                return Some(p);
            }
        }

        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("iCloudDrive"),
                home.join("iCloud Drive"),
                home.join("Library/Mobile Documents/com~apple~CloudDocs"),
                home.join("Pictures").join("iCloud Photos"),
            ];
            for c in &candidates {
                if c.is_dir() {
                    return Some(c.clone());
                }
            }

            // Windows Store iCloud App in LocalAppData
            if let Ok(local_app_data) = std::env::var("LOCALAPPDATA") {
                let pkg_dir = PathBuf::from(local_app_data).join("Packages");
                if pkg_dir.is_dir() {
                    if let Ok(entries) = fs::read_dir(&pkg_dir) {
                        for entry in entries.flatten() {
                            let p = entry.path();
                            if p.is_dir() {
                                if let Some(n) = p.file_name().and_then(|s| s.to_str()) {
                                    if n.starts_with("AppleInc.iCloud") {
                                        let icloud_dir = p
                                            .join("LocalCache")
                                            .join("Local")
                                            .join("Apple Inc")
                                            .join("iCloudDrive");
                                        if icloud_dir.is_dir() {
                                            return Some(icloud_dir);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        None
    }

    pub fn find_dropbox_root(custom_override: Option<&str>) -> Option<PathBuf> {
        if let Some(c) = custom_override {
            let p = PathBuf::from(c);
            if p.is_dir() {
                return Some(p);
            }
        }

        // Check Dropbox info.json
        let json_paths = [
            std::env::var("LOCALAPPDATA").ok().map(|d| PathBuf::from(d).join("Dropbox").join("info.json")),
            std::env::var("APPDATA").ok().map(|d| PathBuf::from(d).join("Dropbox").join("info.json")),
            dirs::home_dir().map(|h| h.join(".dropbox").join("info.json")),
        ];
        for jp_opt in &json_paths {
            if let Some(jp) = jp_opt {
                if jp.is_file() {
                    if let Ok(content) = fs::read_to_string(jp) {
                        if let Ok(val) = serde_json::from_str::<serde_json::Value>(&content) {
                            if let Some(path_str) = val.get("personal").and_then(|p| p.get("path")).and_then(|p| p.as_str()) {
                                let pb = PathBuf::from(path_str);
                                if pb.is_dir() {
                                    return Some(pb);
                                }
                            }
                            if let Some(path_str) = val.get("business").and_then(|p| p.get("path")).and_then(|p| p.as_str()) {
                                let pb = PathBuf::from(path_str);
                                if pb.is_dir() {
                                    return Some(pb);
                                }
                            }
                        }
                    }
                }
            }
        }

        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("Dropbox"),
                home.join("Dropbox (Personal)"),
            ];
            for c in &candidates {
                if c.is_dir() {
                    return Some(c.clone());
                }
            }
        }
        None
    }

    pub fn find_nextcloud_root(custom_override: Option<&str>) -> Option<PathBuf> {
        if let Some(c) = custom_override {
            let p = PathBuf::from(c);
            if p.is_dir() {
                return Some(p);
            }
        }
        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("Nextcloud"),
                home.join("ownCloud"),
            ];
            for c in &candidates {
                if c.is_dir() {
                    return Some(c.clone());
                }
            }
        }
        None
    }

    /// Test read and write access to a provider directory
    pub fn test_provider(
        provider_id: &str,
        path_override: Option<&str>,
        configured_paths: Option<&HashMap<String, String>>,
    ) -> (bool, String) {
        let root_opt = if let Some(po) = path_override {
            let pb = PathBuf::from(po);
            if pb.is_dir() {
                Some(pb)
            } else {
                return (false, format!("Der angegebene Pfad existiert nicht: {}", po));
            }
        } else {
            let get_cfg = || configured_paths.and_then(|cp| cp.get(provider_id)).map(|s| s.as_str());
            match provider_id {
                "onedrive" => Self::find_onedrive_root(get_cfg()),
                "googledrive" => Self::find_google_drive_root(get_cfg()),
                "iclouddrive" => Self::find_icloud_drive_root(get_cfg()),
                "dropbox" => Self::find_dropbox_root(get_cfg()),
                "nextcloud" => Self::find_nextcloud_root(get_cfg()),
                "custom" => get_cfg().map(PathBuf::from).filter(|p| p.is_dir()),
                _ => None,
            }
        };

        let root = match root_opt {
            Some(r) => r,
            None => return (false, format!("Kein gültiger Ordner für '{}' gefunden.", provider_id)),
        };

        let backup_dir = root.join("ModernExplorer").join("Backups");
        if let Err(e) = fs::create_dir_all(&backup_dir) {
            return (false, format!("Konnte Backup-Verzeichnis '{}' nicht erstellen: {}", backup_dir.display(), e));
        }

        // Test writing a temporary file
        let test_file = backup_dir.join(".modernexplorer_sync_test");
        let test_payload = format!("test_{}", Local::now().timestamp_millis());
        if let Err(e) = fs::write(&test_file, &test_payload) {
            return (false, format!("Schreibzugriff verweigert in '{}': {}", backup_dir.display(), e));
        }
        let read_back = fs::read_to_string(&test_file).unwrap_or_default();
        let _ = fs::remove_file(&test_file);

        if read_back != test_payload {
            return (false, "Schreib-/Lesetest nicht übereinstimmend.".to_string());
        }

        (true, format!("Verbindung erfolgreich! Ordner ist beschreibbar: {}", backup_dir.display()))
    }

    /// Resolve target directory for backup creation
    pub fn get_backup_dir(
        provider_id: &str,
        custom_path: Option<&str>,
        configured_paths: Option<&HashMap<String, String>>,
    ) -> Result<PathBuf, String> {
        let get_cfg = || configured_paths.and_then(|cp| cp.get(provider_id)).map(|s| s.as_str());
        let root = match provider_id {
            "onedrive" => Self::find_onedrive_root(get_cfg())
                .ok_or_else(|| "Microsoft OneDrive wurde auf diesem System nicht gefunden. Bitte konfigurieren Sie den Pfad im Setup.".to_string())?,
            "googledrive" => Self::find_google_drive_root(get_cfg())
                .ok_or_else(|| "Google Drive wurde auf diesem System nicht gefunden. Bitte konfigurieren Sie den Pfad im Setup.".to_string())?,
            "iclouddrive" => Self::find_icloud_drive_root(get_cfg())
                .ok_or_else(|| "Apple iCloud Drive wurde auf diesem System nicht gefunden. Bitte konfigurieren Sie den Pfad im Setup.".to_string())?,
            "dropbox" => Self::find_dropbox_root(get_cfg())
                .ok_or_else(|| "Dropbox wurde auf diesem System nicht gefunden. Bitte konfigurieren Sie den Pfad im Setup.".to_string())?,
            "nextcloud" => Self::find_nextcloud_root(get_cfg())
                .ok_or_else(|| "Nextcloud / ownCloud wurde auf diesem System nicht gefunden. Bitte konfigurieren Sie den Pfad im Setup.".to_string())?,
            "custom" => {
                let eff = custom_path.or_else(get_cfg);
                if let Some(cp) = eff {
                    let pb = PathBuf::from(cp);
                    if pb.is_dir() {
                        pb
                    } else {
                        return Err(format!("Benutzerdefinierter Pfad existiert nicht: {}", cp));
                    }
                } else {
                    return Err("Kein benutzerdefinierter Cloud-Pfad angegeben.".to_string());
                }
            }
            _ => return Err(format!("Unbekannter Cloud-Provider: {}", provider_id)),
        };

        let backup_dir = root.join("ModernExplorer").join("Backups");
        fs::create_dir_all(&backup_dir)
            .map_err(|e| format!("Konnte Backup-Verzeichnis '{}' nicht erstellen: {}", backup_dir.display(), e))?;
        Ok(backup_dir)
    }

    /// Create and save a settings backup package into the chosen cloud provider
    pub fn create_cloud_backup(
        provider_id: &str,
        custom_path: Option<&str>,
        configured_paths: Option<&HashMap<String, String>>,
        settings: &AppSettings,
        favorites: Option<Vec<FavoriteEntry>>,
        user_commands: Option<Vec<UserCommandItem>>,
        description: Option<String>,
        app_version: &str,
    ) -> Result<BackupFileInfo, String> {
        let target_dir = Self::get_backup_dir(provider_id, custom_path, configured_paths)?;
        let device_name = whoami_device_name();
        let now = Local::now();
        let timestamp_str = now.format("%Y-%m-%d %H:%M:%S").to_string();
        let file_timestamp = now.format("%Y%m%d_%H%M%S").to_string();

        let pkg = SettingsBackupPackage {
            schema_type: Self::SCHEMA_TYPE_BACKUP.to_string(),
            schema_version: Self::CURRENT_SCHEMA_VERSION,
            app_version: app_version.to_string(),
            created_at: timestamp_str.clone(),
            device_name: device_name.clone(),
            backup_type: "cloud_sync".to_string(),
            description: description.clone(),
            settings: settings.clone(),
            favorites: favorites.clone(),
            user_commands: user_commands.clone(),
        };

        let json_text = serde_json::to_string_pretty(&pkg)
            .map_err(|e| format!("Fehler beim Serialisieren des Backups: {}", e))?;

        // 1. Save timestamped archive copy
        let filename = format!("ModernExplorer-Backup-{}.json", file_timestamp);
        let archive_path = target_dir.join(&filename);
        fs::write(&archive_path, &json_text)
            .map_err(|e| format!("Konnte Backup-Datei '{}' nicht schreiben: {}", archive_path.display(), e))?;

        // 2. Save latest-backup pointer for 1-click restore on fresh install
        let latest_path = target_dir.join("latest-settings-backup.json");
        let _ = fs::write(&latest_path, &json_text);

        let size = fs::metadata(&archive_path).map(|m| m.len()).unwrap_or(json_text.len() as u64);

        let provider_name = match provider_id {
            "onedrive" => "Microsoft OneDrive",
            "googledrive" => "Google Drive",
            "iclouddrive" => "Apple iCloud Drive",
            "dropbox" => "Dropbox",
            "nextcloud" => "Nextcloud / ownCloud",
            _ => "Cloud-Speicher",
        };

        Ok(BackupFileInfo {
            file_name: filename,
            file_path: archive_path.to_string_lossy().to_string(),
            provider_id: provider_id.to_string(),
            provider_name: provider_name.to_string(),
            size_bytes: size,
            created_at: timestamp_str,
            app_version: app_version.to_string(),
            device_name,
            description,
            has_favorites: favorites.is_some(),
            has_user_commands: user_commands.is_some(),
        })
    }

    /// List all backups in a provider directory or all available providers
    pub fn list_backups(
        provider_id_filter: Option<&str>,
        custom_path: Option<&str>,
        configured_paths: Option<&HashMap<String, String>>,
    ) -> Vec<BackupFileInfo> {
        let providers = Self::detect_providers(custom_path, configured_paths);
        let mut results = Vec::new();

        for prov in providers {
            if let Some(target_id) = provider_id_filter {
                if prov.id != target_id {
                    continue;
                }
            }
            if !prov.is_available {
                continue;
            }
            if let Some(b_dir_str) = prov.backup_dir {
                let dir = PathBuf::from(b_dir_str);
                if let Ok(entries) = fs::read_dir(dir) {
                    for entry in entries.flatten() {
                        let path = entry.path();
                        if path.is_file() && path.extension().map_or(false, |e| e == "json") {
                            let name = path.file_name().unwrap_or_default().to_string_lossy().to_string();
                            if let Ok(content) = fs::read_to_string(&path) {
                                if let Ok(pkg) = serde_json::from_str::<SettingsBackupPackage>(&content) {
                                    let size = entry.metadata().map(|m| m.len()).unwrap_or(0);
                                    results.push(BackupFileInfo {
                                        file_name: name,
                                        file_path: path.to_string_lossy().to_string(),
                                        provider_id: prov.id.clone(),
                                        provider_name: prov.name.clone(),
                                        size_bytes: size,
                                        created_at: pkg.created_at,
                                        app_version: pkg.app_version,
                                        device_name: pkg.device_name,
                                        description: pkg.description,
                                        has_favorites: pkg.favorites.is_some(),
                                        has_user_commands: pkg.user_commands.is_some(),
                                    });
                                } else if let Ok(_settings) = serde_json::from_str::<AppSettings>(&content) {
                                    // Raw AppSettings file format
                                    let size = entry.metadata().map(|m| m.len()).unwrap_or(0);
                                    let dt: DateTime<Local> = entry
                                        .metadata()
                                        .ok()
                                        .and_then(|m| m.modified().ok())
                                        .map(DateTime::from)
                                        .unwrap_or_else(Local::now);
                                    results.push(BackupFileInfo {
                                        file_name: name,
                                        file_path: path.to_string_lossy().to_string(),
                                        provider_id: prov.id.clone(),
                                        provider_name: prov.name.clone(),
                                        size_bytes: size,
                                        created_at: dt.format("%Y-%m-%d %H:%M:%S").to_string(),
                                        app_version: "Standard".to_string(),
                                        device_name: "Lokal".to_string(),
                                        description: Some("Reines Einstellungs-JSON".to_string()),
                                        has_favorites: false,
                                        has_user_commands: false,
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        // Sort descending by created_at or file_name
        results.sort_by(|a, b| b.created_at.cmp(&a.created_at));
        results
    }

    /// Load and validate a backup file from any path
    pub fn load_backup_from_file(file_path: &Path) -> Result<SettingsBackupPackage, String> {
        let content = fs::read_to_string(file_path)
            .map_err(|e| format!("Konnte Datei '{}' nicht lesen: {}", file_path.display(), e))?;

        if let Ok(pkg) = serde_json::from_str::<SettingsBackupPackage>(&content) {
            return Ok(pkg);
        }

        // Fallback: If user exported raw AppSettings JSON
        if let Ok(settings) = serde_json::from_str::<AppSettings>(&content) {
            return Ok(SettingsBackupPackage {
                schema_type: Self::SCHEMA_TYPE_BACKUP.to_string(),
                schema_version: Self::CURRENT_SCHEMA_VERSION,
                app_version: "1.9".to_string(),
                created_at: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                device_name: whoami_device_name(),
                backup_type: "raw_settings".to_string(),
                description: Some("Importiert aus einfachem Einstellungs-JSON".to_string()),
                settings,
                favorites: None,
                user_commands: None,
            });
        }

        Err("Die angegebene Datei ist kein gültiges ModernExplorer Einstellungs-Backup.".to_string())
    }

    /// Quick scan on fresh startup to discover if cloud backups exist
    pub fn scan_for_fresh_install_backups(
        configured_paths: Option<&HashMap<String, String>>,
    ) -> Option<BackupFileInfo> {
        let backups = Self::list_backups(None, None, configured_paths);
        backups.into_iter().next()
    }

    // ================= COLOR THEME EXPORT & IMPORT =================

    pub fn export_color_theme(
        settings: &AppSettings,
        name: &str,
        author: Option<&str>,
        description: Option<&str>,
        app_version: &str,
    ) -> ColorThemePackage {
        ColorThemePackage {
            schema_type: Self::SCHEMA_TYPE_THEME.to_string(),
            schema_version: Self::CURRENT_SCHEMA_VERSION,
            name: name.to_string(),
            description: description.map(|s| s.to_string()),
            author: author.map(|s| s.to_string()),
            app_version: app_version.to_string(),
            created_at: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),

            custom_file_list_colors_enabled: settings.custom_file_list_colors_enabled,
            active_panel_border_color_hex: settings.active_panel_border_color_hex.clone(),
            invert_cursor: settings.invert_cursor,
            selection_border_color_hex: settings.selection_border_color_hex.clone(),
            selection_bg_color_hex: settings.selection_bg_color_hex.clone(),
            selection_text_color_hex: settings.selection_text_color_hex.clone(),
            marked_item_text_color_hex: settings.marked_item_text_color_hex.clone(),
            marked_item_bg_color_hex: settings.marked_item_bg_color_hex.clone(),

            file_list_bg_color_hex: settings.file_list_bg_color_hex.clone(),
            enable_zebra_stripes: settings.enable_zebra_stripes,
            file_list_alt_bg_color_hex: settings.file_list_alt_bg_color_hex.clone(),
            show_grid_lines: settings.show_grid_lines,
            grid_line_color_hex: settings.grid_line_color_hex.clone(),
            file_list_text_color_hex: settings.file_list_text_color_hex.clone(),
            folder_text_color_hex: settings.folder_text_color_hex.clone(),
            hidden_item_text_color_hex: settings.hidden_item_text_color_hex.clone(),
            readonly_item_text_color_hex: settings.readonly_item_text_color_hex.clone(),

            enable_filetype_colors: settings.enable_filetype_colors,
            filetype_color_rules: settings.filetype_color_rules.clone(),

            treemap_video_media_color_hex: settings.treemap_video_media_color_hex.clone(),
            treemap_archive_image_color_hex: settings.treemap_archive_image_color_hex.clone(),
            treemap_virtual_disk_system_color_hex: settings.treemap_virtual_disk_system_color_hex.clone(),
            treemap_code_document_color_hex: settings.treemap_code_document_color_hex.clone(),
            treemap_picture_graphic_color_hex: settings.treemap_picture_graphic_color_hex.clone(),
            treemap_binary_executable_color_hex: settings.treemap_binary_executable_color_hex.clone(),
            treemap_other_temp_color_hex: settings.treemap_other_temp_color_hex.clone(),
        }
    }

    pub fn apply_color_theme(settings: &mut AppSettings, theme: &ColorThemePackage) {
        settings.custom_file_list_colors_enabled = theme.custom_file_list_colors_enabled;
        settings.active_panel_border_color_hex = theme.active_panel_border_color_hex.clone();
        settings.invert_cursor = theme.invert_cursor;
        settings.selection_border_color_hex = theme.selection_border_color_hex.clone();
        settings.selection_bg_color_hex = theme.selection_bg_color_hex.clone();
        settings.selection_text_color_hex = theme.selection_text_color_hex.clone();
        settings.marked_item_text_color_hex = theme.marked_item_text_color_hex.clone();
        settings.marked_item_bg_color_hex = theme.marked_item_bg_color_hex.clone();

        settings.file_list_bg_color_hex = theme.file_list_bg_color_hex.clone();
        settings.enable_zebra_stripes = theme.enable_zebra_stripes;
        settings.file_list_alt_bg_color_hex = theme.file_list_alt_bg_color_hex.clone();
        settings.show_grid_lines = theme.show_grid_lines;
        settings.grid_line_color_hex = theme.grid_line_color_hex.clone();
        settings.file_list_text_color_hex = theme.file_list_text_color_hex.clone();
        settings.folder_text_color_hex = theme.folder_text_color_hex.clone();
        settings.hidden_item_text_color_hex = theme.hidden_item_text_color_hex.clone();
        settings.readonly_item_text_color_hex = theme.readonly_item_text_color_hex.clone();

        settings.enable_filetype_colors = theme.enable_filetype_colors;
        if !theme.filetype_color_rules.is_empty() {
            settings.filetype_color_rules = theme.filetype_color_rules.clone();
        }

        settings.treemap_video_media_color_hex = theme.treemap_video_media_color_hex.clone();
        settings.treemap_archive_image_color_hex = theme.treemap_archive_image_color_hex.clone();
        settings.treemap_virtual_disk_system_color_hex = theme.treemap_virtual_disk_system_color_hex.clone();
        settings.treemap_code_document_color_hex = theme.treemap_code_document_color_hex.clone();
        settings.treemap_picture_graphic_color_hex = theme.treemap_picture_graphic_color_hex.clone();
        settings.treemap_binary_executable_color_hex = theme.treemap_binary_executable_color_hex.clone();
        settings.treemap_other_temp_color_hex = theme.treemap_other_temp_color_hex.clone();
    }
}

fn whoami_device_name() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .unwrap_or_else(|_| "Desktop-PC".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    #[test]
    fn test_color_theme_export_and_apply_roundtrip() {
        let mut settings = AppSettings::default();
        settings.custom_file_list_colors_enabled = true;
        settings.file_list_bg_color_hex = "#0A192F".to_string();
        settings.file_list_alt_bg_color_hex = "#112240".to_string();
        settings.folder_text_color_hex = "#64FFDA".to_string();
        settings.invert_cursor = true;
        settings.enable_zebra_stripes = true;

        let theme = CloudBackupService::export_color_theme(
            &settings,
            "Navy Matrix",
            Some("ModernExplorer User"),
            Some("Deep navy blue with cyan folders"),
            "1.9.10",
        );

        assert_eq!(theme.name, "Navy Matrix");
        assert_eq!(theme.file_list_bg_color_hex, "#0A192F");
        assert_eq!(theme.folder_text_color_hex, "#64FFDA");
        assert!(theme.invert_cursor);

        // Serialize to JSON and parse back
        let json_str = serde_json::to_string_pretty(&theme).expect("serialize theme");
        let parsed: ColorThemePackage = serde_json::from_str(&json_str).expect("deserialize theme");

        let mut target_settings = AppSettings::default();
        CloudBackupService::apply_color_theme(&mut target_settings, &parsed);

        assert!(target_settings.custom_file_list_colors_enabled);
        assert_eq!(target_settings.file_list_bg_color_hex, "#0A192F");
        assert_eq!(target_settings.file_list_alt_bg_color_hex, "#112240");
        assert_eq!(target_settings.folder_text_color_hex, "#64FFDA");
        assert!(target_settings.invert_cursor);
    }

    #[test]
    fn test_cloud_backup_save_and_restore_roundtrip() {
        let temp_cloud = TempDir::new().unwrap();
        let custom_cloud_path = temp_cloud.path().to_string_lossy().to_string();

        let mut settings = AppSettings::default();
        settings.default_view_mode = "Tiles".to_string();
        settings.invert_cursor = true;
        settings.natural_number_sorting = true;

        let backup_info = CloudBackupService::create_cloud_backup(
            "custom",
            Some(&custom_cloud_path),
            None,
            &settings,
            None,
            None,
            Some("Integration Test Backup".to_string()),
            "1.9.10",
        ).expect("create backup");

        assert!(Path::new(&backup_info.file_path).exists());
        assert!(backup_info.file_name.starts_with("ModernExplorer-Backup-"));

        let list = CloudBackupService::list_backups(Some("custom"), Some(&custom_cloud_path), None);
        assert!(!list.is_empty());
        assert_eq!(list[0].description.as_deref(), Some("Integration Test Backup"));

        let loaded_pkg = CloudBackupService::load_backup_from_file(Path::new(&backup_info.file_path))
            .expect("load backup from file");

        assert_eq!(loaded_pkg.settings.default_view_mode, "Tiles");
        assert!(loaded_pkg.settings.invert_cursor);
        assert!(loaded_pkg.settings.natural_number_sorting);
    }

    #[test]
    fn test_cloud_backup_provider_detection_and_test() {
        let temp_dir = TempDir::new().unwrap();
        let custom_path = temp_dir.path().to_string_lossy().to_string();

        let mut configured_paths = HashMap::new();
        configured_paths.insert("onedrive".to_string(), custom_path.clone());

        let providers = CloudBackupService::detect_providers(None, Some(&configured_paths));
        let onedrive = providers.iter().find(|p| p.id == "onedrive").expect("onedrive provider");
        assert!(onedrive.is_available);
        assert!(onedrive.detected);
        assert!(onedrive.is_custom_configured);
        assert_eq!(onedrive.detected_root.as_deref(), Some(custom_path.as_str()));

        let (test_ok, msg) = CloudBackupService::test_provider("onedrive", None, Some(&configured_paths));
        assert!(test_ok, "write test should succeed: {}", msg);
    }
}

