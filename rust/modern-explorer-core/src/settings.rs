use crate::app_paths::AppPaths;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ColorSettingTarget {
    ActivePanelBorder,
    SelectionBorder,
    MarkedItemText,
    TreemapVideoMedia,
    TreemapArchiveImage,
    TreemapVirtualDiskSystem,
    TreemapCodeDocument,
    TreemapPictureGraphic,
    TreemapBinaryExecutable,
    TreemapOtherTemp,
}

impl ColorSettingTarget {
    pub fn default_hex(self) -> &'static str {
        match self {
            ColorSettingTarget::ActivePanelBorder => "#0078D4",
            ColorSettingTarget::SelectionBorder => "#60CDFF",
            ColorSettingTarget::MarkedItemText => "#FFB900",
            ColorSettingTarget::TreemapVideoMedia => "#E11D48",
            ColorSettingTarget::TreemapArchiveImage => "#D97706",
            ColorSettingTarget::TreemapVirtualDiskSystem => "#7C3AED",
            ColorSettingTarget::TreemapCodeDocument => "#0284C7",
            ColorSettingTarget::TreemapPictureGraphic => "#059669",
            ColorSettingTarget::TreemapBinaryExecutable => "#2563EB",
            ColorSettingTarget::TreemapOtherTemp => "#475569",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize, Default)]
pub struct WindowBounds {
    pub width: f32,
    pub height: f32,
    #[serde(default)]
    pub maximized: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct AppSettings {
    // 1. Appearance & Design
    pub theme: String,
    pub language: String,
    pub show_drive_bar: bool,
    pub multiline_drive_bar: bool,
    pub show_function_key_bar: bool,
    pub show_tooltips: bool,
    pub file_list_row_height: f32,

    // 2. Colors & Highlighting (Active Panel, Selection, Marked Item + 7 Treemap Categories)
    pub active_panel_border_color_hex: String,
    pub active_panel_border_thickness: f32,
    pub selection_border_color_hex: String,
    pub selection_border_thickness: f32,
    pub marked_item_text_color_hex: String,

    pub treemap_video_media_color_hex: String,
    pub treemap_archive_image_color_hex: String,
    pub treemap_virtual_disk_system_color_hex: String,
    pub treemap_code_document_color_hex: String,
    pub treemap_picture_graphic_color_hex: String,
    pub treemap_binary_executable_color_hex: String,
    pub treemap_other_temp_color_hex: String,

    // 3. File List & Behavior
    pub default_view_mode: String,
    pub sort_folders_first: bool,
    pub show_file_extensions: bool,
    pub show_hidden_files: bool,
    pub show_system_files: bool,
    pub auto_calculate_folder_size_on_mark: bool,
    pub confirm_delete: bool,
    pub single_click_open: bool,

    // 4. Image Hover Preview
    pub enable_image_hover_preview: bool,
    pub image_hover_preview_size: f32,

    // 5. Font Sizes (Global + 8 Main Window Areas + 10 Child Windows)
    pub use_global_font_size: bool,
    pub global_font_size: f32,
    pub file_list_font_size: f32,
    pub file_list_details_font_size: f32,
    pub toolbar_font_size: f32,
    pub tab_header_font_size: f32,
    pub drive_bar_font_size: f32,
    pub address_bar_font_size: f32,
    pub breadcrumb_font_size: f32,
    pub status_bar_font_size: f32,
    pub function_key_bar_font_size: f32,
    pub preview_pane_font_size: f32,
    pub child_window_font_size: f32,

    // Individual Child Window Font Sizes
    pub quick_viewer_font_size: f32,
    pub editor_font_size: f32,
    pub storage_analyzer_font_size: f32,
    pub deep_search_font_size: f32,
    pub duplicate_finder_font_size: f32,
    pub directory_sync_font_size: f32,
    pub batch_rename_font_size: f32,
    pub ftp_manager_font_size: f32,
    pub archive_dialog_font_size: f32,
    pub about_window_font_size: f32,

    // 6. Session, Updates & Window Bounds
    pub restore_previous_session: bool,
    pub check_updates_on_startup: bool,
    pub main_window_width: f32,
    pub main_window_height: f32,
    pub main_window_x: Option<i32>,
    pub main_window_y: Option<i32>,
    pub main_window_maximized: bool,
    pub settings_window_width: f32,
    pub settings_window_height: f32,
    pub child_window_width: f32,
    pub child_window_height: f32,
    pub external_editor_path: String,
    pub custom_terminal_command: String,

    pub dialog_sizes: HashMap<String, WindowBounds>,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            theme: "dark".to_string(),
            language: "system".to_string(),
            show_drive_bar: true,
            multiline_drive_bar: true,
            show_function_key_bar: true,
            show_tooltips: true,
            file_list_row_height: 26.0,

            active_panel_border_color_hex: ColorSettingTarget::ActivePanelBorder.default_hex().to_string(),
            active_panel_border_thickness: 2.0,
            selection_border_color_hex: ColorSettingTarget::SelectionBorder.default_hex().to_string(),
            selection_border_thickness: 1.5,
            marked_item_text_color_hex: ColorSettingTarget::MarkedItemText.default_hex().to_string(),

            treemap_video_media_color_hex: ColorSettingTarget::TreemapVideoMedia.default_hex().to_string(),
            treemap_archive_image_color_hex: ColorSettingTarget::TreemapArchiveImage.default_hex().to_string(),
            treemap_virtual_disk_system_color_hex: ColorSettingTarget::TreemapVirtualDiskSystem.default_hex().to_string(),
            treemap_code_document_color_hex: ColorSettingTarget::TreemapCodeDocument.default_hex().to_string(),
            treemap_picture_graphic_color_hex: ColorSettingTarget::TreemapPictureGraphic.default_hex().to_string(),
            treemap_binary_executable_color_hex: ColorSettingTarget::TreemapBinaryExecutable.default_hex().to_string(),
            treemap_other_temp_color_hex: ColorSettingTarget::TreemapOtherTemp.default_hex().to_string(),

            default_view_mode: "Details".to_string(),
            sort_folders_first: true,
            show_file_extensions: true,
            show_hidden_files: false,
            show_system_files: false,
            auto_calculate_folder_size_on_mark: true,
            confirm_delete: true,
            single_click_open: false,

            enable_image_hover_preview: true,
            image_hover_preview_size: 320.0,

            use_global_font_size: false,
            global_font_size: 13.0,
            file_list_font_size: 13.0,
            file_list_details_font_size: 12.0,
            toolbar_font_size: 12.5,
            tab_header_font_size: 12.0,
            drive_bar_font_size: 12.0,
            address_bar_font_size: 12.5,
            breadcrumb_font_size: 11.5,
            status_bar_font_size: 11.5,
            function_key_bar_font_size: 11.5,
            preview_pane_font_size: 12.5,
            child_window_font_size: 13.0,

            quick_viewer_font_size: 13.0,
            editor_font_size: 13.0,
            storage_analyzer_font_size: 12.5,
            deep_search_font_size: 12.5,
            duplicate_finder_font_size: 12.5,
            directory_sync_font_size: 12.5,
            batch_rename_font_size: 12.5,
            ftp_manager_font_size: 12.5,
            archive_dialog_font_size: 12.5,
            about_window_font_size: 12.5,

            restore_previous_session: true,
            check_updates_on_startup: true,
            main_window_width: 1520.0,
            main_window_height: 920.0,
            main_window_x: None,
            main_window_y: None,
            main_window_maximized: false,
            settings_window_width: 860.0,
            settings_window_height: 660.0,
            child_window_width: 960.0,
            child_window_height: 680.0,
            external_editor_path: String::new(),
            custom_terminal_command: String::new(),

            dialog_sizes: HashMap::new(),
        }
    }
}

impl AppSettings {
    pub fn get_color_hex(&self, target: ColorSettingTarget) -> &str {
        match target {
            ColorSettingTarget::ActivePanelBorder => &self.active_panel_border_color_hex,
            ColorSettingTarget::SelectionBorder => &self.selection_border_color_hex,
            ColorSettingTarget::MarkedItemText => &self.marked_item_text_color_hex,
            ColorSettingTarget::TreemapVideoMedia => &self.treemap_video_media_color_hex,
            ColorSettingTarget::TreemapArchiveImage => &self.treemap_archive_image_color_hex,
            ColorSettingTarget::TreemapVirtualDiskSystem => &self.treemap_virtual_disk_system_color_hex,
            ColorSettingTarget::TreemapCodeDocument => &self.treemap_code_document_color_hex,
            ColorSettingTarget::TreemapPictureGraphic => &self.treemap_picture_graphic_color_hex,
            ColorSettingTarget::TreemapBinaryExecutable => &self.treemap_binary_executable_color_hex,
            ColorSettingTarget::TreemapOtherTemp => &self.treemap_other_temp_color_hex,
        }
    }

    pub fn set_color_hex(&mut self, target: ColorSettingTarget, hex: String) {
        let normalized = Self::normalize_hex(&hex).unwrap_or_else(|| target.default_hex().to_string());
        match target {
            ColorSettingTarget::ActivePanelBorder => self.active_panel_border_color_hex = normalized,
            ColorSettingTarget::SelectionBorder => self.selection_border_color_hex = normalized,
            ColorSettingTarget::MarkedItemText => self.marked_item_text_color_hex = normalized,
            ColorSettingTarget::TreemapVideoMedia => self.treemap_video_media_color_hex = normalized,
            ColorSettingTarget::TreemapArchiveImage => self.treemap_archive_image_color_hex = normalized,
            ColorSettingTarget::TreemapVirtualDiskSystem => self.treemap_virtual_disk_system_color_hex = normalized,
            ColorSettingTarget::TreemapCodeDocument => self.treemap_code_document_color_hex = normalized,
            ColorSettingTarget::TreemapPictureGraphic => self.treemap_picture_graphic_color_hex = normalized,
            ColorSettingTarget::TreemapBinaryExecutable => self.treemap_binary_executable_color_hex = normalized,
            ColorSettingTarget::TreemapOtherTemp => self.treemap_other_temp_color_hex = normalized,
        }
    }

    pub fn reset_color_to_default(&mut self, target: ColorSettingTarget) {
        self.set_color_hex(target, target.default_hex().to_string());
    }

    pub fn reset_treemap_colors_to_default(&mut self) {
        self.reset_color_to_default(ColorSettingTarget::TreemapVideoMedia);
        self.reset_color_to_default(ColorSettingTarget::TreemapArchiveImage);
        self.reset_color_to_default(ColorSettingTarget::TreemapVirtualDiskSystem);
        self.reset_color_to_default(ColorSettingTarget::TreemapCodeDocument);
        self.reset_color_to_default(ColorSettingTarget::TreemapPictureGraphic);
        self.reset_color_to_default(ColorSettingTarget::TreemapBinaryExecutable);
        self.reset_color_to_default(ColorSettingTarget::TreemapOtherTemp);
    }

    pub fn reset_font_sizes_to_default(&mut self) {
        let def = Self::default();
        self.use_global_font_size = def.use_global_font_size;
        self.global_font_size = def.global_font_size;
        self.file_list_font_size = def.file_list_font_size;
        self.file_list_details_font_size = def.file_list_details_font_size;
        self.toolbar_font_size = def.toolbar_font_size;
        self.tab_header_font_size = def.tab_header_font_size;
        self.drive_bar_font_size = def.drive_bar_font_size;
        self.address_bar_font_size = def.address_bar_font_size;
        self.breadcrumb_font_size = def.breadcrumb_font_size;
        self.status_bar_font_size = def.status_bar_font_size;
        self.function_key_bar_font_size = def.function_key_bar_font_size;
        self.preview_pane_font_size = def.preview_pane_font_size;
        self.child_window_font_size = def.child_window_font_size;
        self.quick_viewer_font_size = def.quick_viewer_font_size;
        self.editor_font_size = def.editor_font_size;
        self.storage_analyzer_font_size = def.storage_analyzer_font_size;
        self.deep_search_font_size = def.deep_search_font_size;
        self.duplicate_finder_font_size = def.duplicate_finder_font_size;
        self.directory_sync_font_size = def.directory_sync_font_size;
        self.batch_rename_font_size = def.batch_rename_font_size;
        self.ftp_manager_font_size = def.ftp_manager_font_size;
        self.archive_dialog_font_size = def.archive_dialog_font_size;
        self.about_window_font_size = def.about_window_font_size;
    }

    pub fn reset_window_bounds_to_default(&mut self) {
        let def = Self::default();
        self.main_window_width = def.main_window_width;
        self.main_window_height = def.main_window_height;
        self.main_window_x = None;
        self.main_window_y = None;
        self.main_window_maximized = false;
        self.settings_window_width = def.settings_window_width;
        self.settings_window_height = def.settings_window_height;
        self.child_window_width = def.child_window_width;
        self.child_window_height = def.child_window_height;
        self.dialog_sizes.clear();
    }

    pub fn update_dialog_bounds(&mut self, key: &str, width: f32, height: f32, maximized: bool) {
        let w = width.clamp(380.0, 3840.0);
        let h = height.clamp(240.0, 2160.0);
        self.dialog_sizes.insert(
            key.to_string(),
            WindowBounds {
                width: w,
                height: h,
                maximized,
            },
        );
        if key == "modalSettings" {
            self.settings_window_width = w;
            self.settings_window_height = h;
        } else if key != "modalCreate" && key != "modalPalette" {
            self.child_window_width = w;
            self.child_window_height = h;
        }
    }

    pub fn normalize_hex(input: &str) -> Option<String> {
        let trimmed = input.trim().trim_start_matches('#');
        if trimmed.len() == 6 && trimmed.chars().all(|c| c.is_ascii_hexdigit()) {
            Some(format!("#{}", trimmed.to_uppercase()))
        } else if trimmed.len() == 8 && trimmed.chars().all(|c| c.is_ascii_hexdigit()) {
            Some(format!("#{}", &trimmed[2..8].to_uppercase()))
        } else {
            None
        }
    }

    pub fn parse_rgb(hex: &str) -> (u8, u8, u8) {
        let cleaned = hex.trim().trim_start_matches('#');
        if cleaned.len() >= 6 {
            let r = u8::from_str_radix(&cleaned[0..2], 16).unwrap_or(0);
            let g = u8::from_str_radix(&cleaned[2..4], 16).unwrap_or(120);
            let b = u8::from_str_radix(&cleaned[4..6], 16).unwrap_or(212);
            (r, g, b)
        } else {
            (0, 120, 212)
        }
    }

    pub fn effective_file_list_font_size(&self) -> f32 {
        if self.use_global_font_size { self.global_font_size } else { self.file_list_font_size }
    }

    pub fn effective_toolbar_font_size(&self) -> f32 {
        if self.use_global_font_size { self.global_font_size } else { self.toolbar_font_size }
    }

    pub fn effective_child_window_font_size(&self) -> f32 {
        if self.use_global_font_size { self.global_font_size } else { self.child_window_font_size }
    }
}

pub struct SettingsStore;

impl SettingsStore {
    pub fn load() -> AppSettings {
        Self::load_from(&AppPaths::settings_file())
    }

    pub fn load_from(path: &Path) -> AppSettings {
        if let Ok(text) = fs::read_to_string(path) {
            if let Ok(settings) = serde_json::from_str::<AppSettings>(&text) {
                return settings;
            }
        }
        AppSettings::default()
    }

    pub fn save(settings: &AppSettings) {
        Self::save_to(&AppPaths::settings_file(), settings);
    }

    pub fn save_to(path: &Path, settings: &AppSettings) {
        if let Some(parent) = path.parent() {
            let _ = fs::create_dir_all(parent);
        }
        if let Ok(json) = serde_json::to_string_pretty(settings) {
            let _ = fs::write(path, json);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_dialog_bounds_persistence_roundtrip() {
        let mut settings = AppSettings::default();
        settings.update_dialog_bounds("modalSettings", 1120.0, 760.0, false);
        settings.update_dialog_bounds("tool:BatchRename", 1240.0, 810.0, true);

        assert_eq!(settings.settings_window_width, 1120.0);
        assert_eq!(settings.settings_window_height, 760.0);
        assert_eq!(settings.child_window_width, 1240.0);
        assert_eq!(settings.child_window_height, 810.0);

        let temp_file = std::env::temp_dir().join("modern_explorer_test_settings_bounds.json");
        SettingsStore::save_to(&temp_file, &settings);

        let loaded = SettingsStore::load_from(&temp_file);
        let _ = fs::remove_file(&temp_file);

        let s_bounds = loaded.dialog_sizes.get("modalSettings").expect("modalSettings bounds saved");
        assert_eq!(s_bounds.width, 1120.0);
        assert_eq!(s_bounds.height, 760.0);
        assert!(!s_bounds.maximized);

        let br_bounds = loaded.dialog_sizes.get("tool:BatchRename").expect("tool:BatchRename bounds saved");
        assert_eq!(br_bounds.width, 1240.0);
        assert_eq!(br_bounds.height, 810.0);
        assert!(br_bounds.maximized);
    }
}

