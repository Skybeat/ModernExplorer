use modern_explorer_core::{
    archive::{ArchiveFormat, ArchiveService},
    features::{
        BatchAttributeTimestampOptions, ChecksumBatchService, ChecksumManifestFormat,
        FileBasketService, FileDiffService, FileSplitAndShredService, FileTagColor,
        FileTagService, GitIntegrationService, MetadataAndAttributeService,
        QuickCommandRunnerService, ShredMode, SmartExportFormat, SmartExportService,
        WorkspaceProfile, WorkspaceProfileService,
    },
    filesystem::{
        format_bytes, sort_entries_with_options, DeepSearchCriteria, DeepSearchService,
        DirectoryEnumerator, DriveItem, DuplicateFinderService, DuplicateScanProgress,
        DuplicateScanResult, FileCreationService, FileEntry, SortColumn, SortDirection,
    },
    localization::LanguageCode,
    operations::{
        BatchRenameOptions, BatchRenameService, CaseTransform, ConflictResolution,
        DirectorySyncService, FileOperationExecutor, SyncComparisonMode, SyncDirectionMode,
        UndoAction, UndoHistoryService,
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
    pub tag_color_hex: Option<String>,
    pub tag_label: Option<String>,
    pub tag_note: Option<String>,
}

#[derive(Clone)]
pub struct DuplicatesJobState {
    pub running: bool,
    pub progress: DuplicateScanProgress,
    pub result: Option<DuplicateScanResult>,
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
            "reset_font_sizes" => self.rpc_reset_font_sizes(),
            "reset_window_bounds" => self.rpc_reset_window_bounds(),
            "update_window_bounds" => self.rpc_update_window_bounds(params),
            "reset_all_settings" => self.rpc_reset_all_settings(),
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
            "check_updates" => self.rpc_check_updates(params),
            "install_update" => self.rpc_install_update(params),
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
            - **Studio Design Edition (`Nuharji/markdown-studio` Style) + 100% .NET Feature-Parität**: Modernste Studio-Oberfläche mit Menüleiste, Optionsleiste, vertikaler Werkzeug-Rail, Dual-Pane-Workspace, rechtem Inspector-Dock und 4 Studio-Themes (`Dark`, `Light`, `GitHub Light`, `GitHub Dark`).\n\
            - **Kein Aufblitzen von Terminalfenstern (`CREATE_NO_WINDOW`)**: Reine Rust-Git-Erkennung (`.git/HEAD` in `< 0.01 ms`) beim Navigieren sowie `CREATE_NO_WINDOW` für alle Hintergrundprozesse.\n\
            - **Vollständige Einstellungen wie in der .NET-Version**: Alle 6 Kategorien (Darstellung & Design, Farben & Hervorhebung inkl. aller 7 Treemap-Farben, Dateiliste & Verhalten, Bildvorschau beim Hover, Schriftgrößen für 8 Hauptbereiche + 10 Einzeldialoge, Sitzung/Updates/Fenster + Tastenkürzel).\n\
            - **Mehrzeiliger, scrollbarer Release-Notes-Bereich & Browser-Link (`v{}`)**: Vollständige mehrzeilige Release Notes direkt im Update- und Info-Fenster lesbar und scrollbar, ergänzt durch den direkten Link **🌐 Auf GitHub ansehen ↗**.\n\
            - **Alle 20 integrierten Power-Tools**:\n\
              - Mehrfach-Umbenennungstool (`Strg+M`) inkl. Bild-/EXIF-Platzhaltern (`[Width]`, `[Height]`, `[Resolution]`)\n\
              - Visueller Side-by-Side Datei-Diff-Viewer (`Strg+Shift+U`) mit LCS & Hunk-Merge\n\
              - Prüfsummen-Generator & Manifest-Verifizierer (`Strg+Shift+K`) für SHA-256, SHA-512, MD5, CRC32\n\
              - Datei-Splitter/Joiner (`.001`) & Sicherer Datei-Shredder (`1-Pass`, `3-Pass DoD`, `7-Pass`)\n\
              - Farbige Datei-Tags & Notizen (`Strg+Shift+L`), Arbeitsbereiche (`Strg+Shift+O`), Smart-Export (`Strg+Shift+E`), Virtueller Sammelkorb (`Strg+Shift+B`), Attribute/EXIF-Editor (`Strg+Shift+A`), Eingebettete Konsole (`>_`), Erweitertes Git-Panel (`Strg+Shift+G`), Ordner-Synchronisierung (`Strg+Shift+S`) & Squarified Treemap (`Strg+Shift+W`).",
            GitHubUpdateService::CURRENT_VERSION,
            GitHubUpdateService::CURRENT_VERSION
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

    fn rpc_list_dir(&self, params: &Value) -> Value {
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

        let path_obj = Path::new(raw_path);
        let canonical = if path_obj.is_dir() {
            path_obj.to_path_buf()
        } else {
            std::env::current_dir().unwrap_or_else(|_| PathBuf::from("."))
        };
        let clean_path = canonical.to_string_lossy().to_string();

        let mut entries = DirectoryEnumerator::list_directory(
            &canonical,
            self.settings.show_hidden_files,
            branch_view,
        );

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
        sort_entries_with_options(
            &mut entries,
            sort_col,
            sort_dir,
            self.settings.sort_folders_first,
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

                FileItemDto {
                    full_path: e.full_path.clone(),
                    display_name,
                    name: e.name.clone(),
                    extension: e.extension.clone(),
                    type_text,
                    size_bytes: e.size_bytes,
                    effective_size: eff,
                    size_text: e.display_size(),
                    size_bar_percent: pct,
                    is_directory: e.is_directory,
                    is_hidden: e.is_hidden,
                    is_readonly: e.is_readonly,
                    modified_text: e.modified.format("%d.%m.%Y %H:%M").to_string(),
                    tag_color_hex: tag_hex,
                    tag_label: tag_lbl,
                    tag_note,
                }
            })
            .collect();

        // Build breadcrumbs
        let mut breadcrumbs = Vec::new();
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
        let git_badge = GitIntegrationService::get_quick_badge(&canonical);

        let is_fav = self.favorites.is_favorite(&clean_path);
        let folder_title = canonical
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| clean_path.clone());

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
        let dest_dir = PathBuf::from(
            params
                .get("dest_dir")
                .and_then(|v| v.as_str())
                .unwrap_or("."),
        );
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
        for src_str in paths {
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
            "MarkedItemText" => ColorSettingTarget::MarkedItemText,
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
        let path = Path::new(path_str);
        if !path.is_file() {
            return json!({
                "is_file": false,
                "text": "Ordner oder kein Dateielement ausgewählt.",
            });
        }
        let text = FileContentService::read_text_preview(path, 128 * 1024).unwrap_or_default();
        let hex = FileContentService::format_hex_dump(path, 4096).unwrap_or_default();
        let csv_rows = FileContentService::parse_csv_rows(&text, 100);
        let media = MetadataAndAttributeService::inspect_media_metadata(path);
        let hashes = ChecksumBatchService::compute_checksums(&[path_str.to_string()]);
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
            .unwrap_or("");
        let right_path = params
            .get("right_path")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        let ignore_ws = params
            .get("ignore_whitespace")
            .and_then(|v| v.as_bool())
            .unwrap_or(false);

        let left_text = if let Some(lt) = params.get("left_text").and_then(|v| v.as_str()) {
            lt.to_string()
        } else {
            fs::read_to_string(left_path).unwrap_or_default()
        };
        let right_text = if let Some(rt) = params.get("right_text").and_then(|v| v.as_str()) {
            rt.to_string()
        } else {
            fs::read_to_string(right_path).unwrap_or_default()
        };

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

        if rescan || self.cached_treemap.is_none() {
            self.cached_treemap = Some(StorageTreemapService::scan_directory_tree(dir, 6));
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
        let fmt = if dest.to_string_lossy().ends_with(".tar.gz") || dest.to_string_lossy().ends_with(".tgz") {
            ArchiveFormat::TarGz
        } else {
            ArchiveFormat::Zip
        };
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
                _ => RemoteProtocol::Sftp,
            };
            let prof = RemoteConnectionProfile {
                name: params.get("name").and_then(|v| v.as_str()).unwrap_or("Server").to_string(),
                protocol: proto,
                host: params.get("host").and_then(|v| v.as_str()).unwrap_or("example.com").to_string(),
                port: params.get("port").and_then(|v| v.as_u64()).unwrap_or(22) as u16,
                username: params.get("username").and_then(|v| v.as_str()).unwrap_or("root").to_string(),
                remote_path: params.get("remote_path").and_then(|v| v.as_str()).unwrap_or("/").to_string(),
                ssh_private_key_path: String::new(),
            };
            self.remote_service.upsert(prof);
        } else if sub == "delete" {
            if let Some(name) = params.get("name").and_then(|v| v.as_str()) {
                self.remote_service.remove(name);
            }
        }
        json!({
            "ok": true,
            "profiles": self.remote_service.profiles,
        })
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
            Ok(path) => {
                SettingsStore::save(&self.settings);
                std::thread::spawn(|| {
                    std::thread::sleep(std::time::Duration::from_millis(750));
                    std::process::exit(0);
                });
                json!({
                    "ok": true,
                    "tag_name": rel.tag_name,
                    "downloaded_path": path,
                })
            }
            Err(e) => json!({
                "ok": false,
                "error": e,
            }),
        }
    }
}

