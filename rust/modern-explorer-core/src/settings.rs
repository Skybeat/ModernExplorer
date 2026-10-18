use crate::app_paths::AppPaths;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ColorSettingTarget {
    ActivePanelBorder,
    SelectionBorder,
    SelectionBackground,
    SelectionText,
    MarkedItemText,
    MarkedItemBackground,
    FileListBackground,
    FileListAltBackground,
    FileListText,
    FolderText,
    HiddenItemText,
    ReadonlyItemText,
    GridLine,
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
            ColorSettingTarget::SelectionBackground => "#1E3A5F",
            ColorSettingTarget::SelectionText => "#FFFFFF",
            ColorSettingTarget::MarkedItemText => "#FFB900",
            ColorSettingTarget::MarkedItemBackground => "#332800",
            ColorSettingTarget::FileListBackground => "#151821",
            ColorSettingTarget::FileListAltBackground => "#191D28",
            ColorSettingTarget::FileListText => "#E6E9F2",
            ColorSettingTarget::FolderText => "#FCD34D",
            ColorSettingTarget::HiddenItemText => "#64748B",
            ColorSettingTarget::ReadonlyItemText => "#38BDF8",
            ColorSettingTarget::GridLine => "#252A3A",
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct FileTypeColorRule {
    pub id: String,
    pub name: String,
    pub pattern: String,
    #[serde(default)]
    pub max_age_hours: Option<u64>,
    pub color_hex: String,
    #[serde(default)]
    pub bold: bool,
    #[serde(default = "default_true")]
    pub enabled: bool,
}

fn default_true() -> bool {
    true
}

pub fn default_filetype_color_rules() -> Vec<FileTypeColorRule> {
    vec![
        FileTypeColorRule {
            id: "ft_recent_24h".to_string(),
            name: "Heute geändert (< 24 Std.)".to_string(),
            pattern: "*".to_string(),
            max_age_hours: Some(24),
            color_hex: "#4ADE80".to_string(),
            bold: false,
            enabled: false,
        },
        FileTypeColorRule {
            id: "ft_exec".to_string(),
            name: "Ausführbare Programme & Skripte".to_string(),
            pattern: "*.exe;*.msi;*.bat;*.cmd;*.ps1;*.sh;*.com".to_string(),
            max_age_hours: None,
            color_hex: "#34D399".to_string(),
            bold: true,
            enabled: true,
        },
        FileTypeColorRule {
            id: "ft_archive".to_string(),
            name: "Archive & Disk-Images".to_string(),
            pattern: "*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz;*.zst;*.tzst;*.xz;*.txz;*.iso;*.img;*.cab;*.nupkg".to_string(),
            max_age_hours: None,
            color_hex: "#F59E0B".to_string(),
            bold: false,
            enabled: true,
        },
        FileTypeColorRule {
            id: "ft_code".to_string(),
            name: "Quellcode & Konfigurationsdateien".to_string(),
            pattern: "*.rs;*.cs;*.ts;*.tsx;*.js;*.jsx;*.py;*.go;*.c;*.cpp;*.h;*.json;*.toml;*.yaml;*.yml;*.xml;*.html;*.css;*.sql".to_string(),
            max_age_hours: None,
            color_hex: "#60A5FA".to_string(),
            bold: false,
            enabled: true,
        },
        FileTypeColorRule {
            id: "ft_images".to_string(),
            name: "Bilder & Grafiken".to_string(),
            pattern: "*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.ico;*.tif;*.tiff;*.psd;*.ai".to_string(),
            max_age_hours: None,
            color_hex: "#C084FC".to_string(),
            bold: false,
            enabled: true,
        },
        FileTypeColorRule {
            id: "ft_media".to_string(),
            name: "Video & Audio".to_string(),
            pattern: "*.mp4;*.mkv;*.avi;*.mov;*.webm;*.mp3;*.wav;*.flac;*.ogg;*.m4a".to_string(),
            max_age_hours: None,
            color_hex: "#F472B6".to_string(),
            bold: false,
            enabled: true,
        },
        FileTypeColorRule {
            id: "ft_docs".to_string(),
            name: "Dokumente & Office".to_string(),
            pattern: "*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.md;*.txt;*.rtf;*.csv".to_string(),
            max_age_hours: None,
            color_hex: "#38BDF8".to_string(),
            bold: false,
            enabled: true,
        },
        FileTypeColorRule {
            id: "ft_temp".to_string(),
            name: "Temporäre, Backup- & Log-Dateien".to_string(),
            pattern: "*.tmp;*.bak;*.old;*.orig;*.log;*.swp;~*".to_string(),
            max_age_hours: None,
            color_hex: "#94A3B8".to_string(),
            bold: false,
            enabled: true,
        },
    ]
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize, Default)]
pub struct WindowBounds {
    pub width: f32,
    pub height: f32,
    #[serde(default)]
    pub maximized: bool,
}

fn default_archive_open_action() -> String {
    "archive_browser".to_string()
}

fn default_sync_jump_to_other_pane_folder() -> bool {
    true
}

fn default_size_display_format() -> String {
    "dynamic".to_string()
}

fn default_date_time_format() -> String {
    "de_short".to_string()
}

fn default_ui_zoom_percent() -> u32 {
    100
}

fn default_fs_watcher_poll_ms() -> u32 {
    2500
}

fn default_archive_format() -> String {
    "zip".to_string()
}

fn default_checksum_algorithm() -> String {
    "SHA-256".to_string()
}

fn default_share_server_port() -> u16 {
    8787
}

fn default_quick_viewer_max_kb() -> u32 {
    512
}

fn default_duplicates_min_size_kb() -> u32 {
    1
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
    pub show_favorites_bar: bool,
    pub show_left_tool_rail: bool,
    pub show_breadcrumbs_bar: bool,
    pub show_status_bar: bool,
    pub show_tree_sidebar_on_startup: bool,
    pub show_tooltips: bool,
    pub show_size_proportion_bars: bool,
    pub file_list_row_height: f32,
    #[serde(default = "default_ui_zoom_percent")]
    pub ui_zoom_percent: u32,
    pub right_dock_open: bool,
    pub right_dock_tab: String,
    pub right_dock_width: f32,

    // 2. Colors & Highlighting (Total Commander Style + File Type Rules + 7 Treemap Categories)
    pub active_panel_border_color_hex: String,
    pub active_panel_border_thickness: f32,
    pub selection_border_color_hex: String,
    pub selection_border_thickness: f32,
    pub selection_bg_color_hex: String,
    pub selection_text_color_hex: String,
    pub invert_cursor: bool,
    pub marked_item_text_color_hex: String,
    pub marked_item_bg_color_hex: String,

    pub custom_file_list_colors_enabled: bool,
    pub file_list_bg_color_hex: String,
    pub enable_zebra_stripes: bool,
    pub file_list_alt_bg_color_hex: String,
    pub show_grid_lines: bool,
    pub grid_line_color_hex: String,
    pub file_list_text_color_hex: String,
    pub folder_text_color_hex: String,
    pub hidden_item_text_color_hex: String,
    pub readonly_item_text_color_hex: String,

    pub enable_filetype_colors: bool,
    pub filetype_color_rules: Vec<FileTypeColorRule>,

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
    pub natural_number_sorting: bool,
    pub case_sensitive_sorting: bool,
    pub show_file_extensions: bool,
    pub show_hidden_files: bool,
    pub show_system_files: bool,
    pub show_parent_dir_entry: bool,
    #[serde(default = "default_size_display_format")]
    pub size_display_format: String,
    #[serde(default = "default_date_time_format")]
    pub date_time_format: String,
    pub auto_calculate_folder_size_on_mark: bool,
    pub space_moves_down_on_mark: bool,
    pub double_click_empty_space_goes_up: bool,
    pub middle_click_opens_in_new_tab: bool,
    pub confirm_delete: bool,
    pub confirm_overwrite: bool,
    pub single_click_open: bool,
    #[serde(default = "default_archive_open_action")]
    pub archive_open_action: String,
    #[serde(default = "default_sync_jump_to_other_pane_folder")]
    pub sync_jump_to_other_pane_folder: bool,
    pub enable_live_fs_watcher: bool,
    #[serde(default = "default_fs_watcher_poll_ms")]
    pub fs_watcher_poll_ms: u32,

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
    pub settings_window_font_size: f32,
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

    // 6. Session, Updates, External Tools & Window Bounds
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
    pub external_diff_tool_path: String,
    pub custom_terminal_command: String,
    #[serde(default = "default_archive_format")]
    pub default_archive_format: String,
    #[serde(default = "default_checksum_algorithm")]
    pub default_checksum_algorithm: String,
    pub default_transfer_speed_limit_mbps: u32,
    #[serde(default = "default_share_server_port")]
    pub default_share_server_port: u16,
    #[serde(default = "default_quick_viewer_max_kb")]
    pub quick_viewer_max_kb: u32,
    #[serde(default = "default_duplicates_min_size_kb")]
    pub duplicates_min_size_kb: u32,

    // 7. Cloud Backup & Synchronization
    #[serde(default = "default_cloud_provider")]
    pub cloud_backup_provider: String,
    #[serde(default)]
    pub auto_cloud_backup_enabled: bool,
    #[serde(default)]
    pub custom_cloud_backup_dir: Option<String>,
    #[serde(default)]
    pub cloud_provider_paths: HashMap<String, String>,
    #[serde(default = "default_true")]
    pub first_run_completed: bool,

    pub dialog_sizes: HashMap<String, WindowBounds>,
}

fn default_cloud_provider() -> String {
    "onedrive".to_string()
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            theme: "dark".to_string(),
            language: "system".to_string(),
            show_drive_bar: true,
            multiline_drive_bar: true,
            show_function_key_bar: true,
            show_favorites_bar: true,
            show_left_tool_rail: true,
            show_breadcrumbs_bar: true,
            show_status_bar: true,
            show_tree_sidebar_on_startup: false,
            show_tooltips: true,
            show_size_proportion_bars: true,
            file_list_row_height: 26.0,
            ui_zoom_percent: 100,
            right_dock_open: true,
            right_dock_tab: "preview".to_string(),
            right_dock_width: 320.0,

            active_panel_border_color_hex: ColorSettingTarget::ActivePanelBorder.default_hex().to_string(),
            active_panel_border_thickness: 2.0,
            selection_border_color_hex: ColorSettingTarget::SelectionBorder.default_hex().to_string(),
            selection_border_thickness: 1.5,
            selection_bg_color_hex: ColorSettingTarget::SelectionBackground.default_hex().to_string(),
            selection_text_color_hex: ColorSettingTarget::SelectionText.default_hex().to_string(),
            invert_cursor: false,
            marked_item_text_color_hex: ColorSettingTarget::MarkedItemText.default_hex().to_string(),
            marked_item_bg_color_hex: ColorSettingTarget::MarkedItemBackground.default_hex().to_string(),

            custom_file_list_colors_enabled: false,
            file_list_bg_color_hex: ColorSettingTarget::FileListBackground.default_hex().to_string(),
            enable_zebra_stripes: false,
            file_list_alt_bg_color_hex: ColorSettingTarget::FileListAltBackground.default_hex().to_string(),
            show_grid_lines: false,
            grid_line_color_hex: ColorSettingTarget::GridLine.default_hex().to_string(),
            file_list_text_color_hex: ColorSettingTarget::FileListText.default_hex().to_string(),
            folder_text_color_hex: ColorSettingTarget::FolderText.default_hex().to_string(),
            hidden_item_text_color_hex: ColorSettingTarget::HiddenItemText.default_hex().to_string(),
            readonly_item_text_color_hex: ColorSettingTarget::ReadonlyItemText.default_hex().to_string(),

            enable_filetype_colors: true,
            filetype_color_rules: default_filetype_color_rules(),

            treemap_video_media_color_hex: ColorSettingTarget::TreemapVideoMedia.default_hex().to_string(),
            treemap_archive_image_color_hex: ColorSettingTarget::TreemapArchiveImage.default_hex().to_string(),
            treemap_virtual_disk_system_color_hex: ColorSettingTarget::TreemapVirtualDiskSystem.default_hex().to_string(),
            treemap_code_document_color_hex: ColorSettingTarget::TreemapCodeDocument.default_hex().to_string(),
            treemap_picture_graphic_color_hex: ColorSettingTarget::TreemapPictureGraphic.default_hex().to_string(),
            treemap_binary_executable_color_hex: ColorSettingTarget::TreemapBinaryExecutable.default_hex().to_string(),
            treemap_other_temp_color_hex: ColorSettingTarget::TreemapOtherTemp.default_hex().to_string(),

            default_view_mode: "Details".to_string(),
            sort_folders_first: true,
            natural_number_sorting: true,
            case_sensitive_sorting: false,
            show_file_extensions: true,
            show_hidden_files: false,
            show_system_files: false,
            show_parent_dir_entry: true,
            size_display_format: "dynamic".to_string(),
            date_time_format: "de_short".to_string(),
            auto_calculate_folder_size_on_mark: true,
            space_moves_down_on_mark: false,
            double_click_empty_space_goes_up: true,
            middle_click_opens_in_new_tab: true,
            confirm_delete: true,
            confirm_overwrite: false,
            single_click_open: false,
            archive_open_action: "archive_browser".to_string(),
            sync_jump_to_other_pane_folder: true,
            enable_live_fs_watcher: true,
            fs_watcher_poll_ms: 2500,

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

            settings_window_font_size: 12.5,
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
            external_diff_tool_path: String::new(),
            custom_terminal_command: String::new(),
            default_archive_format: "zip".to_string(),
            default_checksum_algorithm: "SHA-256".to_string(),
            default_transfer_speed_limit_mbps: 0,
            default_share_server_port: 8787,
            quick_viewer_max_kb: 512,
            duplicates_min_size_kb: 1,

            cloud_backup_provider: default_cloud_provider(),
            auto_cloud_backup_enabled: false,
            custom_cloud_backup_dir: None,
            cloud_provider_paths: HashMap::new(),
            first_run_completed: true,

            dialog_sizes: HashMap::new(),
        }
    }
}

impl AppSettings {
    pub fn get_color_hex(&self, target: ColorSettingTarget) -> &str {
        match target {
            ColorSettingTarget::ActivePanelBorder => &self.active_panel_border_color_hex,
            ColorSettingTarget::SelectionBorder => &self.selection_border_color_hex,
            ColorSettingTarget::SelectionBackground => &self.selection_bg_color_hex,
            ColorSettingTarget::SelectionText => &self.selection_text_color_hex,
            ColorSettingTarget::MarkedItemText => &self.marked_item_text_color_hex,
            ColorSettingTarget::MarkedItemBackground => &self.marked_item_bg_color_hex,
            ColorSettingTarget::FileListBackground => &self.file_list_bg_color_hex,
            ColorSettingTarget::FileListAltBackground => &self.file_list_alt_bg_color_hex,
            ColorSettingTarget::FileListText => &self.file_list_text_color_hex,
            ColorSettingTarget::FolderText => &self.folder_text_color_hex,
            ColorSettingTarget::HiddenItemText => &self.hidden_item_text_color_hex,
            ColorSettingTarget::ReadonlyItemText => &self.readonly_item_text_color_hex,
            ColorSettingTarget::GridLine => &self.grid_line_color_hex,
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
            ColorSettingTarget::SelectionBackground => self.selection_bg_color_hex = normalized,
            ColorSettingTarget::SelectionText => self.selection_text_color_hex = normalized,
            ColorSettingTarget::MarkedItemText => self.marked_item_text_color_hex = normalized,
            ColorSettingTarget::MarkedItemBackground => self.marked_item_bg_color_hex = normalized,
            ColorSettingTarget::FileListBackground => self.file_list_bg_color_hex = normalized,
            ColorSettingTarget::FileListAltBackground => self.file_list_alt_bg_color_hex = normalized,
            ColorSettingTarget::FileListText => self.file_list_text_color_hex = normalized,
            ColorSettingTarget::FolderText => self.folder_text_color_hex = normalized,
            ColorSettingTarget::HiddenItemText => self.hidden_item_text_color_hex = normalized,
            ColorSettingTarget::ReadonlyItemText => self.readonly_item_text_color_hex = normalized,
            ColorSettingTarget::GridLine => self.grid_line_color_hex = normalized,
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

    pub fn reset_filetype_colors_to_default(&mut self) {
        self.filetype_color_rules = default_filetype_color_rules();
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
        self.settings_window_font_size = def.settings_window_font_size;
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
    pub fn config_dir() -> std::path::PathBuf {
        AppPaths::settings_file()
            .parent()
            .map(|p| p.to_path_buf())
            .unwrap_or_else(std::env::temp_dir)
    }

    pub fn is_fresh_install() -> bool {
        !AppPaths::settings_file().exists()
    }

    pub fn load() -> AppSettings {
        Self::load_from(&AppPaths::settings_file())
    }

    pub fn load_from(path: &Path) -> AppSettings {
        if let Ok(text) = fs::read_to_string(path) {
            if let Ok(settings) = serde_json::from_str::<AppSettings>(&text) {
                return settings;
            }
        }
        let mut def = AppSettings::default();
        def.first_run_completed = false;
        def
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
    fn test_total_commander_colors_and_new_settings_roundtrip() {
        let mut settings = AppSettings::default();
        assert!(settings.enable_filetype_colors);
        assert!(!settings.filetype_color_rules.is_empty());
        assert!(settings.natural_number_sorting);
        assert_eq!(settings.size_display_format, "dynamic");
        assert_eq!(settings.date_time_format, "de_short");

        settings.custom_file_list_colors_enabled = true;
        settings.file_list_bg_color_hex = "#000080".to_string();
        settings.invert_cursor = true;
        settings.space_moves_down_on_mark = true;
        settings.reset_color_to_default(ColorSettingTarget::FileListBackground);
        assert_eq!(settings.file_list_bg_color_hex, "#151821");
    }

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

    #[test]
    fn test_dock_state_persistence_in_settings_and_session() {
        let mut settings = AppSettings::default();
        assert!(settings.right_dock_open);
        settings.right_dock_open = false;
        settings.right_dock_tab = "basket".to_string();
        settings.right_dock_width = 380.0;

        let temp_file = std::env::temp_dir().join("modern_explorer_test_dock_settings.json");
        SettingsStore::save_to(&temp_file, &settings);
        let loaded = SettingsStore::load_from(&temp_file);
        let _ = fs::remove_file(&temp_file);

        assert!(!loaded.right_dock_open);
        assert_eq!(loaded.right_dock_tab, "basket");
        assert_eq!(loaded.right_dock_width, 380.0);

        let mut session = crate::session::SessionState::default();
        session.dock_open = Some(false);
        session.dock_tab = Some("git".to_string());
        session.dock_width = Some(420.0);

        let s_json = serde_json::to_string(&session).expect("serialize session");
        let s_loaded: crate::session::SessionState =
            serde_json::from_str(&s_json).expect("deserialize session");
        assert_eq!(s_loaded.dock_open, Some(false));
        assert_eq!(s_loaded.dock_tab, Some("git".to_string()));
        assert_eq!(s_loaded.dock_width, Some(420.0));
    }
}

