use modern_explorer_core::{
    archive::{ArchiveFormat, ArchiveService},
    cloud_backup::{CloudBackupService, ColorThemePackage, SettingsBackupPackage},
    features::{
        BatchAttributeTimestampOptions, ChecksumBatchService, ChecksumManifestFormat,
        FileBasketService, FileDiffService, FileSplitAndShredService, FileTagColor,
        FileTagService, GitIntegrationService, MetadataAndAttributeService,
        QuickCommandRunnerService, ShredMode, SmartExportFormat, SmartExportService,
        WorkspaceProfile, WorkspaceProfileService,
    },
    filesystem::{
        format_bytes, format_bytes_custom, sort_entries_advanced,
        DeepSearchCriteria, DeepSearchService, DirectoryEnumerator, DriveItem,
        DuplicateFinderService, DuplicateScanProgress, DuplicateScanResult, FileCreationService,
        FileEntry, SortColumn, SortDirection,
    },
    localization::LanguageCode,
    operations::{
        BatchRenameOptions, BatchRenameService, CaseTransform, ConflictResolution,
        DirectorySyncService, FileOperationExecutor, SyncComparisonMode, SyncDirectionMode,
        UndoAction, UndoHistoryService,
    },
    power_tools_v2::{
        ClipboardHistoryService, DataConverterService, FrecencyStore, ImageConvertOptions,
        ImageConverterService, LinkManagerService, LiveDirectoryWatcher, LocalShareServerState,
        NtfsAndLockInspectorService, PdfToolkitService, SelectionPresetsStore, SelectionRule,
        TransferQueueManager, UserCommandItem, UserCommandStore,
    },
    remote::{RemoteConnectionProfile, RemoteFileSystemService, RemoteProtocol},
    session::{CommandRegistry, FavoritesStore, SessionState, SessionStore},
    settings::{AppSettings, ColorSettingTarget, SettingsStore},
    treemap::{StorageTreemapService, TreemapFileCategory, TreemapNode},
    update::GitHubUpdateService,
    viewer::FileContentService,
};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Mutex,
};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BreadcrumbDto {
    pub label: String,
    pub full_path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileItemDto {
    pub full_path: String,
    pub name: String,
    pub display_name: String,
    pub extension: String,
    pub type_text: String,
    pub size_bytes: u64,
    pub effective_size: u64,
    pub size_text: String,
    pub size_bar_percent: f32,
    pub is_directory: bool,
    pub is_hidden: bool,
    pub is_readonly: bool,
    pub modified_text: String,
    pub age_hours: u32,
    pub tag_color_hex: Option<String>,
    pub tag_label: Option<String>,
    pub tag_note: Option<String>,
    pub link_target: Option<String>,
}

#[derive(Clone)]
pub struct DuplicatesJobState {
    pub running: bool,
    pub progress: DuplicateScanProgress,
    pub result: Option<DuplicateScanResult>,
    pub error: Option<String>,
    pub cancel_flag: Arc<AtomicBool>,
}

#[derive(Clone)]
pub struct TreemapJobState {
    pub running: bool,
    pub scanned_files: usize,
    pub scanned_folders: usize,
    pub current_path: String,
    pub root_path: String,
    pub result: Option<TreemapNode>,
    pub error: Option<String>,
    pub cancel_flag: Arc<AtomicBool>,
}

pub struct BackendState {
    pub settings: AppSettings,
    pub favorites: FavoritesStore,
    pub undo_service: UndoHistoryService,
    pub tag_service: FileTagService,
    pub workspace_service: WorkspaceProfileService,
    pub remote_service: RemoteFileSystemService,
    pub basket_service: FileBasketService,
    pub quick_runner: QuickCommandRunnerService,
    pub cached_treemap: Option<TreemapNode>,
    pub duplicates_job: Arc<Mutex<Option<DuplicatesJobState>>>,
    pub treemap_job: Arc<Mutex<Option<TreemapJobState>>>,
    pub selection_store: SelectionPresetsStore,
    pub transfer_queue: TransferQueueManager,
    pub frecency_store: FrecencyStore,
    pub clipboard_history: ClipboardHistoryService,
    pub user_commands: UserCommandStore,
    pub share_server: LocalShareServerState,
    pub fs_watcher: LiveDirectoryWatcher,
}

impl BackendState {
    pub fn new() -> Self {
        Self {
            settings: SettingsStore::load(),
            favorites: FavoritesStore::load(),
            undo_service: UndoHistoryService::default(),
            tag_service: FileTagService::load(),
            workspace_service: WorkspaceProfileService::load(),
            remote_service: RemoteFileSystemService::load(),
            basket_service: FileBasketService::default(),
            quick_runner: QuickCommandRunnerService::new(),
            cached_treemap: None,
            duplicates_job: Arc::new(Mutex::new(None)),
            treemap_job: Arc::new(Mutex::new(None)),
            selection_store: SelectionPresetsStore::load(),
            transfer_queue: TransferQueueManager::new(),
            frecency_store: FrecencyStore::load(),
            clipboard_history: ClipboardHistoryService::load(),
            user_commands: UserCommandStore::load(),
            share_server: LocalShareServerState::new(),
            fs_watcher: LiveDirectoryWatcher::new(),
        }
    }

    pub fn handle_rpc(&mut self, action: &str, params: &Value) -> Value {
        match action {
            "init" => self.rpc_init(),
            "list_dir" => self.rpc_list_dir(params),
            "open_item" => self.rpc_open_item(params),
            "copy_or_move" => self.rpc_copy_or_move(params),
            "delete_items" => self.rpc_delete_items(params),
            "rename_item" => self.rpc_rename_item(params),
            "create_item" => self.rpc_create_item(params),
            "undo" => self.rpc_undo(),
            "toggle_favorite" => self.rpc_toggle_favorite(params),
            "save_settings" => self.rpc_save_settings(params),
            "save_session" => self.rpc_save_session(params),
            "reset_color" => self.rpc_reset_color(params),
            "reset_treemap_colors" => self.rpc_reset_treemap_colors(),
            "reset_filetype_colors" => self.rpc_reset_filetype_colors(),
            "reset_font_sizes" => self.rpc_reset_font_sizes(),
            "reset_window_bounds" => self.rpc_reset_window_bounds(),
            "update_window_bounds" => self.rpc_update_window_bounds(params),
            "reset_all_settings" => self.rpc_reset_all_settings(),
            "open_config_dir" => self.rpc_open_config_dir(),
            "launch_external_tool" => self.rpc_launch_external_tool(params),
            "calc_folder_size" => self.rpc_calc_folder_size(params),
            "autocomplete_path" => self.rpc_autocomplete_path(params),
            "open_terminal" => self.rpc_open_terminal(params),
            "quick_preview" => self.rpc_quick_preview(params),
            "save_file_text" => self.rpc_save_file_text(params),
            "batch_rename_preview" => self.rpc_batch_rename_preview(params),
            "batch_rename_execute" => self.rpc_batch_rename_execute(params),
            "file_diff" => self.rpc_file_diff(params),
            "checksums" => self.rpc_checksums(params),
            "split_or_join_or_shred" => self.rpc_split_or_join_or_shred(params),
            "set_tag" => self.rpc_set_tag(params),
            "workspaces" => self.rpc_workspaces(params),
            "smart_export" => self.rpc_smart_export(params),
            "basket" => self.rpc_basket(params),
            "attr_exif" => self.rpc_attr_exif(params),
            "quick_console" => self.rpc_quick_console(params),
            "git_panel" => self.rpc_git_panel(params),
            "dir_sync" => self.rpc_dir_sync(params),
            "deep_search" => self.rpc_deep_search(params),
            "duplicates" => self.rpc_duplicates(params),
            "duplicates_start" => self.rpc_duplicates_start(params),
            "duplicates_poll" => self.rpc_duplicates_poll(),
            "duplicates_cancel" => self.rpc_duplicates_cancel(),
            "treemap" => self.rpc_treemap(params),
            "archive" => self.rpc_archive(params),
            "remote" => self.rpc_remote(params),
            "selection_engine" => self.rpc_selection_engine(params),
            "transfer_queue" => self.rpc_transfer_queue(params),
            "dir_tree" => self.rpc_dir_tree(params),
            "smart_jump" => self.rpc_smart_jump(params),
            "image_converter" => self.rpc_image_converter(params),
            "pdf_toolkit" => self.rpc_pdf_toolkit(params),
            "file_vault" => self.rpc_file_vault(params),
            "ntfs_locksmith" => self.rpc_ntfs_locksmith(params),
            "hex_editor" => self.rpc_hex_editor(params),
            "data_converter" => self.rpc_data_converter(params),
            "user_commands" => self.rpc_user_commands(params),
            "share_server" => self.rpc_share_server(params),
            "fs_watcher_poll" => self.rpc_fs_watcher_poll(),
            "link_manager" => self.rpc_link_manager(params),
            "check_updates" => self.rpc_check_updates(params),
            "install_update" => self.rpc_install_update(params),
            "cloud_backup_detect" => self.rpc_cloud_backup_detect(),
            "cloud_backup_save" => self.rpc_cloud_backup_save(params),
            "cloud_backup_list" => self.rpc_cloud_backup_list(params),
            "cloud_backup_restore" => self.rpc_cloud_backup_restore(params),
            "cloud_backup_check_fresh_install" => self.rpc_cloud_backup_check_fresh_install(),
            "cloud_backup_save_provider_config" => self.rpc_cloud_backup_save_provider_config(params),
            "cloud_backup_test_provider" => self.rpc_cloud_backup_test_provider(params),
            "cloud_backup_pick_folder" => self.rpc_cloud_backup_pick_folder(),
            "open_cloud_backup_dir" => self.rpc_open_cloud_backup_dir(params),
            "export_color_theme" => self.rpc_export_color_theme(params),
            "import_color_theme" => self.rpc_import_color_theme(params),
            "export_settings_package" => self.rpc_export_settings_package(),
            "import_settings_package" => self.rpc_import_settings_package(params),
            "open_url" => {
                if let Some(u) = params.get("url").and_then(|v| v.as_str()) {
                    GitHubUpdateService::open_release_in_browser(Some(u));
                } else {
                    GitHubUpdateService::open_release_in_browser(None);
                }
                json!({ "ok": true })
            }
            _ => json!({ "error": format!("Unknown action: {}", action) }),
        }
    }

    fn rpc_init(&self) -> Value {
        let cwd = std::env::current_dir()
            .unwrap_or_else(|_| PathBuf::from("."))
            .to_string_lossy()
            .to_string();

        let drives: Vec<Value> = DriveItem::enumerate_drives()
            .into_iter()
            .map(|d| {
                let pct = (d.used_fraction() * 100.0).round() as u32;
                let short = d.root_path.trim_end_matches('\\').to_string();
                json!({
                    "root_path": d.root_path,
                    "display_text": if d.name.is_empty() {
                        format!("{} ({})", short, format_bytes(d.available_bytes))
                    } else {
                        format!("{} [{}] ({})", short, d.name, format_bytes(d.available_bytes))
                    },
                    "tooltip": format!("{} ({}) – {} frei von {} ({}% belegt)", d.root_path, d.kind_label, format_bytes(d.available_bytes), format_bytes(d.total_bytes), pct),
                    "total_bytes": d.total_bytes,
                    "available_bytes": d.available_bytes,
                    "used_percent": pct,
                })
            })
            .collect();

        let templates: Vec<Value> = FileCreationService::templates()
            .iter()
            .map(|t| {
                json!({
                    "id": t.id,
                    "display_name": t.display_name,
                    "default_file_name": t.default_file_name,
                    "initial_content": t.initial_content,
                })
            })
            .collect();

        let commands: Vec<Value> = CommandRegistry::all_commands()
            .iter()
            .map(|c| {
                json!({
                    "id": c.id,
                    "title": c.title,
                    "shortcut": c.shortcut,
                    "category": c.category,
                })
            })
            .collect();

        let languages: Vec<Value> = LanguageCode::all_options()
            .iter()
            .map(|(code, label)| json!({ "code": code, "label": label }))
            .collect();

        let default_release_md = format!(
            "## What's New in Modern Explorer v{}\n\
            - **Studio Design Edition (`Nuharji/markdown-studio` Style) + 37 integrierte Power-Tools**: Modernste Studio-Oberfläche mit Menüleiste, Optionsleiste, vertikaler Werkzeug-Rail, einklappbarem Ordner-Baum (`Alt+F10`), Dual-Pane-Workspace, rechtem Inspector-Dock und 4 Studio-Themes (`Dark`, `Light`, `GitHub Light`, `GitHub Dark`).\n\
            - **Synchrones Navigieren & Scrollen (`Sync-Lock`), Smart Jump (`Alt+Down`), Erweiterte Auswahl-Engine (`Num + / - / *`), Hintergrund-Transfer-Queue mit Speed-Limit & Live-Filesystem-Watcher (`notify`)**.\n\
            - **Batch-Bildkonverter, PDF-Werkzeugkasten (`lopdf`), AES-256-GCM Datei-Tresor (`.menc`), NTFS-ADS & File-Locksmith-Inspector, Interaktiver Hex-Editor, Strukturierter Daten-Konverter (`JSON/YAML/TOML/CSV`), Benutzerdefinierte Toolbar-Tools (`%P`, `%T`, `%N`), Lokaler HTTP/QR-Code-Dateiserver, Symlink/Hardlink/Junction-Manager, `.7z`/`.tar.zst`/`.tar.xz`/`.rar`-Archive & WebDAV/S3-Cloud-Speicher**.",
            GitHubUpdateService::CURRENT_VERSION,
        );

        let saved_session = SessionStore::load();

        json!({
            "version": GitHubUpdateService::CURRENT_VERSION,
            "releases_url": GitHubUpdateService::RELEASES_URL,
            "default_release_notes": GitHubUpdateService::format_release_notes_for_display(&default_release_md),
            "cwd": cwd,
            "settings": self.settings,
            "session": saved_session,
            "favorites": self.favorites.items,
            "workspaces": self.workspace_service.profiles,
            "remote_profiles": self.remote_service.profiles,
            "selection_presets": self.selection_store.presets,
            "user_commands": self.user_commands.commands,
            "drives": drives,
            "templates": templates,
            "commands": commands,
            "languages": languages,
            "can_undo": self.undo_service.can_undo(),
            "undo_desc": self.undo_service.last_description().unwrap_or(""),
            "basket_count": self.basket_service.count(),
            "basket_summary": self.basket_service.summary_label(),
        })
    }

    fn rpc_list_dir(&mut self, params: &Value) -> Value {
        let raw_path = params
            .get("path")
            .and_then(|v| v.as_str())
            .unwrap_or(".");
        let branch_view = params
            .get("branch_view")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let show_tree_sizes = params
            .get("show_tree_sizes")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let filter_query = params
            .get("filter")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        let sort_col_str = params
            .get("sort_col")
            .and_then(|v| v.as_str())
            .unwrap_or("Name");
        let sort_asc = params
            .get("sort_asc")
            .and_then(|v| v.as_bool())
            .unwrap_or(true);

        let side = params
            .get("side")
            .and_then(|v| v.as_str())
            .unwrap_or("left");

        let is_virtual = ArchiveService::is_virtual_archive_path(raw_path)
            || (Path::new(raw_path).is_file() && ArchiveService::is_supported_archive(Path::new(raw_path)));

        let (canonical, clean_path, mut entries) = if is_virtual {
            let effective_virtual_path = if ArchiveService::is_virtual_archive_path(raw_path) {
                raw_path.to_string()
            } else {
                ArchiveService::build_virtual_archive_path(raw_path, "")
            };
            let entries = ArchiveService::list_virtual_archive_entries(&effective_virtual_path)
                .unwrap_or_default();
            (PathBuf::from(&effective_virtual_path), effective_virtual_path, entries)
        } else {
            let path_obj = Path::new(raw_path);
            let canonical = if path_obj.is_dir() {
                path_obj.to_path_buf()
            } else {
                std::env::current_dir().unwrap_or_else(|_| PathBuf::from("."))
            };
            let clean_path = canonical.to_string_lossy().to_string();
            self.frecency_store.record_visit(&clean_path);
            if self.settings.enable_live_fs_watcher {
                self.fs_watcher.update_watch(side, &clean_path);
            }
            let entries = DirectoryEnumerator::list_directory(
                &canonical,
                self.settings.show_hidden_files,
                branch_view,
            );
            (canonical, clean_path, entries)
        };

        if show_tree_sizes {
            for e in &mut entries {
                if e.is_directory {
                    let node = StorageTreemapService::scan_directory_tree(&e.full_path, 3);
                    e.allocated_tree_size = Some(node.size_bytes);
                }
            }
        }

        let sort_col = match sort_col_str {
            "Size" => SortColumn::Size,
            "Modified" => SortColumn::Modified,
            "Extension" => SortColumn::Extension,
            _ => SortColumn::Name,
        };
        let sort_dir = if sort_asc {
            SortDirection::Ascending
        } else {
            SortDirection::Descending
        };
        sort_entries_advanced(
            &mut entries,
            sort_col,
            sort_dir,
            self.settings.sort_folders_first,
            self.settings.natural_number_sorting,
            self.settings.case_sensitive_sorting,
        );

        let total_unfiltered = entries.len();
        let filtered = DirectoryEnumerator::filter_entries(
            &entries,
            filter_query,
            Some(&self.tag_service),
        );

        let max_size = filtered
            .iter()
            .map(|e| e.effective_size())
            .max()
            .unwrap_or(1)
            .max(1);

        let show_exts = self.settings.show_file_extensions;
        let size_fmt_mode = self.settings.size_display_format.as_str();
        let date_fmt_str = match self.settings.date_time_format.as_str() {
            "de_sec" => "%d.%m.%Y %H:%M:%S",
            "iso" => "%Y-%m-%d %H:%M",
            "iso_sec" => "%Y-%m-%d %H:%M:%S",
            "us" => "%m/%d/%Y %I:%M %p",
            _ => "%d.%m.%Y %H:%M",
        };
        let now_local = chrono::Local::now();
        let mut total_bytes = 0u64;
        let items: Vec<FileItemDto> = filtered
            .into_iter()
            .map(|e| {
                let eff = e.effective_size();
                total_bytes = total_bytes.saturating_add(eff);
                let pct = if eff > 0 {
                    ((eff as f64 / max_size as f64) * 100.0).clamp(3.0, 100.0) as f32
                } else {
                    0.0
                };
                let tag_info = self.tag_service.get(&e.full_path);
                let (tag_hex, tag_lbl, tag_note) = if let Some(t) = tag_info {
                    let hex = if t.color != FileTagColor::None {
                        Some(t.color.hex_color().to_string())
                    } else {
                        None
                    };
                    let badge = if !t.note.is_empty() {
                        Some(format!("🏷 {}", t.note))
                    } else if t.color != FileTagColor::None {
                        Some(format!("{:?}", t.color))
                    } else {
                        None
                    };
                    (hex, badge, Some(t.note.clone()))
                } else {
                    (None, None, None)
                };

                let lang = LanguageCode::from_setting(&self.settings.language);
                let (folder_word, file_word) = match lang {
                    LanguageCode::De => ("Dateiordner", "Datei"),
                    LanguageCode::Es => ("Carpeta", "Archivo"),
                    LanguageCode::Fr => ("Dossier", "Fichier"),
                    LanguageCode::It => ("Cartella", "File"),
                    LanguageCode::Nl => ("Map", "Bestand"),
                    LanguageCode::Pl => ("Folder", "Plik"),
                    LanguageCode::Pt => ("Pasta", "Arquivo"),
                    LanguageCode::Ja => ("フォルダ", "ファイル"),
                    LanguageCode::Zh => ("文件夹", "文件"),
                    LanguageCode::En => ("Folder", "File"),
                };
                let type_text = if e.is_directory {
                    folder_word.to_string()
                } else if e.extension.is_empty() {
                    file_word.to_string()
                } else {
                    format!(
                        "{} {}",
                        e.extension.trim_start_matches('.').to_ascii_uppercase(),
                        file_word
                    )
                };

                let base_display = e.relative_path.clone().unwrap_or_else(|| e.name.clone());
                let display_name = if !show_exts && !e.is_directory && !e.extension.is_empty() {
                    base_display
                        .strip_suffix(&e.extension)
                        .unwrap_or(&base_display)
                        .to_string()
                } else {
                    base_display
                };

                let link_target = if !is_virtual {
                    LinkManagerService::read_link_target(Path::new(&e.full_path))
                } else {
                    None
                };

                let size_text = if e.is_directory {
                    if let Some(tree_bytes) = e.allocated_tree_size {
                        format_bytes_custom(tree_bytes, size_fmt_mode)
                    } else {
                        "<DIR>".to_string()
                    }
                } else {
                    format_bytes_custom(e.size_bytes, size_fmt_mode)
                };

                let age_hours = now_local
                    .signed_duration_since(e.modified)
                    .num_hours()
                    .max(0) as u32;

                FileItemDto {
                    full_path: e.full_path.clone(),
                    display_name,
                    name: e.name.clone(),
                    extension: e.extension.clone(),
                    type_text,
                    size_bytes: e.size_bytes,
                    effective_size: eff,
                    size_text,
                    size_bar_percent: pct,
                    is_directory: e.is_directory,
                    is_hidden: e.is_hidden,
                    is_readonly: e.is_readonly,
                    modified_text: e.modified.format(date_fmt_str).to_string(),
                    age_hours,
                    tag_color_hex: tag_hex,
                    tag_label: tag_lbl,
                    tag_note,
                    link_target,
                }
            })
            .collect();

        // Build breadcrumbs
        let mut breadcrumbs = Vec::new();
        let (git_badge, folder_title) = if is_virtual {
            if let Some((archive_file, inner_rel)) = ArchiveService::parse_virtual_archive_path(&clean_path) {
                let arch_p = Path::new(&archive_file);
                let mut cur = arch_p.parent();
                let mut disk_crumbs = Vec::new();
                while let Some(p) = cur {
                    let label = p
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .filter(|s| !s.is_empty())
                        .unwrap_or_else(|| p.to_string_lossy().trim_end_matches('\\').to_string());
                    disk_crumbs.push(BreadcrumbDto {
                        label,
                        full_path: p.to_string_lossy().to_string(),
                    });
                    cur = p.parent();
                }
                disk_crumbs.reverse();
                breadcrumbs.extend(disk_crumbs);

                let arch_name = arch_p
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| archive_file.clone());
                breadcrumbs.push(BreadcrumbDto {
                    label: format!("📦 {}", arch_name),
                    full_path: ArchiveService::build_virtual_archive_path(&archive_file, ""),
                });

                if !inner_rel.is_empty() {
                    let mut accum = String::new();
                    for seg in inner_rel.split('/') {
                        if seg.is_empty() {
                            continue;
                        }
                        if !accum.is_empty() {
                            accum.push('/');
                        }
                        accum.push_str(seg);
                        breadcrumbs.push(BreadcrumbDto {
                            label: seg.to_string(),
                            full_path: ArchiveService::build_virtual_archive_path(&archive_file, &accum),
                        });
                    }
                }

                let title = if inner_rel.is_empty() {
                    format!("📦 {}", arch_name)
                } else {
                    inner_rel
                        .split('/')
                        .filter(|s| !s.is_empty())
                        .last()
                        .unwrap_or(&arch_name)
                        .to_string()
                };
                (None, title)
            } else {
                (None, clean_path.clone())
            }
        } else {
            let mut cur: Option<&Path> = Some(&canonical);
            while let Some(p) = cur {
                let label = p
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .filter(|s| !s.is_empty())
                    .unwrap_or_else(|| p.to_string_lossy().trim_end_matches('\\').to_string());
                breadcrumbs.push(BreadcrumbDto {
                    label,
                    full_path: p.to_string_lossy().to_string(),
                });
                cur = p.parent();
            }
            breadcrumbs.reverse();

            // Pure-Rust instant .git/HEAD lookup (0 process spawns!)
            let git = GitIntegrationService::get_quick_badge(&canonical);
            let title = canonical
                .file_name()
                .map(|n| n.to_string_lossy().to_string())
                .filter(|s| !s.is_empty())
                .unwrap_or_else(|| clean_path.clone());
            (git, title)
        };

        let is_fav = self.favorites.is_favorite(&clean_path);

        json!({
            "path": clean_path,
            "title": folder_title,
            "is_favorite": is_fav,
            "git_badge": git_badge,
            "breadcrumbs": breadcrumbs,
            "total_unfiltered": total_unfiltered,
            "total_bytes_text": format_bytes(total_bytes),
            "items": items,
        })
    }

    fn rpc_open_item(&self, params: &Value) -> Value {
        if let Some(p) = params.get("path").and_then(|v| v.as_str()) {
            if ArchiveService::is_virtual_archive_path(p) {
                if let Some((arch_file, inner_path)) = ArchiveService::parse_virtual_archive_path(p) {
                    if let Ok(extracted) = ArchiveService::extract_single_entry_to_temp(&arch_file, &inner_path) {
                        let _ = open::that(&extracted);
                        return json!({ "ok": true, "temp_path": extracted.to_string_lossy() });
                    }
                }
            }
            let _ = open::that(p);
            json!({ "ok": true })
        } else {
            json!({ "error": "Missing path" })
        }
    }

    fn rpc_copy_or_move(&mut self, params: &Value) -> Value {
        let is_move = params
            .get("is_move")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let dest_dir_str = params
            .get("dest_dir")
            .and_then(|v| v.as_str())
            .unwrap_or(".");

        if ArchiveService::is_virtual_archive_path(dest_dir_str) {
            return json!({
                "ok": false,
                "error": "Kopieren in ein virtuelles Archiv wird aktuell noch nicht unterstützt."
            });
        }

        let dest_dir = PathBuf::from(dest_dir_str);
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|arr| {
                arr.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();

        let mut count = 0usize;
        let mut virtual_archive_groups: std::collections::HashMap<String, Vec<String>> =
            std::collections::HashMap::new();
        let mut normal_paths: Vec<String> = Vec::new();

        for p in paths {
            if ArchiveService::is_virtual_archive_path(&p) {
                if let Some((arch_file, inner_path)) = ArchiveService::parse_virtual_archive_path(&p) {
                    virtual_archive_groups
                        .entry(arch_file)
                        .or_default()
                        .push(inner_path);
                }
            } else {
                normal_paths.push(p);
            }
        }

        // Process virtual archive paths (extracting to dest_dir)
        for (arch_file, inners) in virtual_archive_groups {
            let arch_path = Path::new(&arch_file);
            match ArchiveService::extract_virtual_entries(arch_path, &inners, &dest_dir) {
                Ok(extracted_count) => {
                    count += extracted_count;
                    let display_name = arch_path
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_else(|| arch_file.clone());
                    self.undo_service.record(
                        format!("Aus Archiv entpackt ({display_name}): {extracted_count} Element(e)"),
                        UndoAction::Copied {
                            copied_path: dest_dir.to_string_lossy().to_string(),
                        },
                    );
                }
                Err(err) => {
                    return json!({
                        "ok": false,
                        "error": format!("Fehler beim Entpacken aus dem Archiv: {err}")
                    });
                }
            }
        }

        // Process normal filesystem paths
        for src_str in normal_paths {
            let src = Path::new(&src_str);
            if is_move {
                if let Ok(Some(moved)) =
                    FileOperationExecutor::move_entry(src, &dest_dir, ConflictResolution::KeepBoth)
                {
                    self.undo_service.record(
                        format!("Verschoben: {}", src.display()),
                        UndoAction::Moved {
                            original_path: src_str,
                            moved_path: moved.to_string_lossy().to_string(),
                        },
                    );
                    count += 1;
                }
            } else if let Ok(Some(copied)) =
                FileOperationExecutor::copy_entry(src, &dest_dir, ConflictResolution::KeepBoth)
            {
                self.undo_service.record(
                    format!("Kopiert: {}", src.display()),
                    UndoAction::Copied {
                        copied_path: copied.to_string_lossy().to_string(),
                    },
                );
                count += 1;
            }
        }

        json!({
            "ok": true,
            "count": count,
            "can_undo": self.undo_service.can_undo(),
            "undo_desc": self.undo_service.last_description().unwrap_or(""),
        })
    }

    fn rpc_delete_items(&mut self, params: &Value) -> Value {
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|arr| {
                arr.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();
        let mut count = 0usize;
        for p in &paths {
            if FileOperationExecutor::delete_entry(Path::new(p)).is_ok() {
                count += 1;
            }
        }
        if let Some(root) = &mut self.cached_treemap {
            let set: HashSet<String> = paths.into_iter().collect();
            StorageTreemapService::remove_paths_from_tree(root, &set);
        }
        json!({ "ok": true, "count": count })
    }

    fn rpc_rename_item(&mut self, params: &Value) -> Value {
        let path = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        let new_name = params
            .get("new_name")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .trim();
        let src = Path::new(path);
        if let Some(par) = src.parent() {
            let dst = par.join(new_name);
            match fs::rename(src, &dst) {
                Ok(()) => {
                    self.undo_service.record(
                        format!("Umbenannt in {}", new_name),
                        UndoAction::Rename {
                            original_path: path.to_string(),
                            current_path: dst.to_string_lossy().to_string(),
                        },
                    );
                    return json!({
                        "ok": true,
                        "can_undo": self.undo_service.can_undo(),
                        "undo_desc": self.undo_service.last_description().unwrap_or(""),
                    });
                }
                Err(e) => return json!({ "error": e.to_string() }),
            }
        }
        json!({ "error": "Ungültiger Pfad" })
    }

    fn rpc_create_item(&mut self, params: &Value) -> Value {
        let dir = Path::new(params.get("dir").and_then(|v| v.as_str()).unwrap_or("."));
        let name = params
            .get("name")
            .and_then(|v| v.as_str())
            .unwrap_or("NewItem");
        let is_folder = params
            .get("is_folder")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let content = params.get("content").and_then(|v| v.as_str()).unwrap_or("");

        let res = if is_folder {
            FileCreationService::create_folder(dir, name)
        } else {
            FileCreationService::create_file(dir, name, content)
        };
        match res {
            Ok(created) => {
                self.undo_service.record(
                    format!("Erstellt: {}", name),
                    UndoAction::CreatedFileOrDir {
                        created_path: created.to_string_lossy().to_string(),
                    },
                );
                json!({
                    "ok": true,
                    "created_path": created.to_string_lossy().to_string(),
                    "can_undo": self.undo_service.can_undo(),
                    "undo_desc": self.undo_service.last_description().unwrap_or(""),
                })
            }
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_undo(&mut self) -> Value {
        match self.undo_service.undo_last() {
            Ok(desc) => json!({
                "ok": true,
                "desc": desc,
                "can_undo": self.undo_service.can_undo(),
                "undo_desc": self.undo_service.last_description().unwrap_or(""),
            }),
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_toggle_favorite(&mut self, params: &Value) -> Value {
        let path = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        let added = self.favorites.toggle(path);
        json!({
            "ok": true,
            "is_favorite": added,
            "favorites": self.favorites.items,
        })
    }

    fn rpc_save_settings(&mut self, params: &Value) -> Value {
        if let Some(s_val) = params.get("settings") {
            if let Ok(mut updated) = serde_json::from_value::<AppSettings>(s_val.clone()) {
                updated.main_window_x = self.settings.main_window_x;
                updated.main_window_y = self.settings.main_window_y;
                updated.main_window_maximized = self.settings.main_window_maximized;
                self.settings = updated;
                SettingsStore::save(&self.settings);
                if self.settings.auto_cloud_backup_enabled {
                    let _ = CloudBackupService::create_cloud_backup(
                        &self.settings.cloud_backup_provider,
                        self.settings.custom_cloud_backup_dir.as_deref(),
                        Some(&self.settings.cloud_provider_paths),
                        &self.settings,
                        Some(self.favorites.items.clone()),
                        Some(self.user_commands.commands.clone()),
                        Some("Automatisches Cloud-Backup".to_string()),
                        GitHubUpdateService::CURRENT_VERSION,
                    );
                }
                return json!({ "ok": true, "settings": self.settings });
            }
        }
        json!({ "error": "Invalid settings payload" })
    }

    fn rpc_save_session(&self, params: &Value) -> Value {
        if let Some(sess_val) = params.get("session") {
            if let Ok(state) = serde_json::from_value::<SessionState>(sess_val.clone()) {
                SessionStore::save(&state);
                return json!({ "ok": true });
            }
        }
        json!({ "error": "Invalid session payload" })
    }

    fn rpc_reset_color(&mut self, params: &Value) -> Value {
        let target_str = params.get("target").and_then(|v| v.as_str()).unwrap_or("");
        let target = match target_str {
            "ActivePanelBorder" => ColorSettingTarget::ActivePanelBorder,
            "SelectionBorder" => ColorSettingTarget::SelectionBorder,
            "SelectionBackground" => ColorSettingTarget::SelectionBackground,
            "SelectionText" => ColorSettingTarget::SelectionText,
            "MarkedItemText" => ColorSettingTarget::MarkedItemText,
            "MarkedItemBackground" => ColorSettingTarget::MarkedItemBackground,
            "FileListBackground" => ColorSettingTarget::FileListBackground,
            "FileListAltBackground" => ColorSettingTarget::FileListAltBackground,
            "FileListText" => ColorSettingTarget::FileListText,
            "FolderText" => ColorSettingTarget::FolderText,
            "HiddenItemText" => ColorSettingTarget::HiddenItemText,
            "ReadonlyItemText" => ColorSettingTarget::ReadonlyItemText,
            "GridLine" => ColorSettingTarget::GridLine,
            "TreemapVideoMedia" => ColorSettingTarget::TreemapVideoMedia,
            "TreemapArchiveImage" => ColorSettingTarget::TreemapArchiveImage,
            "TreemapVirtualDiskSystem" => ColorSettingTarget::TreemapVirtualDiskSystem,
            "TreemapCodeDocument" => ColorSettingTarget::TreemapCodeDocument,
            "TreemapPictureGraphic" => ColorSettingTarget::TreemapPictureGraphic,
            "TreemapBinaryExecutable" => ColorSettingTarget::TreemapBinaryExecutable,
            _ => ColorSettingTarget::TreemapOtherTemp,
        };
        self.settings.reset_color_to_default(target);
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_reset_treemap_colors(&mut self) -> Value {
        self.settings.reset_treemap_colors_to_default();
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_reset_filetype_colors(&mut self) -> Value {
        self.settings.reset_filetype_colors_to_default();
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_reset_font_sizes(&mut self) -> Value {
        self.settings.reset_font_sizes_to_default();
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_reset_window_bounds(&mut self) -> Value {
        self.settings.reset_window_bounds_to_default();
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_update_window_bounds(&mut self, params: &Value) -> Value {
        let key = params
            .get("key")
            .and_then(|v| v.as_str())
            .unwrap_or("modalTool");
        let width = params
            .get("width")
            .and_then(|v| v.as_f64())
            .unwrap_or(960.0) as f32;
        let height = params
            .get("height")
            .and_then(|v| v.as_f64())
            .unwrap_or(680.0) as f32;
        let maximized = params
            .get("maximized")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        self.settings
            .update_dialog_bounds(key, width, height, maximized);
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_reset_all_settings(&mut self) -> Value {
        self.settings = AppSettings::default();
        SettingsStore::save(&self.settings);
        json!({ "ok": true, "settings": self.settings })
    }

    fn rpc_open_config_dir(&self) -> Value {
        let dir = SettingsStore::config_dir();
        let _ = open::that(&dir);
        json!({ "ok": true, "path": dir.to_string_lossy() })
    }

    fn rpc_cloud_backup_detect(&self) -> Value {
        let providers = CloudBackupService::detect_providers(
            self.settings.custom_cloud_backup_dir.as_deref(),
            Some(&self.settings.cloud_provider_paths),
        );
        let active_dir = CloudBackupService::get_backup_dir(
            &self.settings.cloud_backup_provider,
            self.settings.custom_cloud_backup_dir.as_deref(),
            Some(&self.settings.cloud_provider_paths),
        )
        .map(|p| p.to_string_lossy().to_string())
        .ok();

        let active_prov_info = providers.iter().find(|p| p.id == self.settings.cloud_backup_provider).cloned();

        json!({
            "ok": true,
            "providers": providers,
            "active_provider": active_prov_info,
            "active_provider_id": self.settings.cloud_backup_provider,
            "active_dir": active_dir,
            "cloud_provider_paths": self.settings.cloud_provider_paths
        })
    }

    fn rpc_cloud_backup_save(&mut self, params: &Value) -> Value {
        let provider = params
            .get("provider")
            .and_then(|v| v.as_str())
            .unwrap_or(&self.settings.cloud_backup_provider);
        let desc = params.get("description").and_then(|v| v.as_str()).map(|s| s.to_string());
        let custom_path = self.settings.custom_cloud_backup_dir.as_deref();

        match CloudBackupService::create_cloud_backup(
            provider,
            custom_path,
            Some(&self.settings.cloud_provider_paths),
            &self.settings,
            Some(self.favorites.items.clone()),
            Some(self.user_commands.commands.clone()),
            desc,
            GitHubUpdateService::CURRENT_VERSION,
        ) {
            Ok(info) => json!({ "ok": true, "backup": info }),
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_cloud_backup_list(&self, params: &Value) -> Value {
        let provider_filter = params.get("provider").and_then(|v| v.as_str());
        let custom_path = self.settings.custom_cloud_backup_dir.as_deref();
        let list = CloudBackupService::list_backups(
            provider_filter,
            custom_path,
            Some(&self.settings.cloud_provider_paths),
        );
        json!({ "ok": true, "backups": list })
    }

    fn rpc_cloud_backup_restore(&mut self, params: &Value) -> Value {
        let path_str = match params.get("file_path").and_then(|v| v.as_str()) {
            Some(p) => p,
            None => return json!({ "error": "file_path fehlt" }),
        };
        let pb = PathBuf::from(path_str);
        match CloudBackupService::load_backup_from_file(&pb) {
            Ok(pkg) => {
                let prev_x = self.settings.main_window_x;
                let prev_y = self.settings.main_window_y;
                let prev_max = self.settings.main_window_maximized;
                self.settings = pkg.settings;
                self.settings.main_window_x = prev_x;
                self.settings.main_window_y = prev_y;
                self.settings.main_window_maximized = prev_max;
                self.settings.first_run_completed = true;
                SettingsStore::save(&self.settings);
                if let Some(favs) = pkg.favorites {
                    self.favorites.items = favs;
                    self.favorites.save();
                }
                if let Some(cmds) = pkg.user_commands {
                    self.user_commands.commands = cmds;
                    self.user_commands.save();
                }
                json!({
                    "ok": true,
                    "settings": self.settings,
                    "favorites": self.favorites.items,
                    "message": "Backup erfolgreich wiederhergestellt!"
                })
            }
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_cloud_backup_check_fresh_install(&self) -> Value {
        let is_fresh = !self.settings.first_run_completed || SettingsStore::is_fresh_install();
        let backup = if is_fresh {
            CloudBackupService::scan_for_fresh_install_backups(Some(&self.settings.cloud_provider_paths))
        } else {
            None
        };
        let detected_backups = if let Some(ref b) = backup { vec![b.clone()] } else { vec![] };
        json!({
            "is_fresh_install": is_fresh,
            "found_backup": backup,
            "detected_backups": detected_backups
        })
    }

    fn rpc_open_cloud_backup_dir(&self, params: &Value) -> Value {
        let provider = params
            .get("provider")
            .and_then(|v| v.as_str())
            .unwrap_or(&self.settings.cloud_backup_provider);
        let custom_path = self.settings.custom_cloud_backup_dir.as_deref();
        match CloudBackupService::get_backup_dir(
            provider,
            custom_path,
            Some(&self.settings.cloud_provider_paths),
        ) {
            Ok(dir) => {
                let _ = open::that(&dir);
                json!({ "ok": true, "path": dir.to_string_lossy().to_string() })
            }
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_cloud_backup_save_provider_config(&mut self, params: &Value) -> Value {
        let provider_id = match params.get("provider_id").and_then(|v| v.as_str()) {
            Some(id) => id.to_string(),
            None => return json!({ "error": "provider_id fehlt" }),
        };
        let path = params.get("path").and_then(|v| v.as_str()).map(|s| s.trim().to_string());
        let set_as_active = params.get("set_as_active").and_then(|v| v.as_bool()).unwrap_or(false);

        if let Some(p) = path {
            if p.is_empty() {
                self.settings.cloud_provider_paths.remove(&provider_id);
                if provider_id == "custom" {
                    self.settings.custom_cloud_backup_dir = None;
                }
            } else {
                self.settings.cloud_provider_paths.insert(provider_id.clone(), p.clone());
                if provider_id == "custom" {
                    self.settings.custom_cloud_backup_dir = Some(p);
                }
            }
        }
        if set_as_active {
            self.settings.cloud_backup_provider = provider_id;
        }
        SettingsStore::save(&self.settings);

        let providers = CloudBackupService::detect_providers(
            self.settings.custom_cloud_backup_dir.as_deref(),
            Some(&self.settings.cloud_provider_paths),
        );
        json!({
            "ok": true,
            "providers": providers,
            "active_provider": self.settings.cloud_backup_provider,
            "cloud_provider_paths": self.settings.cloud_provider_paths
        })
    }

    fn rpc_cloud_backup_test_provider(&self, params: &Value) -> Value {
        let provider_id = match params.get("provider_id").and_then(|v| v.as_str()) {
            Some(id) => id,
            None => return json!({ "error": "provider_id fehlt" }),
        };
        let path_override = params.get("path").and_then(|v| v.as_str());
        let (ok, message) = CloudBackupService::test_provider(
            provider_id,
            path_override,
            Some(&self.settings.cloud_provider_paths),
        );
        json!({ "ok": ok, "message": message })
    }

    fn rpc_cloud_backup_pick_folder(&self) -> Value {
        #[cfg(target_os = "windows")]
        {
            use std::process::Command;
            let ps_script = r#"
Add-Type -AssemblyName System.Windows.Forms
$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = "Wählen Sie das Stammverzeichnis Ihres Cloud-Speichers"
$dialog.ShowNewFolderButton = $true
if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    Write-Output $dialog.SelectedPath
}
"#;
            let mut cmd = Command::new("powershell");
            cmd.args(&["-NoProfile", "-NonInteractive", "-Command", ps_script]);
            #[cfg(windows)]
            {
                use std::os::windows::process::CommandExt;
                const CREATE_NO_WINDOW: u32 = 0x08000000;
                cmd.creation_flags(CREATE_NO_WINDOW);
            }
            if let Ok(output) = cmd.output() {
                if output.status.success() {
                    let path_str = String::from_utf8_lossy(&output.stdout).trim().to_string();
                    if !path_str.is_empty() {
                        return json!({ "ok": true, "path": path_str });
                    }
                }
            }
        }
        json!({ "ok": false, "cancelled": true })
    }

    fn rpc_export_color_theme(&self, params: &Value) -> Value {
        let name = params.get("name").and_then(|v| v.as_str()).unwrap_or("Mein Farbschema");
        let author = params.get("author").and_then(|v| v.as_str());
        let desc = params.get("description").and_then(|v| v.as_str());
        let theme = CloudBackupService::export_color_theme(
            &self.settings,
            name,
            author,
            desc,
            GitHubUpdateService::CURRENT_VERSION,
        );
        json!({ "ok": true, "theme": theme })
    }

    fn rpc_import_color_theme(&mut self, params: &Value) -> Value {
        let theme_val = match params.get("theme") {
            Some(v) => v,
            None => return json!({ "error": "theme parameter fehlt" }),
        };
        match serde_json::from_value::<ColorThemePackage>(theme_val.clone()) {
            Ok(theme) => {
                CloudBackupService::apply_color_theme(&mut self.settings, &theme);
                SettingsStore::save(&self.settings);
                json!({
                    "ok": true,
                    "settings": self.settings,
                    "theme_name": theme.name
                })
            }
            Err(e) => json!({ "error": format!("Ungültiges Farbschema-Format: {}", e) }),
        }
    }

    fn rpc_export_settings_package(&self) -> Value {
        let pkg = SettingsBackupPackage {
            schema_type: CloudBackupService::SCHEMA_TYPE_BACKUP.to_string(),
            schema_version: CloudBackupService::CURRENT_SCHEMA_VERSION,
            app_version: GitHubUpdateService::CURRENT_VERSION.to_string(),
            created_at: chrono::Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
            device_name: std::env::var("COMPUTERNAME").unwrap_or_else(|_| "Desktop".to_string()),
            backup_type: "export_file".to_string(),
            description: Some("Vollständiges Einstellungs-Backup".to_string()),
            settings: self.settings.clone(),
            favorites: Some(self.favorites.items.clone()),
            user_commands: Some(self.user_commands.commands.clone()),
        };
        json!({ "ok": true, "package": pkg })
    }

    fn rpc_import_settings_package(&mut self, params: &Value) -> Value {
        let pkg_val = match params.get("package") {
            Some(v) => v,
            None => return json!({ "error": "package parameter fehlt" }),
        };

        if let Ok(pkg) = serde_json::from_value::<SettingsBackupPackage>(pkg_val.clone()) {
            let prev_x = self.settings.main_window_x;
            let prev_y = self.settings.main_window_y;
            let prev_max = self.settings.main_window_maximized;
            self.settings = pkg.settings;
            self.settings.main_window_x = prev_x;
            self.settings.main_window_y = prev_y;
            self.settings.main_window_maximized = prev_max;
            self.settings.first_run_completed = true;
            SettingsStore::save(&self.settings);
            if let Some(favs) = pkg.favorites {
                self.favorites.items = favs;
                self.favorites.save();
            }
            if let Some(cmds) = pkg.user_commands {
                self.user_commands.commands = cmds;
                self.user_commands.save();
            }
            return json!({ "ok": true, "settings": self.settings, "favorites": self.favorites.items });
        }

        if let Ok(mut settings) = serde_json::from_value::<AppSettings>(pkg_val.clone()) {
            settings.main_window_x = self.settings.main_window_x;
            settings.main_window_y = self.settings.main_window_y;
            settings.main_window_maximized = self.settings.main_window_maximized;
            settings.first_run_completed = true;
            self.settings = settings;
            SettingsStore::save(&self.settings);
            return json!({ "ok": true, "settings": self.settings });
        }

        json!({ "error": "Ungültiges Einstellungs-Format" })
    }

    fn rpc_launch_external_tool(&self, params: &Value) -> Value {
        let tool_cmd = params.get("tool").and_then(|v| v.as_str()).unwrap_or("").trim();
        let args: Vec<String> = params
            .get("args")
            .and_then(|v| v.as_array())
            .map(|arr| {
                arr.iter()
                    .filter_map(|v| v.as_str().map(String::from))
                    .collect()
            })
            .unwrap_or_default();
        if tool_cmd.is_empty() {
            return json!({ "error": "Kein externes Programm konfiguriert." });
        }
        match std::process::Command::new(tool_cmd).args(&args).spawn() {
            Ok(_) => json!({ "ok": true }),
            Err(e) => json!({ "error": format!("Fehler beim Starten von '{}': {}", tool_cmd, e) }),
        }
    }

    fn rpc_calc_folder_size(&self, params: &Value) -> Value {
        let path_str = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        let path = Path::new(path_str);
        if !path.is_dir() {
            return json!({ "error": "Not a directory" });
        }
        let node = StorageTreemapService::scan_directory_tree(path, 5);
        json!({
            "ok": true,
            "path": path_str,
            "size_bytes": node.size_bytes,
            "size_text": format_bytes(node.size_bytes),
        })
    }

    fn rpc_autocomplete_path(&self, params: &Value) -> Value {
        let query = params.get("query").and_then(|v| v.as_str()).unwrap_or("").trim();
        if query.is_empty() {
            return json!({ "suggestions": [] });
        }
        let p = Path::new(query);
        let (search_dir, prefix) = if query.ends_with('\\') || query.ends_with('/') {
            (p.to_path_buf(), String::new())
        } else if let Some(parent) = p.parent() {
            let pref = p
                .file_name()
                .map(|n| n.to_string_lossy().to_lowercase())
                .unwrap_or_default();
            (parent.to_path_buf(), pref)
        } else {
            return json!({ "suggestions": [] });
        };

        let mut suggestions = Vec::new();
        if let Ok(rd) = fs::read_dir(&search_dir) {
            for entry in rd.flatten() {
                if let Ok(ft) = entry.file_type() {
                    if ft.is_dir() {
                        let name = entry.file_name().to_string_lossy().to_string();
                        if !self.settings.show_hidden_files && name.starts_with('.') {
                            continue;
                        }
                        if prefix.is_empty() || name.to_lowercase().starts_with(&prefix) {
                            suggestions.push(entry.path().to_string_lossy().to_string());
                            if suggestions.len() >= 15 {
                                break;
                            }
                        }
                    }
                }
            }
        }
        json!({ "suggestions": suggestions })
    }

    fn rpc_open_terminal(&self, params: &Value) -> Value {
        let dir = params.get("dir").and_then(|v| v.as_str()).unwrap_or(".");
        let custom_cmd = self.settings.custom_terminal_command.trim();
        if !custom_cmd.is_empty() {
            let expanded = custom_cmd.replace("%P", dir);
            #[cfg(target_os = "windows")]
            {
                let _ = modern_explorer_core::features::hidden_command("cmd")
                    .args(["/C", "start", "", &expanded])
                    .current_dir(dir)
                    .spawn();
            }
            #[cfg(not(target_os = "windows"))]
            {
                let _ = std::process::Command::new("sh")
                    .args(["-c", &expanded])
                    .current_dir(dir)
                    .spawn();
            }
            return json!({ "ok": true });
        }
        #[cfg(target_os = "windows")]
        {
            // Explicitly open an external interactive PowerShell window via cmd /C start
            let _ = modern_explorer_core::features::hidden_command("cmd")
                .args(["/C", "start", "powershell", "-NoExit", "-Command", &format!("Set-Location -LiteralPath '{}'", dir.replace('\'', "''"))])
                .current_dir(dir)
                .spawn();
        }
        #[cfg(target_os = "macos")]
        {
            let _ = std::process::Command::new("open")
                .args(["-a", "Terminal", dir])
                .spawn();
        }
        #[cfg(target_os = "linux")]
        {
            let terminals = ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "xterm"];
            for term in terminals {
                if std::process::Command::new(term).current_dir(dir).spawn().is_ok() {
                    break;
                }
            }
        }
        json!({ "ok": true })
    }

    fn rpc_quick_preview(&self, params: &Value) -> Value {
        let path_str = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        let mut temp_extracted: Option<PathBuf> = None;
        let path = if ArchiveService::is_virtual_archive_path(path_str) {
            if let Some((arch_file, inner_path)) = ArchiveService::parse_virtual_archive_path(path_str) {
                if let Ok(extracted) = ArchiveService::extract_single_entry_to_temp(&arch_file, &inner_path) {
                    temp_extracted = Some(extracted);
                }
            }
            temp_extracted.as_deref().unwrap_or_else(|| Path::new(path_str))
        } else {
            Path::new(path_str)
        };

        if !path.is_file() {
            return json!({
                "is_file": false,
                "text": "Ordner oder kein Dateielement ausgewählt.",
            });
        }
        let max_bytes = (self.settings.quick_viewer_max_kb as usize).clamp(16, 16384) * 1024;
        let text = FileContentService::read_text_preview(path, max_bytes).unwrap_or_default();
        let hex = FileContentService::format_hex_dump(path, 4096).unwrap_or_default();
        let csv_rows = FileContentService::parse_csv_rows(&text, 100);
        let media = MetadataAndAttributeService::inspect_media_metadata(path);
        let hashes = ChecksumBatchService::compute_checksums(&[path.to_string_lossy().to_string()]);
        json!({
            "is_file": true,
            "path": path_str,
            "text": text,
            "hex": hex,
            "csv_rows": csv_rows,
            "media_format": media.format_name,
            "resolution": media.resolution_text,
            "checksum": hashes.first(),
        })
    }

    fn rpc_save_file_text(&self, params: &Value) -> Value {
        let path = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        let content = params.get("content").and_then(|v| v.as_str()).unwrap_or("");
        match FileContentService::save_text(Path::new(path), content) {
            Ok(()) => json!({ "ok": true }),
            Err(e) => json!({ "error": e }),
        }
    }

    fn parse_batch_options(params: &Value) -> (Vec<FileEntry>, BatchRenameOptions) {
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();
        let entries: Vec<FileEntry> = paths
            .iter()
            .filter_map(|p| FileEntry::try_from_path(p))
            .collect();

        let case_str = params
            .get("case_transform")
            .and_then(|v| v.as_str())
            .unwrap_or("Unchanged");
        let case_transform = match case_str {
            "Lowercase" => CaseTransform::Lowercase,
            "Uppercase" => CaseTransform::Uppercase,
            "TitleCase" => CaseTransform::TitleCase,
            _ => CaseTransform::Unchanged,
        };
        let opts = BatchRenameOptions {
            name_pattern: params
                .get("name_pattern")
                .and_then(|v| v.as_str())
                .unwrap_or("[N]")
                .to_string(),
            extension_pattern: params
                .get("extension_pattern")
                .and_then(|v| v.as_str())
                .unwrap_or("[E]")
                .to_string(),
            search_for: params
                .get("search_for")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string(),
            replace_with: params
                .get("replace_with")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string(),
            use_regex: params
                .get("use_regex")
                .and_then(|v| v.as_bool())
                .unwrap_or(false),
            case_sensitive: params
                .get("case_sensitive")
                .and_then(|v| v.as_bool())
                .unwrap_or(false),
            case_transform,
            counter_start: params
                .get("counter_start")
                .and_then(|v| v.as_u64())
                .unwrap_or(1) as usize,
            counter_step: params
                .get("counter_step")
                .and_then(|v| v.as_u64())
                .unwrap_or(1) as usize,
            counter_digits: params
                .get("counter_digits")
                .and_then(|v| v.as_u64())
                .unwrap_or(3) as usize,
        };
        (entries, opts)
    }

    fn rpc_batch_rename_preview(&self, params: &Value) -> Value {
        let (entries, opts) = Self::parse_batch_options(params);
        let previews: Vec<Value> = BatchRenameService::generate_preview(&entries, &opts)
            .into_iter()
            .map(|p| {
                json!({
                    "original_path": p.original_path,
                    "original_name": p.original_name,
                    "new_name": p.new_name,
                    "has_conflict": p.has_conflict,
                    "error_message": p.error_message,
                })
            })
            .collect();
        json!({ "previews": previews })
    }

    fn rpc_batch_rename_execute(&mut self, params: &Value) -> Value {
        let (entries, opts) = Self::parse_batch_options(params);
        let previews = BatchRenameService::generate_preview(&entries, &opts);
        match BatchRenameService::execute_rename(&previews) {
            Ok(pairs) => {
                let count = pairs.len();
                self.undo_service
                    .record("Mehrfach-Umbenennung", UndoAction::BatchRename { pairs });
                json!({
                    "ok": true,
                    "count": count,
                    "can_undo": self.undo_service.can_undo(),
                    "undo_desc": self.undo_service.last_description().unwrap_or(""),
                })
            }
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_file_diff(&self, params: &Value) -> Value {
        let left_path = params
            .get("left_path")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .trim();
        let right_path = params
            .get("right_path")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .trim();
        let ignore_ws = params
            .get("ignore_whitespace")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);

        let has_explicit_text = params.get("left_text").is_some() || params.get("right_text").is_some();

        if !has_explicit_text && (left_path.is_empty() || right_path.is_empty()) {
            return json!({
                "error": "Bitte wählen Sie sowohl die linke als auch die rechte Datei für den Vergleich aus."
            });
        }

        let left_bytes = if has_explicit_text {
            None
        } else {
            match fs::read(left_path) {
                Ok(b) => Some(b),
                Err(e) => {
                    return json!({ "error": format!("Fehler beim Lesen der linken Datei ({}): {}", left_path, e) });
                }
            }
        };

        let right_bytes = if has_explicit_text {
            None
        } else {
            match fs::read(right_path) {
                Ok(b) => Some(b),
                Err(e) => {
                    return json!({ "error": format!("Fehler beim Lesen der rechten Datei ({}): {}", right_path, e) });
                }
            }
        };

        let mut left_text = if let Some(lt) = params.get("left_text").and_then(|v| v.as_str()) {
            lt.to_string()
        } else if let Some(ref lb) = left_bytes {
            FileDiffService::convert_bytes_for_diff(Path::new(left_path), lb).unwrap_or_default()
        } else {
            String::new()
        };

        let mut right_text = if let Some(rt) = params.get("right_text").and_then(|v| v.as_str()) {
            rt.to_string()
        } else if let Some(ref rb) = right_bytes {
            FileDiffService::convert_bytes_for_diff(Path::new(right_path), rb).unwrap_or_default()
        } else {
            String::new()
        };

        if let (Some(ref lb), Some(ref rb)) = (&left_bytes, &right_bytes) {
            if left_text == right_text && lb != rb {
                let left_hash = FileDiffService::compute_sha256_hex(lb);
                let right_hash = FileDiffService::compute_sha256_hex(rb);
                left_text.push_str(&format!(
                    "\n\n=== [Binäre Datei-Eigenschaften (Extrahierter Text ist identisch)] ===\nDateigröße: {} Bytes\nSHA-256: {}\n",
                    lb.len(),
                    left_hash
                ));
                right_text.push_str(&format!(
                    "\n\n=== [Binäre Datei-Eigenschaften (Extrahierter Text ist identisch)] ===\nDateigröße: {} Bytes\nSHA-256: {}\n",
                    rb.len(),
                    right_hash
                ));
            }
        }

        let diff = FileDiffService::compare_text(&left_text, &right_text, ignore_ws);
        let rows: Vec<Value> = diff
            .rows
            .iter()
            .map(|r| {
                json!({
                    "row_index": r.row_index,
                    "left_line_number": r.left_line_number,
                    "right_line_number": r.right_line_number,
                    "left_text": r.left_text,
                    "right_text": r.right_text,
                    "kind": format!("{:?}", r.kind),
                    "hunk_id": r.hunk_id,
                })
            })
            .collect();

        json!({
            "hunk_count": diff.hunk_count,
            "added_count": diff.added_count,
            "removed_count": diff.removed_count,
            "modified_count": diff.modified_count,
            "rows": rows,
        })
    }

    fn rpc_checksums(&self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("compute");
        if sub == "verify" {
            let manifest_path = Path::new(params.get("manifest_path").and_then(|v| v.as_str()).unwrap_or(""));
            match ChecksumBatchService::verify_manifest(manifest_path) {
                Ok(items) => {
                    let rows: Vec<Value> = items
                        .into_iter()
                        .map(|i| {
                            json!({
                                "file_name": i.file_name,
                                "expected_hash": i.expected_hash,
                                "actual_hash": i.actual_hash,
                                "is_match": i.is_match,
                                "file_exists": i.file_exists,
                            })
                        })
                        .collect();
                    return json!({ "ok": true, "verified_items": rows });
                }
                Err(e) => return json!({ "error": e }),
            }
        }
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();
        let entries = ChecksumBatchService::compute_checksums(&paths);
        let sha256_manifest =
            ChecksumBatchService::format_manifest(&entries, ChecksumManifestFormat::Sha256);
        let sha512_manifest =
            ChecksumBatchService::format_manifest(&entries, ChecksumManifestFormat::Sha512);
        let md5_manifest =
            ChecksumBatchService::format_manifest(&entries, ChecksumManifestFormat::Md5);
        let sfv_manifest =
            ChecksumBatchService::format_manifest(&entries, ChecksumManifestFormat::Sfv);
        json!({
            "entries": entries,
            "sha256_manifest": sha256_manifest,
            "sha512_manifest": sha512_manifest,
            "md5_manifest": md5_manifest,
            "sfv_manifest": sfv_manifest,
        })
    }

    fn rpc_split_or_join_or_shred(&self, params: &Value) -> Value {
        let sub = params.get("mode").and_then(|v| v.as_str()).unwrap_or("split");
        let path_str = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        let path = Path::new(path_str);

        match sub {
            "split" => {
                let part_mb = params
                    .get("part_size_mb")
                    .and_then(|v| v.as_u64())
                    .unwrap_or(100)
                    .max(1);
                let out_dir = path.parent().unwrap_or_else(|| Path::new("."));
                match FileSplitAndShredService::split_file(path, out_dir, part_mb * 1024 * 1024) {
                    Ok(parts) => json!({
                        "ok": true,
                        "message": format!("Datei erfolgreich in {} Teile (.001..) + .sha256 Manifest aufgeteilt.", parts.len())
                    }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "join" => {
                let dst = path.with_extension("joined");
                match FileSplitAndShredService::join_parts(path, &dst) {
                    Ok(bytes) => json!({
                        "ok": true,
                        "message": format!("Teile erfolgreich zusammengefügt ({}) -> {}", format_bytes(bytes), dst.display())
                    }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "shred" => {
                let passes = params.get("passes").and_then(|v| v.as_u64()).unwrap_or(3);
                let mode = match passes {
                    1 => ShredMode::OnePassRandom,
                    7 => ShredMode::SevenPass,
                    _ => ShredMode::ThreePassDoD,
                };
                match FileSplitAndShredService::shred_file(path, mode) {
                    Ok(()) => json!({
                        "ok": true,
                        "message": "Datei kryptografisch überschrieben und unwiderruflich vernichtet."
                    }),
                    Err(e) => json!({ "error": e }),
                }
            }
            _ => json!({ "error": "Unknown split/shred mode" }),
        }
    }

    fn rpc_set_tag(&mut self, params: &Value) -> Value {
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();
        let color_str = params.get("color").and_then(|v| v.as_str()).unwrap_or("None");
        let note = params.get("note").and_then(|v| v.as_str()).unwrap_or("");
        let color = match color_str {
            "Red" => FileTagColor::Red,
            "Orange" => FileTagColor::Orange,
            "Yellow" => FileTagColor::Yellow,
            "Green" => FileTagColor::Green,
            "Blue" => FileTagColor::Blue,
            "Purple" => FileTagColor::Purple,
            _ => FileTagColor::None,
        };
        for p in paths {
            self.tag_service.set_tag_and_note(&p, color, note);
        }
        json!({ "ok": true })
    }

    fn rpc_workspaces(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("list");
        if sub == "save" {
            if let Ok(profile) =
                serde_json::from_value::<WorkspaceProfile>(params.get("profile").cloned().unwrap_or_default())
            {
                self.workspace_service.upsert_profile(profile);
            }
        } else if sub == "delete" {
            if let Some(name) = params.get("name").and_then(|v| v.as_str()) {
                self.workspace_service.delete_profile(name);
            }
        }
        json!({
            "ok": true,
            "workspaces": self.workspace_service.profiles,
        })
    }

    fn rpc_smart_export(&self, params: &Value) -> Value {
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();
        let entries: Vec<FileEntry> = paths
            .iter()
            .filter_map(|p| FileEntry::try_from_path(p))
            .collect();
        let base_dir = params.get("base_dir").and_then(|v| v.as_str()).map(Path::new);
        let fmt_str = params
            .get("format")
            .and_then(|v| v.as_str())
            .unwrap_or("FullPaths");
        let fmt = match fmt_str {
            "FileNamesOnly" => SmartExportFormat::FileNamesOnly,
            "RelativePaths" => SmartExportFormat::RelativePaths,
            "ForwardSlashPaths" => SmartExportFormat::ForwardSlashPaths,
            "MarkdownFileLinks" => SmartExportFormat::MarkdownFileLinks,
            "CsvTable" => SmartExportFormat::CsvTable,
            "MarkdownTable" => SmartExportFormat::MarkdownTable,
            "JsonArray" => SmartExportFormat::JsonArray,
            "HtmlTable" => SmartExportFormat::HtmlTable,
            _ => SmartExportFormat::FullPaths,
        };
        let output = SmartExportService::format_entries(&entries, fmt, base_dir);
        json!({ "output": output })
    }

    fn rpc_basket(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("list");
        match sub {
            "add" => {
                let paths: Vec<String> = params
                    .get("paths")
                    .and_then(|v| v.as_array())
                    .map(|a| {
                        a.iter()
                            .filter_map(|x| x.as_str().map(|s| s.to_string()))
                            .collect()
                    })
                    .unwrap_or_default();
                self.basket_service.add_paths(&paths);
            }
            "remove" => {
                if let Some(p) = params.get("path").and_then(|v| v.as_str()) {
                    self.basket_service.remove_by_path(p);
                }
            }
            "clear" => self.basket_service.clear(),
            "copy_here" => {
                let dest = Path::new(params.get("dest_dir").and_then(|v| v.as_str()).unwrap_or("."));
                for item in self.basket_service.items() {
                    let _ = FileOperationExecutor::copy_entry(
                        Path::new(&item.full_path),
                        dest,
                        ConflictResolution::KeepBoth,
                    );
                }
            }
            "move_here" => {
                let dest = Path::new(params.get("dest_dir").and_then(|v| v.as_str()).unwrap_or("."));
                for item in self.basket_service.items() {
                    let _ = FileOperationExecutor::move_entry(
                        Path::new(&item.full_path),
                        dest,
                        ConflictResolution::KeepBoth,
                    );
                }
                self.basket_service.clear();
            }
            _ => {}
        }
        json!({
            "items": self.basket_service.items(),
            "count": self.basket_service.count(),
            "summary": self.basket_service.summary_label(),
        })
    }

    fn rpc_attr_exif(&self, params: &Value) -> Value {
        let sub = params
            .get("sub")
            .and_then(|v| v.as_str())
            .unwrap_or("inspect");
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();

        if sub == "apply" {
            let shift_hours = params
                .get("shift_hours")
                .and_then(|v| v.as_i64())
                .unwrap_or(0);
            let touch = params
                .get("touch_to_now")
                .and_then(|v| v.as_bool())
                .unwrap_or(false);
            let set_ro = params.get("set_readonly").and_then(|v| v.as_bool());
            let _ = MetadataAndAttributeService::apply_batch_changes(
                &paths,
                &BatchAttributeTimestampOptions {
                    shift_timestamps: shift_hours != 0,
                    shift_hours,
                    touch_to_now: touch,
                    set_readonly: set_ro,
                },
            );
        }

        let details: Vec<Value> = paths
            .iter()
            .filter_map(|p| {
                let fe = FileEntry::try_from_path(p)?;
                let m = MetadataAndAttributeService::inspect_media_metadata(p);
                Some(json!({
                    "name": fe.name,
                    "full_path": fe.full_path,
                    "modified": fe.modified.format("%Y-%m-%d %H:%M:%S").to_string(),
                    "created": fe.created.format("%Y-%m-%d %H:%M:%S").to_string(),
                    "is_readonly": fe.is_readonly,
                    "is_hidden": fe.is_hidden,
                    "format_name": m.format_name,
                    "resolution": m.resolution_text,
                }))
            })
            .collect();
        json!({ "ok": true, "details": details })
    }

    fn rpc_quick_console(&mut self, params: &Value) -> Value {
        let cmd = params.get("cmd").and_then(|v| v.as_str()).unwrap_or("");
        let cwd = params.get("cwd").and_then(|v| v.as_str()).unwrap_or(".");
        let res = self.quick_runner.execute(cmd, Path::new(cwd));
        json!({
            "command_line": res.command_line,
            "exit_code": res.exit_code,
            "output_text": res.output_text,
            "navigate_to_directory": res.navigate_to_directory,
            "history": self.quick_runner.history,
        })
    }

    fn rpc_git_panel(&self, params: &Value) -> Value {
        let dir = Path::new(params.get("dir").and_then(|v| v.as_str()).unwrap_or("."));
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("status");

        if sub == "checkout" {
            if let Some(b) = params.get("branch").and_then(|v| v.as_str()) {
                let _ = GitIntegrationService::checkout_branch(dir, b);
            }
        } else if sub == "stage" {
            if let Some(rel) = params.get("relative_path").and_then(|v| v.as_str()) {
                let _ = GitIntegrationService::stage_file(dir, rel);
            }
        } else if sub == "unstage" {
            if let Some(rel) = params.get("relative_path").and_then(|v| v.as_str()) {
                let _ = GitIntegrationService::unstage_file(dir, rel);
            }
        } else if sub == "commit" {
            if let Some(msg) = params.get("message").and_then(|v| v.as_str()) {
                let _ = GitIntegrationService::commit(dir, msg);
            }
        } else if sub == "diff_head" {
            if let Some(rel) = params.get("relative_path").and_then(|v| v.as_str()) {
                let snap = GitIntegrationService::inspect_repository(dir);
                if snap.is_repo {
                    let head = GitIntegrationService::get_head_file_content(
                        Path::new(&snap.repo_root),
                        rel,
                    )
                    .unwrap_or_default();
                    let full_p = Path::new(&snap.repo_root).join(rel);
                    let work = fs::read_to_string(&full_p).unwrap_or_default();
                    return self.rpc_file_diff(&json!({
                        "left_text": head,
                        "right_text": work,
                    }));
                }
            }
        }

        let snap = GitIntegrationService::inspect_repository(dir);
        let changed: Vec<Value> = snap
            .changed_files
            .iter()
            .map(|f| {
                json!({
                    "status_code": f.status_code,
                    "relative_path": f.relative_path,
                    "full_path": f.full_path,
                })
            })
            .collect();
        let commits: Vec<Value> = snap
            .recent_commits
            .iter()
            .map(|c| {
                json!({
                    "short_hash": c.short_hash,
                    "author": c.author,
                    "date": c.date,
                    "subject": c.subject,
                })
            })
            .collect();

        json!({
            "is_repo": snap.is_repo,
            "repo_root": snap.repo_root,
            "current_branch": snap.current_branch,
            "branches": snap.branches,
            "changed_files": changed,
            "recent_commits": commits,
        })
    }

    fn rpc_dir_sync(&self, params: &Value) -> Value {
        let left = Path::new(params.get("left_dir").and_then(|v| v.as_str()).unwrap_or("."));
        let right = Path::new(params.get("right_dir").and_then(|v| v.as_str()).unwrap_or("."));
        let comp_mode = match params.get("comp_mode").and_then(|v| v.as_str()).unwrap_or("SizeAndTimestamp") {
            "SizeOnly" => SyncComparisonMode::SizeOnly,
            "Sha256Content" => SyncComparisonMode::Sha256Content,
            _ => SyncComparisonMode::SizeAndTimestamp,
        };
        let dir_mode = match params.get("dir_mode").and_then(|v| v.as_str()).unwrap_or("Bidirectional") {
            "LeftToRight" => SyncDirectionMode::LeftToRight,
            "RightToLeft" => SyncDirectionMode::RightToLeft,
            _ => SyncDirectionMode::Bidirectional,
        };
        let delete_orphans = params
            .get("delete_orphans")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let execute = params
            .get("execute")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);

        let items = DirectorySyncService::compare_directories(
            left,
            right,
            comp_mode,
            dir_mode,
            delete_orphans,
        );
        let mut executed_count = 0usize;
        if execute {
            executed_count = DirectorySyncService::execute_sync(left, right, &items).unwrap_or(0);
        }
        let rows: Vec<Value> = items
            .into_iter()
            .filter(|i| i.action != modern_explorer_core::operations::SyncAction::Equal)
            .map(|i| {
                json!({
                    "relative_path": i.relative_path,
                    "left_size": i.left_size.map(format_bytes).unwrap_or_else(|| "-".to_string()),
                    "right_size": i.right_size.map(format_bytes).unwrap_or_else(|| "-".to_string()),
                    "action": format!("{:?}", i.action),
                })
            })
            .collect();
        json!({
            "ok": true,
            "executed_count": executed_count,
            "items": rows,
        })
    }

    fn rpc_deep_search(&self, params: &Value) -> Value {
        let criteria = DeepSearchCriteria {
            root_path: params
                .get("root_path")
                .and_then(|v| v.as_str())
                .unwrap_or(".")
                .to_string(),
            name_pattern: params
                .get("name_pattern")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string(),
            use_regex: params
                .get("use_regex")
                .and_then(|v| v.as_bool())
                .unwrap_or(false),
            content_query: params
                .get("content_query")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string(),
            case_sensitive_content: false,
            min_size_bytes: None,
            max_size_bytes: None,
            include_hidden: self.settings.show_hidden_files,
            max_results: 500,
        };
        let hits: Vec<Value> = DeepSearchService::search(&criteria)
            .into_iter()
            .map(|e| {
                json!({
                    "name": e.name,
                    "full_path": e.full_path,
                    "parent_dir": Path::new(&e.full_path).parent().map(|p| p.to_string_lossy().to_string()).unwrap_or_default(),
                    "size_text": e.display_size(),
                    "modified_text": e.modified.format("%d.%m.%Y %H:%M").to_string(),
                })
            })
            .collect();
        json!({ "results": hits })
    }

    fn format_duplicate_groups_result(res: &DuplicateScanResult) -> Value {
        let groups: Vec<Value> = res
            .groups
            .iter()
            .map(|g| {
                json!({
                    "sha256": g.sha256_hash,
                    "file_size_bytes": g.file_size_bytes,
                    "size_text": format_bytes(g.file_size_bytes),
                    "wasted_bytes": g.wasted_bytes(),
                    "wasted_text": format_bytes(g.wasted_bytes()),
                    "files": g.files.iter().map(|f| &f.full_path).collect::<Vec<_>>(),
                    "file_details": g.files.iter().map(|f| {
                        json!({
                            "name": f.name,
                            "full_path": f.full_path,
                            "modified_text": f.modified.format("%d.%m.%Y %H:%M").to_string(),
                            "size_bytes": f.size_bytes,
                            "size_text": format_bytes(f.size_bytes),
                        })
                    }).collect::<Vec<_>>(),
                })
            })
            .collect();
        json!({
            "total_files_scanned": res.total_files_scanned,
            "total_wasted_bytes": res.total_wasted_bytes,
            "total_wasted_text": format_bytes(res.total_wasted_bytes),
            "groups": groups,
        })
    }

    fn rpc_duplicates(&self, params: &Value) -> Value {
        let dir = params.get("dir").and_then(|v| v.as_str()).unwrap_or(".");
        let min_kb = params
            .get("min_size_kb")
            .and_then(|v| v.as_u64())
            .unwrap_or(1);
        let res = DuplicateFinderService::scan_directory(dir, min_kb * 1024);
        Self::format_duplicate_groups_result(&res)
    }

    fn rpc_duplicates_start(&self, params: &Value) -> Value {
        let dir = params
            .get("dir")
            .and_then(|v| v.as_str())
            .unwrap_or(".")
            .to_string();
        let min_kb = params
            .get("min_size_kb")
            .and_then(|v| v.as_u64())
            .unwrap_or(1);
        let min_bytes = min_kb * 1024;

        if let Ok(mut lock) = self.duplicates_job.lock() {
            if let Some(ref current) = *lock {
                if current.running {
                    current.cancel_flag.store(true, Ordering::SeqCst);
                }
            }
            let cancel_flag = Arc::new(AtomicBool::new(false));
            *lock = Some(DuplicatesJobState {
                running: true,
                progress: DuplicateScanProgress {
                    stage: 1,
                    stage_message: "Starte Duplikatanalyse…".to_string(),
                    percentage: 0.0,
                    processed_files: 0,
                    total_files: 0,
                    is_indeterminate: true,
                },
                result: None,
                error: None,
                cancel_flag: Arc::clone(&cancel_flag),
            });

            let job_ref = Arc::clone(&self.duplicates_job);
            std::thread::spawn(move || {
                let res = DuplicateFinderService::scan_directory_with_progress(
                    &dir,
                    min_bytes,
                    |p| {
                        if let Ok(mut lk) = job_ref.lock() {
                            if let Some(ref mut j) = *lk {
                                j.progress = p.clone();
                            }
                        }
                    },
                    Some(cancel_flag),
                );
                if let Ok(mut lk) = job_ref.lock() {
                    if let Some(ref mut j) = *lk {
                        j.running = false;
                        j.result = Some(res);
                    }
                }
            });
        }
        json!({ "ok": true, "started": true })
    }

    fn rpc_duplicates_poll(&self) -> Value {
        if let Ok(lock) = self.duplicates_job.lock() {
            if let Some(ref job) = *lock {
                let mut out = json!({
                    "ok": true,
                    "running": job.running,
                    "stage": job.progress.stage,
                    "stage_message": job.progress.stage_message,
                    "percentage": job.progress.percentage,
                    "processed_files": job.progress.processed_files,
                    "total_files": job.progress.total_files,
                    "is_indeterminate": job.progress.is_indeterminate,
                    "error": job.error,
                });
                if let Some(ref res) = job.result {
                    out["result"] = Self::format_duplicate_groups_result(res);
                }
                return out;
            }
        }
        json!({ "ok": true, "running": false, "no_job": true })
    }

    fn rpc_duplicates_cancel(&self) -> Value {
        if let Ok(lock) = self.duplicates_job.lock() {
            if let Some(ref job) = *lock {
                job.cancel_flag.store(true, Ordering::SeqCst);
            }
        }
        json!({ "ok": true })
    }

    fn rpc_treemap(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("view");
        let is_de = LanguageCode::from_setting(&self.settings.language) == LanguageCode::De;

        if sub == "start_scan" {
            let dir = params
                .get("dir")
                .and_then(|v| v.as_str())
                .unwrap_or(".")
                .to_string();

            if let Ok(mut lock) = self.treemap_job.lock() {
                if let Some(ref current) = *lock {
                    if current.running {
                        current.cancel_flag.store(true, Ordering::SeqCst);
                    }
                }
                let cancel_flag = Arc::new(AtomicBool::new(false));
                *lock = Some(TreemapJobState {
                    running: true,
                    scanned_files: 0,
                    scanned_folders: 0,
                    current_path: dir.clone(),
                    root_path: dir.clone(),
                    result: None,
                    error: None,
                    cancel_flag: Arc::clone(&cancel_flag),
                });

                let job_ref = Arc::clone(&self.treemap_job);
                let dir_clone = dir.clone();
                std::thread::spawn(move || {
                    let cancel_inner = Arc::clone(&cancel_flag);
                    let tree = StorageTreemapService::scan_directory_tree_with_progress(
                        &dir_clone,
                        6,
                        cancel_inner,
                        |files, folders, cur| {
                            if let Ok(mut lk) = job_ref.lock() {
                                if let Some(ref mut j) = *lk {
                                    j.scanned_files = files;
                                    j.scanned_folders = folders;
                                    j.current_path = cur.to_string();
                                }
                            }
                        },
                    );
                    if let Ok(mut lk) = job_ref.lock() {
                        if let Some(ref mut j) = *lk {
                            j.running = false;
                            if !j.cancel_flag.load(Ordering::Relaxed) {
                                j.result = Some(tree);
                            }
                        }
                    }
                });
            }
            return json!({ "ok": true, "started": true });
        }

        if sub == "poll_progress" {
            if let Ok(lock) = self.treemap_job.lock() {
                if let Some(ref job) = *lock {
                    return json!({
                        "ok": true,
                        "running": job.running,
                        "scanned_files": job.scanned_files,
                        "scanned_folders": job.scanned_folders,
                        "current_path": job.current_path,
                        "root_path": job.root_path,
                        "done": !job.running && job.result.is_some(),
                        "cancelled": job.cancel_flag.load(Ordering::Relaxed),
                        "error": job.error,
                    });
                }
            }
            return json!({ "ok": true, "running": false, "no_job": true });
        }

        if sub == "cancel_scan" {
            if let Ok(lock) = self.treemap_job.lock() {
                if let Some(ref job) = *lock {
                    job.cancel_flag.store(true, Ordering::SeqCst);
                }
            }
            return json!({ "ok": true, "cancelled": true });
        }

        if sub == "list_tree_subdirs" {
            let target = params.get("path").and_then(|v| v.as_str()).unwrap_or(".");
            let mut subdirs = Vec::new();
            if let Ok(rd) = fs::read_dir(target) {
                for entry in rd.flatten() {
                    if let Ok(ft) = entry.file_type() {
                        if ft.is_dir() && !ft.is_symlink() {
                            let p = entry.path();
                            let name = entry.file_name().to_string_lossy().to_string();
                            let full_path = p.to_string_lossy().to_string();
                            subdirs.push((name, full_path));
                        }
                    }
                }
            }
            subdirs.sort_by(|a, b| a.0.to_ascii_lowercase().cmp(&b.0.to_ascii_lowercase()));
            subdirs.truncate(200);
            let list: Vec<Value> = subdirs
                .into_iter()
                .map(|(name, full_path)| json!({ "name": name, "full_path": full_path }))
                .collect();
            return json!({ "ok": true, "subdirs": list });
        }

        let mut deleted_count = 0usize;
        let mut freed_bytes = 0u64;
        if sub == "delete_nodes" {
            let paths: Vec<String> = params
                .get("paths")
                .and_then(|v| v.as_array())
                .map(|a| {
                    a.iter()
                        .filter_map(|x| x.as_str().map(|s| s.to_string()))
                        .collect()
                })
                .unwrap_or_default();
            let mut deleted_set = HashSet::new();
            for p in &paths {
                let size_before = self
                    .cached_treemap
                    .as_ref()
                    .and_then(|r| StorageTreemapService::find_node_by_path(r, p))
                    .map(|n| n.size_bytes)
                    .unwrap_or(0);
                if FileOperationExecutor::delete_entry(Path::new(p)).is_ok()
                    || !Path::new(p).exists()
                {
                    deleted_count += 1;
                    freed_bytes = freed_bytes.saturating_add(size_before);
                    deleted_set.insert(p.clone());
                }
            }
            if let Some(root) = &mut self.cached_treemap {
                StorageTreemapService::remove_paths_from_tree(root, &deleted_set);
            }
        }

        if let Ok(mut lock) = self.treemap_job.lock() {
            if let Some(ref mut job) = *lock {
                if let Some(res) = job.result.take() {
                    self.cached_treemap = Some(res);
                }
            }
        }

        let dir = params.get("dir").and_then(|v| v.as_str()).unwrap_or(".");
        let rescan = params
            .get("rescan")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);
        let width = params
            .get("width")
            .and_then(|v| v.as_f64())
            .unwrap_or(960.0) as f32;
        let height = params
            .get("height")
            .and_then(|v| v.as_f64())
            .unwrap_or(360.0) as f32;
        let color_mode = params
            .get("color_mode")
            .and_then(|v| v.as_str())
            .unwrap_or("Category");
        let include_folders = params
            .get("include_folders")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);

        if rescan {
            self.cached_treemap = Some(StorageTreemapService::scan_directory_tree(dir, 6));
        }

        if self.cached_treemap.is_none() {
            return json!({
                "ok": true,
                "needs_scan": true,
                "dir": dir,
            });
        }

        let scanned_root = self.cached_treemap.as_ref().unwrap();
        let zoom_req = params
            .get("zoom_path")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        let current_root = if !zoom_req.is_empty() {
            StorageTreemapService::find_node_by_path(scanned_root, zoom_req)
                .unwrap_or(scanned_root)
        } else {
            scanned_root
        };

        if sub == "export_report" {
            let md_report = StorageTreemapService::export_storage_report(current_root, true);
            let csv_report = StorageTreemapService::export_storage_report(current_root, false);
            let stamp = chrono::Local::now().format("%Y%m%d_%H%M%S").to_string();
            let target_dir = Path::new(&current_root.full_path);
            let out_dir = if target_dir.is_dir() {
                target_dir.to_path_buf()
            } else {
                std::env::temp_dir()
            };
            let mut md_path = out_dir.join(format!("StorageReport_{}.md", stamp));
            let mut csv_path = out_dir.join(format!("StorageReport_{}.csv", stamp));
            if fs::write(&md_path, &md_report).is_err() || fs::write(&csv_path, &csv_report).is_err() {
                let fallback = std::env::temp_dir();
                md_path = fallback.join(format!("StorageReport_{}.md", stamp));
                csv_path = fallback.join(format!("StorageReport_{}.csv", stamp));
                let _ = fs::write(&md_path, &md_report);
                let _ = fs::write(&csv_path, &csv_report);
            }
            return json!({
                "ok": true,
                "md_report": md_report,
                "csv_report": csv_report,
                "saved_md_path": md_path.to_string_lossy().to_string(),
                "saved_csv_path": csv_path.to_string_lossy().to_string(),
            });
        }

        let is_zoomed = !current_root
            .full_path
            .trim_end_matches(&['\\', '/'][..])
            .eq_ignore_ascii_case(scanned_root.full_path.trim_end_matches(&['\\', '/'][..]));
        let parent_path = if is_zoomed {
            StorageTreemapService::find_parent_path(scanned_root, &current_root.full_path)
        } else {
            None
        };

        let cur_total = current_root.size_bytes.max(1) as f64;
        let scan_total = scanned_root.size_bytes.max(1) as f64;

        let resolve_node_color = |node: &TreemapNode| -> String {
            if node.is_directory {
                "#475569".to_string()
            } else if color_mode == "FileAgeHeatmap" {
                StorageTreemapService::get_age_heatmap_color_hex(node.modified).to_string()
            } else {
                node.category.color_hex(&self.settings)
            }
        };

        let rects: Vec<Value> = StorageTreemapService::build_squarified_treemap_layout(
            current_root,
            width,
            height,
            2,
            420,
            4.0,
            include_folders,
        )
        .into_iter()
        .map(|r| {
            let color_hex = resolve_node_color(&r.node);
            let pct_view = ((r.node.size_bytes as f64) * 100.0 / cur_total).clamp(0.0, 100.0);
            let pct_root = ((r.node.size_bytes as f64) * 100.0 / scan_total).clamp(0.0, 100.0);
            json!({
                "x": r.x,
                "y": r.y,
                "width": r.width,
                "height": r.height,
                "depth": r.depth,
                "is_group_container": r.is_group_container,
                "name": r.node.name,
                "full_path": r.node.full_path,
                "relative_path": r.node.relative_path,
                "extension": if r.node.extension.is_empty() {
                    if is_de { "<keine Endung>".to_string() } else { "<no extension>".to_string() }
                } else {
                    r.node.extension.clone()
                },
                "is_directory": r.node.is_directory,
                "size_bytes": r.node.size_bytes,
                "size_text": format_bytes(r.node.size_bytes),
                "file_count": r.node.file_count,
                "folder_count": r.node.folder_count,
                "percentage_view": pct_view,
                "percentage_root": pct_root,
                "category": format!("{:?}", r.node.category),
                "category_label": r.node.category.display_name(is_de),
                "category_short": r.node.category.short_name(is_de),
                "color_hex": color_hex,
                "modified_text": r.node.modified.format("%Y-%m-%d %H:%M:%S").to_string(),
            })
        })
        .collect();

        let top_files: Vec<Value> = StorageTreemapService::get_top_largest_files(current_root, 100)
            .into_iter()
            .map(|f| {
                let pct_view = ((f.size_bytes as f64) * 100.0 / cur_total).clamp(0.0, 100.0);
                let pct_root = ((f.size_bytes as f64) * 100.0 / scan_total).clamp(0.0, 100.0);
                let color_hex = resolve_node_color(&f);
                json!({
                    "name": f.name,
                    "full_path": f.full_path,
                    "relative_path": f.relative_path,
                    "extension": if f.extension.is_empty() {
                        if is_de { "<keine Endung>".to_string() } else { "<no extension>".to_string() }
                    } else {
                        f.extension.clone()
                    },
                    "is_directory": false,
                    "size_bytes": f.size_bytes,
                    "size_text": format_bytes(f.size_bytes),
                    "percentage": pct_view,
                    "percentage_root": pct_root,
                    "file_count": 1,
                    "folder_count": 0,
                    "category": format!("{:?}", f.category),
                    "category_label": f.category.display_name(is_de),
                    "category_short": f.category.short_name(is_de),
                    "color_hex": color_hex,
                    "modified_text": f.modified.format("%Y-%m-%d %H:%M:%S").to_string(),
                })
            })
            .collect();

        let children: Vec<Value> = current_root
            .children
            .iter()
            .take(250)
            .map(|c| {
                let pct_view = ((c.size_bytes as f64) * 100.0 / cur_total).clamp(0.0, 100.0);
                let pct_root = ((c.size_bytes as f64) * 100.0 / scan_total).clamp(0.0, 100.0);
                let color_hex = resolve_node_color(c);
                json!({
                    "name": c.name,
                    "full_path": c.full_path,
                    "relative_path": c.relative_path,
                    "extension": if c.extension.is_empty() {
                        if is_de { "<keine Endung>".to_string() } else { "<no extension>".to_string() }
                    } else {
                        c.extension.clone()
                    },
                    "is_directory": c.is_directory,
                    "size_bytes": c.size_bytes,
                    "size_text": format_bytes(c.size_bytes),
                    "percentage": pct_view,
                    "percentage_root": pct_root,
                    "file_count": c.file_count,
                    "folder_count": c.folder_count,
                    "category": format!("{:?}", c.category),
                    "category_label": c.category.display_name(is_de),
                    "category_short": c.category.short_name(is_de),
                    "color_hex": color_hex,
                    "modified_text": c.modified.format("%Y-%m-%d %H:%M:%S").to_string(),
                })
            })
            .collect();

        let ext_stats =
            StorageTreemapService::compute_extension_stats(current_root, &self.settings, is_de, 60);

        let legend_categories: Vec<Value> = TreemapFileCategory::all_categories()
            .iter()
            .map(|&cat| {
                json!({
                    "category": format!("{:?}", cat),
                    "label": cat.display_name(is_de),
                    "short_label": cat.short_name(is_de),
                    "color_hex": cat.color_hex(&self.settings),
                })
            })
            .collect();

        json!({
            "scanned_root_path": scanned_root.full_path,
            "scanned_total_bytes": scanned_root.size_bytes,
            "scanned_total_size_text": format_bytes(scanned_root.size_bytes),
            "root_path": current_root.full_path,
            "root_name": current_root.name,
            "parent_path": parent_path,
            "is_zoomed": is_zoomed,
            "total_bytes": current_root.size_bytes,
            "total_size_text": format_bytes(current_root.size_bytes),
            "file_count": current_root.file_count,
            "folder_count": current_root.folder_count,
            "children": children,
            "top_files": top_files,
            "ext_stats": ext_stats,
            "rects": rects,
            "legend_categories": legend_categories,
            "deleted_count": deleted_count,
            "freed_bytes": freed_bytes,
            "freed_text": format_bytes(freed_bytes),
            "include_folders": include_folders,
            "md_report": StorageTreemapService::export_storage_report(current_root, true),
            "csv_report": StorageTreemapService::export_storage_report(current_root, false),
        })
    }

    fn rpc_archive(&self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("pack");
        if sub == "list" {
            let arch = Path::new(params.get("archive_path").and_then(|v| v.as_str()).unwrap_or(""));
            return match ArchiveService::list_entries(arch) {
                Ok(entries) => {
                    let rows: Vec<Value> = entries
                        .into_iter()
                        .map(|e| {
                            json!({
                                "full_name": e.full_name,
                                "name": e.name,
                                "size_bytes": e.size_bytes,
                                "size_text": format_bytes(e.size_bytes),
                                "compressed_bytes": e.compressed_bytes,
                                "compressed_text": format_bytes(e.compressed_bytes),
                                "is_directory": e.is_directory,
                            })
                        })
                        .collect();
                    json!({ "ok": true, "entries": rows })
                }
                Err(e) => json!({ "error": e }),
            };
        }
        if sub == "extract" {
            let arch = Path::new(params.get("archive_path").and_then(|v| v.as_str()).unwrap_or(""));
            let dest = Path::new(params.get("dest_dir").and_then(|v| v.as_str()).unwrap_or("."));
            return match ArchiveService::extract_all(arch, dest) {
                Ok(n) => json!({ "ok": true, "count": n }),
                Err(e) => json!({ "error": e }),
            };
        }
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| {
                a.iter()
                    .filter_map(|x| x.as_str().map(|s| s.to_string()))
                    .collect()
            })
            .unwrap_or_default();
        let dest = Path::new(params.get("dest_archive").and_then(|v| v.as_str()).unwrap_or("archive.zip"));
        let fmt_param = params.get("format").and_then(|v| v.as_str()).unwrap_or("");
        let fmt = ArchiveFormat::from_str_or_path(fmt_param, dest);
        match ArchiveService::pack_archive(&paths, dest, fmt) {
            Ok(n) => json!({ "ok": true, "count": n }),
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_remote(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("list");
        if sub == "save" {
            let proto = match params.get("protocol").and_then(|v| v.as_str()).unwrap_or("Sftp") {
                "Ftps" => RemoteProtocol::Ftps,
                "Ftp" => RemoteProtocol::Ftp,
                "WebDav" => RemoteProtocol::WebDav,
                "S3" => RemoteProtocol::S3,
                _ => RemoteProtocol::Sftp,
            };
            let prof = RemoteConnectionProfile {
                name: params.get("name").and_then(|v| v.as_str()).unwrap_or("Server").to_string(),
                protocol: proto,
                host: params.get("host").and_then(|v| v.as_str()).unwrap_or("example.com").to_string(),
                port: params.get("port").and_then(|v| v.as_u64()).unwrap_or(proto.default_port() as u64) as u16,
                username: params.get("username").and_then(|v| v.as_str()).unwrap_or("root").to_string(),
                remote_path: params.get("remote_path").and_then(|v| v.as_str()).unwrap_or("/").to_string(),
                ssh_private_key_path: String::new(),
                password_or_secret: params.get("password").and_then(|v| v.as_str()).unwrap_or("").to_string(),
                bucket: params.get("s3_bucket").and_then(|v| v.as_str()).unwrap_or("").to_string(),
                region: params.get("s3_region").and_then(|v| v.as_str()).unwrap_or("us-east-1").to_string(),
                endpoint_url: params.get("endpoint_url").and_then(|v| v.as_str()).unwrap_or("").to_string(),
                use_tls: params.get("use_tls").and_then(|v| v.as_bool()).unwrap_or(true),
            };
            self.remote_service.upsert(prof);
        } else if sub == "delete" {
            if let Some(name) = params.get("name").and_then(|v| v.as_str()) {
                self.remote_service.remove(name);
            }
        } else if sub == "browse" {
            let prof_name = params.get("name").and_then(|v| v.as_str()).unwrap_or("");
            let password = params.get("password").and_then(|v| v.as_str()).unwrap_or("");
            let path_override = params.get("remote_path").and_then(|v| v.as_str()).unwrap_or("");
            if let Some(mut prof) = self
                .remote_service
                .profiles
                .iter()
                .find(|p| p.name.eq_ignore_ascii_case(prof_name))
                .cloned()
            {
                if !password.is_empty() {
                    prof.password_or_secret = password.to_string();
                }
                let subpath = if path_override.is_empty() {
                    prof.remote_path.clone()
                } else {
                    path_override.to_string()
                };
                return match RemoteFileSystemService::list_remote_entries(&prof, &subpath) {
                    Ok(entries) => {
                        let items: Vec<Value> = entries
                            .into_iter()
                            .map(|e| {
                                json!({
                                    "name": e.name,
                                    "full_path": e.full_path,
                                    "size_bytes": e.size_bytes,
                                    "size_text": format_bytes(e.size_bytes),
                                    "is_directory": e.is_directory,
                                    "modified_text": e.modified_text,
                                })
                            })
                            .collect();
                        json!({ "ok": true, "entries": items })
                    }
                    Err(e) => json!({ "error": e }),
                };
            } else {
                return json!({ "error": "Profil nicht gefunden." });
            }
        }
        json!({
            "ok": true,
            "profiles": self.remote_service.profiles,
        })
    }

    fn rpc_selection_engine(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("evaluate");
        if sub == "save_preset" {
            if let Ok(rule) = serde_json::from_value::<SelectionRule>(params.get("rule").cloned().unwrap_or_default()) {
                self.selection_store.presets.retain(|r| !r.name.eq_ignore_ascii_case(&rule.name));
                self.selection_store.presets.push(rule);
                self.selection_store.save();
            }
            return json!({ "ok": true, "presets": self.selection_store.presets });
        }
        if sub == "delete_preset" {
            if let Some(name) = params.get("name").and_then(|v| v.as_str()) {
                self.selection_store.presets.retain(|r| !r.name.eq_ignore_ascii_case(name));
                self.selection_store.save();
            }
            return json!({ "ok": true, "presets": self.selection_store.presets });
        }
        if sub == "compare_panes" {
            let left_dir = params.get("left_dir").and_then(|v| v.as_str()).unwrap_or(".");
            let right_dir = params.get("right_dir").and_then(|v| v.as_str()).unwrap_or(".");
            let mode = params.get("mode").and_then(|v| v.as_str()).unwrap_or("missing_or_newer");
            let left_entries = DirectoryEnumerator::list_directory(Path::new(left_dir), self.settings.show_hidden_files, false);
            let right_entries = DirectoryEnumerator::list_directory(Path::new(right_dir), self.settings.show_hidden_files, false);

            let mut left_marked = Vec::new();
            let mut right_marked = Vec::new();

            for l in &left_entries {
                if l.is_directory {
                    continue;
                }
                let r_match = right_entries.iter().find(|r| r.name.eq_ignore_ascii_case(&l.name));
                match mode {
                    "missing" => {
                        if r_match.is_none() {
                            left_marked.push(l.full_path.clone());
                        }
                    }
                    "newer" => {
                        if let Some(r) = r_match {
                            if l.modified > r.modified {
                                left_marked.push(l.full_path.clone());
                            }
                        }
                    }
                    "different_size" => {
                        if let Some(r) = r_match {
                            if l.size_bytes != r.size_bytes {
                                left_marked.push(l.full_path.clone());
                            }
                        }
                    }
                    _ => {
                        if r_match.map(|r| l.modified > r.modified || l.size_bytes != r.size_bytes).unwrap_or(true) {
                            left_marked.push(l.full_path.clone());
                        }
                    }
                }
            }

            for r in &right_entries {
                if r.is_directory {
                    continue;
                }
                let l_match = left_entries.iter().find(|l| l.name.eq_ignore_ascii_case(&r.name));
                match mode {
                    "missing" => {
                        if l_match.is_none() {
                            right_marked.push(r.full_path.clone());
                        }
                    }
                    "newer" => {
                        if let Some(l) = l_match {
                            if r.modified > l.modified {
                                right_marked.push(r.full_path.clone());
                            }
                        }
                    }
                    "different_size" => {
                        if let Some(l) = l_match {
                            if r.size_bytes != l.size_bytes {
                                right_marked.push(r.full_path.clone());
                            }
                        }
                    }
                    _ => {
                        if l_match.map(|l| r.modified > l.modified || r.size_bytes != l.size_bytes).unwrap_or(true) {
                            right_marked.push(r.full_path.clone());
                        }
                    }
                }
            }

            return json!({
                "ok": true,
                "left_marked": left_marked,
                "right_marked": right_marked,
                "presets": self.selection_store.presets,
            });
        }

        // Default: evaluate rule against directory
        let dir = params.get("dir").and_then(|v| v.as_str()).unwrap_or(".");
        let rule: SelectionRule = serde_json::from_value(params.get("rule").cloned().unwrap_or_default()).unwrap_or_default();
        let entries = DirectoryEnumerator::list_directory(Path::new(dir), self.settings.show_hidden_files, false);
        let matched = SelectionPresetsStore::evaluate_entries(&entries, &rule);
        json!({
            "ok": true,
            "matched_paths": matched,
            "presets": self.selection_store.presets,
        })
    }

    fn rpc_transfer_queue(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("list");
        match sub {
            "enqueue" => {
                let is_move = params.get("is_move").and_then(|v| v.as_bool()).unwrap_or(false);
                let dest_dir = params.get("dest_dir").and_then(|v| v.as_str()).unwrap_or(".").to_string();
                let paths: Vec<String> = params
                    .get("paths")
                    .and_then(|v| v.as_array())
                    .map(|a| a.iter().filter_map(|x| x.as_str().map(|s| s.to_string())).collect())
                    .unwrap_or_default();
                let id = self.transfer_queue.enqueue(paths, dest_dir, is_move);
                return json!({ "ok": true, "enqueued_id": id });
            }
            "pause" => {
                let paused = params.get("paused").and_then(|v| v.as_bool()).unwrap_or(true);
                self.transfer_queue.paused.store(paused, Ordering::Relaxed);
            }
            "cancel" => {
                let id = params.get("id").and_then(|v| v.as_u64()).unwrap_or(0);
                if let Ok(mut g) = self.transfer_queue.items.lock() {
                    if let Some(it) = g.iter_mut().find(|i| i.id == id) {
                        it.status = "Cancelled".to_string();
                    }
                }
            }
            "speed_limit" => {
                let kbps = params.get("kbps").and_then(|v| v.as_u64()).unwrap_or(0);
                self.transfer_queue.speed_limit_kbps.store(kbps, Ordering::Relaxed);
            }
            "clear_finished" => {
                if let Ok(mut g) = self.transfer_queue.items.lock() {
                    g.retain(|i| i.status == "Queued" || i.status == "Running" || i.status == "Paused");
                }
            }
            _ => {}
        }
        let items = self.transfer_queue.items.lock().map(|g| g.clone()).unwrap_or_default();
        let paused = self.transfer_queue.paused.load(Ordering::Relaxed);
        let speed_limit_kbps = self.transfer_queue.speed_limit_kbps.load(Ordering::Relaxed);
        json!({
            "ok": true,
            "paused": paused,
            "speed_limit_kbps": speed_limit_kbps,
            "items": items,
        })
    }

    fn rpc_dir_tree(&self, params: &Value) -> Value {
        let path_str = params.get("path").and_then(|v| v.as_str()).unwrap_or("");
        if path_str.is_empty() {
            let drives: Vec<Value> = DriveItem::enumerate_drives()
                .into_iter()
                .map(|d| {
                    let short = d.root_path.trim_end_matches('\\').to_string();
                    json!({
                        "name": if d.name.is_empty() { short } else { format!("{} ({})", short, d.name) },
                        "full_path": d.root_path,
                        "has_children": true,
                    })
                })
                .collect();
            return json!({ "ok": true, "nodes": drives });
        }
        let nodes = FrecencyStore::list_subdirectories(
            Path::new(path_str),
            self.settings.show_hidden_files,
        );
        json!({ "ok": true, "nodes": nodes })
    }

    fn rpc_smart_jump(&self, params: &Value) -> Value {
        let query = params.get("query").and_then(|v| v.as_str()).unwrap_or("");
        let results = self.frecency_store.query_smart_jump(query, 30);
        json!({ "ok": true, "items": results })
    }

    fn rpc_image_converter(&self, params: &Value) -> Value {
        let paths: Vec<String> = params
            .get("paths")
            .and_then(|v| v.as_array())
            .map(|a| a.iter().filter_map(|x| x.as_str().map(|s| s.to_string())).collect())
            .unwrap_or_default();
        let opts = ImageConvertOptions {
            target_format: params.get("target_format").and_then(|v| v.as_str()).unwrap_or("webp").to_string(),
            scale_percent: params.get("scale_percent").and_then(|v| v.as_u64()).map(|n| n as u32),
            max_width: params.get("resize_width").and_then(|v| v.as_u64()).map(|n| n as u32),
            max_height: params.get("resize_height").and_then(|v| v.as_u64()).map(|n| n as u32),
            rotate_degrees: params.get("rotate_degrees").and_then(|v| v.as_u64()).unwrap_or(0) as u32,
            flip_horizontal: params.get("flip_horizontal").and_then(|v| v.as_bool()).unwrap_or(false),
            flip_vertical: params.get("flip_vertical").and_then(|v| v.as_bool()).unwrap_or(false),
            grayscale: params.get("grayscale").and_then(|v| v.as_bool()).unwrap_or(false),
            watermark_banner: params.get("watermark_banner").and_then(|v| v.as_bool()).unwrap_or(false),
            output_dir: params.get("output_dir").and_then(|v| v.as_str()).unwrap_or("").to_string(),
            filename_suffix: params.get("filename_suffix").and_then(|v| v.as_str()).unwrap_or("_conv").to_string(),
        };
        let results = ImageConverterService::convert_batch(&paths, &opts);
        json!({ "ok": true, "results": results })
    }

    fn rpc_pdf_toolkit(&self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("inspect");
        match sub {
            "inspect" => {
                let path = Path::new(params.get("path").and_then(|v| v.as_str()).unwrap_or(""));
                match PdfToolkitService::inspect_pdf(path) {
                    Ok(info) => json!({ "ok": true, "info": info }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "merge" => {
                let paths: Vec<String> = params
                    .get("paths")
                    .and_then(|v| v.as_array())
                    .map(|a| a.iter().filter_map(|x| x.as_str().map(|s| s.to_string())).collect())
                    .unwrap_or_default();
                let out_path = Path::new(params.get("output_path").and_then(|v| v.as_str()).unwrap_or("merged.pdf"));
                match PdfToolkitService::merge_pdfs(&paths, out_path) {
                    Ok(pages) => json!({ "ok": true, "total_pages": pages, "output_path": out_path.to_string_lossy() }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "extract" => {
                let path = Path::new(params.get("path").and_then(|v| v.as_str()).unwrap_or(""));
                let range_str = params.get("range").and_then(|v| v.as_str()).unwrap_or("1");
                let out_path = Path::new(params.get("output_path").and_then(|v| v.as_str()).unwrap_or("extracted.pdf"));
                let mut pages = Vec::new();
                for part in range_str.split(|c| c == ',' || c == ';') {
                    let t = part.trim();
                    if let Some((a, b)) = t.split_once('-') {
                        if let (Ok(start), Ok(end)) = (a.trim().parse::<u32>(), b.trim().parse::<u32>()) {
                            for p in start.min(end)..=start.max(end) {
                                pages.push(p);
                            }
                        }
                    } else if let Ok(p) = t.parse::<u32>() {
                        pages.push(p);
                    }
                }
                match PdfToolkitService::extract_page_range(path, out_path, &pages) {
                    Ok(n) => json!({ "ok": true, "extracted_pages": n, "output_path": out_path.to_string_lossy() }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "rotate" => {
                let path = Path::new(params.get("path").and_then(|v| v.as_str()).unwrap_or(""));
                let deg = params.get("degrees").and_then(|v| v.as_i64()).unwrap_or(90);
                let out_path = Path::new(params.get("output_path").and_then(|v| v.as_str()).unwrap_or("rotated.pdf"));
                match PdfToolkitService::rotate_pdf_pages(path, out_path, deg) {
                    Ok(pages) => json!({ "ok": true, "rotated_pages": pages, "output_path": out_path.to_string_lossy() }),
                    Err(e) => json!({ "error": e }),
                }
            }
            _ => json!({ "error": "Unknown PDF sub-action" }),
        }
    }

    fn rpc_file_vault(&self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("encrypt");
        let path = Path::new(params.get("path").and_then(|v| v.as_str()).unwrap_or(""));
        let password = params.get("password").and_then(|v| v.as_str()).unwrap_or("");
        let keyfile = params
            .get("keyfile")
            .and_then(|v| v.as_str())
            .filter(|s| !s.trim().is_empty())
            .map(Path::new);
        let delete_source = params.get("delete_source").and_then(|v| v.as_bool()).unwrap_or(false);

        if sub == "decrypt" {
            let out_path = params
                .get("output_path")
                .and_then(|v| v.as_str())
                .filter(|s| !s.trim().is_empty())
                .map(PathBuf::from)
                .unwrap_or_else(|| {
                    let s = path.to_string_lossy();
                    if let Some(stripped) = s.strip_suffix(".menc") {
                        PathBuf::from(stripped)
                    } else {
                        path.with_extension("decrypted")
                    }
                });
            match modern_explorer_core::power_tools_v2::FileVaultService::decrypt_file(path, &out_path, password, keyfile) {
                Ok(bytes) => {
                    if delete_source {
                        let _ = fs::remove_file(path);
                    }
                    json!({ "ok": true, "output_path": out_path.to_string_lossy(), "bytes": bytes, "size_text": format_bytes(bytes) })
                }
                Err(e) => json!({ "error": e }),
            }
        } else {
            let out_str = params
                .get("output_path")
                .and_then(|v| v.as_str())
                .filter(|s| !s.trim().is_empty())
                .map(PathBuf::from)
                .unwrap_or_else(|| PathBuf::from(format!("{}.menc", path.to_string_lossy())));
            match modern_explorer_core::power_tools_v2::FileVaultService::encrypt_file(path, &out_str, password, keyfile) {
                Ok(bytes) => {
                    if delete_source {
                        let _ = FileOperationExecutor::delete_entry(path);
                    }
                    json!({ "ok": true, "output_path": out_str.to_string_lossy(), "bytes": bytes, "size_text": format_bytes(bytes) })
                }
                Err(e) => json!({ "error": e }),
            }
        }
    }

    fn rpc_ntfs_locksmith(&self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("inspect");
        let path = Path::new(params.get("path").and_then(|v| v.as_str()).unwrap_or("."));
        if sub == "kill_process" {
            let pid = params.get("pid").and_then(|v| v.as_u64()).unwrap_or(0) as u32;
            if let Err(e) = NtfsAndLockInspectorService::kill_process_by_pid(pid) {
                return json!({ "error": e });
            }
        } else if sub == "remove_stream" {
            if let Err(e) = NtfsAndLockInspectorService::unblock_zone_identifier(path) {
                return json!({ "error": e });
            }
        }
        let info = NtfsAndLockInspectorService::inspect_path(path);
        json!({ "ok": true, "info": info })
    }

    fn rpc_hex_editor(&self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("read");
        let path = Path::new(params.get("path").and_then(|v| v.as_str()).unwrap_or(""));
        match sub {
            "read" => {
                let offset = params.get("offset").and_then(|v| v.as_u64()).unwrap_or(0);
                let length = params.get("length").and_then(|v| v.as_u64()).unwrap_or(512) as usize;
                match FileContentService::read_hex_editor_page(path, offset, length) {
                    Ok(page) => json!({ "ok": true, "page": page }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "patch" => {
                let patches: Vec<(u64, u8)> = params
                    .get("patches")
                    .and_then(|v| v.as_array())
                    .map(|arr| {
                        arr.iter()
                            .filter_map(|item| {
                                let off = item.get("offset")?.as_u64()?;
                                let b = item.get("byte")?.as_u64()? as u8;
                                Some((off, b))
                            })
                            .collect()
                    })
                    .unwrap_or_default();
                match FileContentService::apply_hex_patches(path, &patches) {
                    Ok(count) => json!({ "ok": true, "patched_count": count }),
                    Err(e) => json!({ "error": e }),
                }
            }
            "search" => {
                let query = params.get("query").and_then(|v| v.as_str()).unwrap_or("");
                let is_hex = params.get("is_hex").and_then(|v| v.as_bool()).unwrap_or(false);
                match FileContentService::search_binary(path, query, is_hex, 100) {
                    Ok(offsets) => json!({ "ok": true, "offsets": offsets }),
                    Err(e) => json!({ "error": e }),
                }
            }
            _ => json!({ "error": "Unknown hex_editor sub-action" }),
        }
    }

    fn rpc_data_converter(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("convert");
        match sub {
            "push_clipboard" => {
                if let Some(txt) = params.get("text").and_then(|v| v.as_str()) {
                    self.clipboard_history.push_entry(txt.to_string(), "Editor/UI");
                }
                json!({ "ok": true, "history": self.clipboard_history.items })
            }
            "clear_clipboard" => {
                self.clipboard_history.items.clear();
                self.clipboard_history.save();
                json!({ "ok": true, "history": self.clipboard_history.items })
            }
            "list_clipboard" => {
                json!({ "ok": true, "history": self.clipboard_history.items })
            }
            _ => {
                let input = params.get("input").and_then(|v| v.as_str()).unwrap_or("");
                let from_fmt = params.get("from_format").and_then(|v| v.as_str()).unwrap_or("auto");
                let to_fmt = params.get("to_format").and_then(|v| v.as_str()).unwrap_or("json");
                match DataConverterService::convert_structured_data(input, from_fmt, to_fmt) {
                    Ok(output) => {
                        self.clipboard_history.push_entry(output.clone(), &format!("{} → {}", from_fmt.to_uppercase(), to_fmt.to_uppercase()));
                        json!({ "ok": true, "output": output, "history": self.clipboard_history.items })
                    }
                    Err(e) => json!({ "error": e, "history": self.clipboard_history.items }),
                }
            }
        }
    }

    fn rpc_user_commands(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("list");
        match sub {
            "save" => {
                if let Ok(item) = serde_json::from_value::<UserCommandItem>(params.get("item").cloned().unwrap_or_default()) {
                    self.user_commands.commands.retain(|c| c.id != item.id);
                    self.user_commands.commands.push(item);
                    self.user_commands.save();
                }
            }
            "delete" => {
                if let Some(id) = params.get("id").and_then(|v| v.as_str()) {
                    self.user_commands.commands.retain(|c| c.id != id);
                    self.user_commands.save();
                }
            }
            "execute" => {
                let id = params.get("id").and_then(|v| v.as_str()).unwrap_or("");
                let active_dir = params.get("active_dir").and_then(|v| v.as_str()).unwrap_or(".");
                let target_dir = params.get("target_dir").and_then(|v| v.as_str()).unwrap_or(".");
                let left_dir = params.get("left_dir").and_then(|v| v.as_str()).unwrap_or(".");
                let right_dir = params.get("right_dir").and_then(|v| v.as_str()).unwrap_or(".");
                let selected_file = params.get("selected_file").and_then(|v| v.as_str()).unwrap_or("");
                let selected_list: Vec<String> = params
                    .get("selected_list")
                    .and_then(|v| v.as_array())
                    .map(|a| a.iter().filter_map(|x| x.as_str().map(|s| s.to_string())).collect())
                    .unwrap_or_default();
                if let Some(cmd) = self.user_commands.commands.iter().find(|i| i.id == id).cloned() {
                    return match UserCommandStore::execute_command(
                        &cmd,
                        active_dir,
                        target_dir,
                        selected_file,
                        &selected_list,
                        left_dir,
                        right_dir,
                    ) {
                        Ok(msg) => json!({ "ok": true, "message": msg, "items": self.user_commands.commands }),
                        Err(e) => json!({ "error": e, "items": self.user_commands.commands }),
                    };
                } else {
                    return json!({ "error": "Tool nicht gefunden." });
                }
            }
            _ => {}
        }
        json!({ "ok": true, "items": self.user_commands.commands })
    }

    fn rpc_share_server(&mut self, params: &Value) -> Value {
        let sub = params.get("sub").and_then(|v| v.as_str()).unwrap_or("status");
        if sub == "start" {
            let dir = params.get("dir").and_then(|v| v.as_str()).unwrap_or(".");
            let port = params.get("port").and_then(|v| v.as_u64()).unwrap_or(8080) as u16;
            if let Err(e) = self.share_server.start(Path::new(dir), port) {
                return json!({ "error": e });
            }
        } else if sub == "stop" {
            self.share_server.stop();
        }
        let running = self.share_server.running.load(Ordering::Relaxed);
        let logs = self.share_server.access_log.lock().map(|g| g.clone()).unwrap_or_default();
        json!({
            "ok": true,
            "running": running,
            "url": self.share_server.url,
            "qr_svg": self.share_server.qr_svg,
            "shared_root": self.share_server.shared_root.to_string_lossy(),
            "access_log": logs,
        })
    }

    fn rpc_fs_watcher_poll(&self) -> Value {
        let (dirty_left, dirty_right) = self.fs_watcher.poll_dirty();
        json!({
            "ok": true,
            "dirty_left": dirty_left,
            "dirty_right": dirty_right,
        })
    }

    fn rpc_link_manager(&self, params: &Value) -> Value {
        let link_type = params.get("link_type").and_then(|v| v.as_str()).unwrap_or("symlink");
        let target_path = Path::new(params.get("target_path").and_then(|v| v.as_str()).unwrap_or(""));
        let link_path = Path::new(params.get("link_path").and_then(|v| v.as_str()).unwrap_or(""));
        match LinkManagerService::create_link(link_type, target_path, link_path) {
            Ok(msg) => json!({ "ok": true, "message": msg }),
            Err(e) => json!({ "error": e }),
        }
    }

    fn rpc_check_updates(&self, _params: &Value) -> Value {
        match GitHubUpdateService::check_for_update_full() {
            Ok((is_newer, rel)) => {
                let notes = GitHubUpdateService::format_release_notes_for_display(&rel.body);
                let (asset_name, asset_size) = GitHubUpdateService::select_best_rust_asset(&rel)
                    .map(|a| (a.name.clone(), a.size))
                    .unwrap_or_default();
                let name = if rel.name.is_empty() {
                    rel.tag_name.clone()
                } else {
                    rel.name
                };
                let html_url = if rel.html_url.is_empty() {
                    GitHubUpdateService::RELEASES_URL.to_string()
                } else {
                    rel.html_url
                };
                json!({
                    "update_available": is_newer,
                    "current_version": GitHubUpdateService::CURRENT_VERSION,
                    "tag_name": rel.tag_name,
                    "name": name,
                    "html_url": html_url,
                    "release_notes": notes,
                    "asset_name": asset_name,
                    "asset_size": asset_size,
                })
            }
            Err(e) => json!({
                "update_available": false,
                "current_version": GitHubUpdateService::CURRENT_VERSION,
                "error": e,
                "html_url": GitHubUpdateService::RELEASES_URL,
            }),
        }
    }

    fn rpc_install_update(&self, _params: &Value) -> Value {
        let rel = match GitHubUpdateService::fetch_latest_release() {
            Ok(r) => r,
            Err(e) => return json!({ "ok": false, "error": e }),
        };
        match GitHubUpdateService::download_and_install_update(&rel) {
            Ok((path, directly_replaced)) => {
                SettingsStore::save(&self.settings);
                std::thread::spawn(move || {
                    std::thread::sleep(std::time::Duration::from_millis(700));
                    if directly_replaced {
                        let _ = GitHubUpdateService::restart_application();
                    }
                    std::process::exit(0);
                });
                json!({
                    "ok": true,
                    "tag_name": rel.tag_name,
                    "downloaded_path": path,
                    "directly_replaced": directly_replaced,
                })
            }
            Err(e) => json!({
                "ok": false,
                "error": e,
            }),
        }
    }
}

