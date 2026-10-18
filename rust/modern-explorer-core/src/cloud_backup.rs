use crate::power_tools_v2::UserCommandItem;
use crate::session::FavoriteEntry;
use crate::settings::{AppSettings, FileTypeColorRule};
use chrono::{DateTime, Local};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CloudProviderInfo {
    pub id: String,
    pub name: String,
    pub icon: String,
    pub detected_root: Option<String>,
    pub backup_dir: Option<String>,
    pub is_available: bool,
    pub backup_count: usize,
    pub latest_backup_date: Option<String>,
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
    pub fn detect_providers(custom_path: Option<&str>) -> Vec<CloudProviderInfo> {
        let mut list = Vec::new();

        // 1. Microsoft OneDrive
        list.push(Self::probe_provider(
            "onedrive",
            "Microsoft OneDrive",
            "☁️",
            Self::find_onedrive_root(),
        ));

        // 2. Google Drive
        list.push(Self::probe_provider(
            "googledrive",
            "Google Drive",
            "🔷",
            Self::find_google_drive_root(),
        ));

        // 3. Apple iCloud Drive
        list.push(Self::probe_provider(
            "iclouddrive",
            "Apple iCloud Drive",
            "🍏",
            Self::find_icloud_drive_root(),
        ));

        // 4. Dropbox
        list.push(Self::probe_provider(
            "dropbox",
            "Dropbox",
            "📦",
            Self::find_dropbox_root(),
        ));

        // 5. Custom Cloud / Network directory
        let custom_root = custom_path.and_then(|p| {
            let pb = PathBuf::from(p);
            if pb.is_dir() {
                Some(pb)
            } else {
                None
            }
        });
        list.push(Self::probe_provider(
            "custom",
            "Benutzerdefinierter Cloud-Pfad",
            "📁",
            custom_root,
        ));

        list
    }

    fn probe_provider(
        id: &str,
        name: &str,
        icon: &str,
        root_opt: Option<PathBuf>,
    ) -> CloudProviderInfo {
        let (is_available, detected_root, backup_dir, backup_count, latest_backup_date) =
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

                (
                    true,
                    Some(root.to_string_lossy().to_string()),
                    Some(b_dir.to_string_lossy().to_string()),
                    count,
                    latest_date,
                )
            } else {
                (false, None, None, 0, None)
            };

        CloudProviderInfo {
            id: id.to_string(),
            name: name.to_string(),
            icon: icon.to_string(),
            detected_root,
            backup_dir,
            is_available,
            backup_count,
            latest_backup_date,
        }
    }

    pub fn find_onedrive_root() -> Option<PathBuf> {
        // Environment variables standard on Windows
        for env_var in &["OneDrive", "OneDriveConsumer", "OneDriveCommercial"] {
            if let Ok(val) = std::env::var(env_var) {
                let p = PathBuf::from(val);
                if p.is_dir() {
                    return Some(p);
                }
            }
        }
        if let Some(home) = dirs::home_dir() {
            let p = home.join("OneDrive");
            if p.is_dir() {
                return Some(p);
            }
        }
        None
    }

    pub fn find_google_drive_root() -> Option<PathBuf> {
        // Common paths on Windows / macOS
        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("Google Drive").join("My Drive"),
                home.join("Google Drive").join("Meine Ablage"),
                home.join("Google Drive"),
            ];
            for c in candidates {
                if c.is_dir() {
                    return Some(c);
                }
            }
        }
        // Google Drive mounted virtual drives on Windows (G:\, H:\)
        for drive_letter in &["G:\\My Drive", "G:\\Meine Ablage", "G:\\"] {
            let p = PathBuf::from(drive_letter);
            if p.is_dir() {
                return Some(p);
            }
        }
        None
    }

    pub fn find_icloud_drive_root() -> Option<PathBuf> {
        if let Some(home) = dirs::home_dir() {
            let candidates = [
                home.join("iCloudDrive"),
                home.join("iCloud Drive"),
                home.join("Library/Mobile Documents/com~apple~CloudDocs"),
            ];
            for c in candidates {
                if c.is_dir() {
                    return Some(c);
                }
            }
        }
        None
    }

    pub fn find_dropbox_root() -> Option<PathBuf> {
        if let Some(home) = dirs::home_dir() {
            let p = home.join("Dropbox");
            if p.is_dir() {
                return Some(p);
            }
        }
        None
    }

    /// Resolve target directory for backup creation
    pub fn get_backup_dir(provider_id: &str, custom_path: Option<&str>) -> Result<PathBuf, String> {
        let root = match provider_id {
            "onedrive" => Self::find_onedrive_root()
                .ok_or_else(|| "Microsoft OneDrive wurde auf diesem System nicht gefunden.".to_string())?,
            "googledrive" => Self::find_google_drive_root()
                .ok_or_else(|| "Google Drive wurde auf diesem System nicht gefunden.".to_string())?,
            "iclouddrive" => Self::find_icloud_drive_root()
                .ok_or_else(|| "Apple iCloud Drive wurde auf diesem System nicht gefunden.".to_string())?,
            "dropbox" => Self::find_dropbox_root()
                .ok_or_else(|| "Dropbox wurde auf diesem System nicht gefunden.".to_string())?,
            "custom" => {
                if let Some(cp) = custom_path {
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
        settings: &AppSettings,
        favorites: Option<Vec<FavoriteEntry>>,
        user_commands: Option<Vec<UserCommandItem>>,
        description: Option<String>,
        app_version: &str,
    ) -> Result<BackupFileInfo, String> {
        let target_dir = Self::get_backup_dir(provider_id, custom_path)?;
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
    pub fn list_backups(provider_id_filter: Option<&str>, custom_path: Option<&str>) -> Vec<BackupFileInfo> {
        let providers = Self::detect_providers(custom_path);
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
                            // Skip the duplicate pointer in listing if historical exists
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
    pub fn scan_for_fresh_install_backups() -> Option<BackupFileInfo> {
        let backups = Self::list_backups(None, None);
        // Find latest non-empty backup
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
            &settings,
            None,
            None,
            Some("Integration Test Backup".to_string()),
            "1.9.10",
        ).expect("create backup");

        assert!(Path::new(&backup_info.file_path).exists());
        assert!(backup_info.file_name.starts_with("ModernExplorer-Backup-"));

        let list = CloudBackupService::list_backups(Some("custom"), Some(&custom_cloud_path));
        assert!(!list.is_empty());
        assert_eq!(list[0].description.as_deref(), Some("Integration Test Backup"));

        let loaded_pkg = CloudBackupService::load_backup_from_file(Path::new(&backup_info.file_path))
            .expect("load backup from file");

        assert_eq!(loaded_pkg.settings.default_view_mode, "Tiles");
        assert!(loaded_pkg.settings.invert_cursor);
        assert!(loaded_pkg.settings.natural_number_sorting);
    }
}

