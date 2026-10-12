use crate::app_paths::AppPaths;
use serde::{Deserialize, Serialize};
use std::fs;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TabLockState {
    Normal,
    Pinned,
    Locked,
    LockedDirChangesAllowed,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ExplorerViewMode {
    Details,
    List,
    Tiles,
    SmallIcons,
    MediumIcons,
    LargeIcons,
    ExtraLargeIcons,
    Content,
}

impl ExplorerViewMode {
    pub fn all() -> &'static [(ExplorerViewMode, &'static str)] {
        &[
            (Self::Details, "☰ Details"),
            (Self::List, "≡ Liste (List)"),
            (Self::Tiles, "⊞ Kacheln (Tiles)"),
            (Self::SmallIcons, "▪ Kleine Symbole (Small)"),
            (Self::MediumIcons, "◼ Mittlere Symbole (Medium)"),
            (Self::LargeIcons, "⬛ Große Symbole (Large)"),
            (Self::ExtraLargeIcons, "🖼 Extra große Symbole (XL)"),
            (Self::Content, "▤ Inhalt (Content)"),
        ]
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SavedTabState {
    pub path: String,
    pub home_path: String,
    pub lock_state: TabLockState,
    pub view_mode: ExplorerViewMode,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionState {
    pub left_tabs: Vec<SavedTabState>,
    pub right_tabs: Vec<SavedTabState>,
    pub active_left_tab: usize,
    pub active_right_tab: usize,
    pub is_dual_pane: bool,
    pub show_preview_pane: bool,
    pub show_quick_console: bool,
    pub split_ratio: f32,
}

impl Default for SessionState {
    fn default() -> Self {
        let cwd = std::env::current_dir()
            .map(|p| p.to_string_lossy().to_string())
            .unwrap_or_else(|_| ".".to_string());
        let default_tab = SavedTabState {
            path: cwd.clone(),
            home_path: cwd,
            lock_state: TabLockState::Normal,
            view_mode: ExplorerViewMode::Details,
        };
        Self {
            left_tabs: vec![default_tab.clone()],
            right_tabs: vec![default_tab],
            active_left_tab: 0,
            active_right_tab: 0,
            is_dual_pane: true,
            show_preview_pane: false,
            show_quick_console: false,
            split_ratio: 0.5,
        }
    }
}

pub struct SessionStore;

impl SessionStore {
    pub fn load() -> SessionState {
        let path = AppPaths::session_file();
        if let Ok(text) = fs::read_to_string(&path) {
            if let Ok(state) = serde_json::from_str::<SessionState>(&text) {
                return state;
            }
        }
        SessionState::default()
    }

    pub fn save(state: &SessionState) {
        let path = AppPaths::session_file();
        if let Ok(json) = serde_json::to_string_pretty(state) {
            let _ = fs::write(path, json);
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FavoriteEntry {
    pub name: String,
    pub path: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct FavoritesStore {
    pub items: Vec<FavoriteEntry>,
}

impl FavoritesStore {
    pub fn load() -> Self {
        let path = AppPaths::favorites_file();
        if let Ok(text) = fs::read_to_string(&path) {
            if let Ok(store) = serde_json::from_str::<FavoritesStore>(&text) {
                return store;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let path = AppPaths::favorites_file();
        if let Ok(json) = serde_json::to_string_pretty(self) {
            let _ = fs::write(path, json);
        }
    }

    pub fn is_favorite(&self, path: &str) -> bool {
        self.items
            .iter()
            .any(|i| i.path.eq_ignore_ascii_case(path))
    }

    pub fn toggle(&mut self, path: &str) -> bool {
        if self.is_favorite(path) {
            self.items
                .retain(|i| !i.path.eq_ignore_ascii_case(path));
            self.save();
            false
        } else {
            let name = std::path::Path::new(path)
                .file_name()
                .map(|n| n.to_string_lossy().to_string())
                .unwrap_or_else(|| path.to_string());
            self.items.push(FavoriteEntry {
                name,
                path: path.to_string(),
            });
            self.save();
            true
        }
    }
}

#[derive(Debug, Clone)]
pub struct CommandDefinition {
    pub id: &'static str,
    pub title: &'static str,
    pub shortcut: &'static str,
    pub category: &'static str,
}

pub struct CommandRegistry;

impl CommandRegistry {
    pub fn all_commands() -> &'static [CommandDefinition] {
        &[
            CommandDefinition { id: "help.shortcuts", title: "Keyboard Shortcuts Reference", shortcut: "F1", category: "Help" },
            CommandDefinition { id: "file.rename", title: "Rename Selected Item", shortcut: "F2", category: "File" },
            CommandDefinition { id: "view.quickViewer", title: "Open Quick Viewer", shortcut: "F3", category: "View" },
            CommandDefinition { id: "file.edit", title: "Open Internal Text Editor", shortcut: "F4", category: "File" },
            CommandDefinition { id: "file.copy", title: "Copy to Opposite Pane", shortcut: "F5", category: "File" },
            CommandDefinition { id: "file.move", title: "Move to Opposite Pane", shortcut: "F6", category: "File" },
            CommandDefinition { id: "file.newFile", title: "New File from Template", shortcut: "F7", category: "File" },
            CommandDefinition { id: "file.newFolder", title: "Create New Folder", shortcut: "Shift+F7", category: "File" },
            CommandDefinition { id: "file.delete", title: "Delete Selected Items", shortcut: "F8 / Del", category: "File" },
            CommandDefinition { id: "tools.terminal", title: "Open Terminal Here", shortcut: "F9", category: "Tools" },
            CommandDefinition { id: "view.commandPalette", title: "Open Command Palette", shortcut: "F10 / Ctrl+Shift+P", category: "View" },
            CommandDefinition { id: "view.dualPane", title: "Toggle Dual-Pane / Single-Pane", shortcut: "F12", category: "View" },
            CommandDefinition { id: "tools.batchRename", title: "Multi-Rename Tool (Batch Rename)", shortcut: "Ctrl+M", category: "Power Tools" },
            CommandDefinition { id: "tools.diff", title: "Visual Side-by-Side File Diff Viewer", shortcut: "Ctrl+Shift+U", category: "Power Tools" },
            CommandDefinition { id: "tools.hashes", title: "Checksum Generator & Verifier", shortcut: "Ctrl+Shift+K", category: "Power Tools" },
            CommandDefinition { id: "tools.splitShred", title: "File Splitter, Joiner & Secure Shredder", shortcut: "Ctrl+Shift+X", category: "Power Tools" },
            CommandDefinition { id: "tools.tags", title: "Color File Tags & Notes", shortcut: "Ctrl+Shift+L", category: "Power Tools" },
            CommandDefinition { id: "tools.workspaces", title: "Workspace Profiles Manager", shortcut: "Ctrl+Shift+O", category: "Power Tools" },
            CommandDefinition { id: "tools.export", title: "Smart Copy / Path & Table Exporter", shortcut: "Ctrl+Shift+E", category: "Power Tools" },
            CommandDefinition { id: "tools.basket", title: "Virtual File Basket (Drop-Basket)", shortcut: "Ctrl+Shift+B", category: "Power Tools" },
            CommandDefinition { id: "tools.attrExif", title: "EXIF / Media Metadata & Timestamp/Attribute Editor", shortcut: "Ctrl+Shift+A", category: "Power Tools" },
            CommandDefinition { id: "tools.quickConsole", title: "Toggle Embedded Quick Command Bar (>_)", shortcut: "Ctrl+Shift+J", category: "Power Tools" },
            CommandDefinition { id: "tools.git", title: "Extended Git Panel (Branches, Status, Diff, Log)", shortcut: "Ctrl+Shift+G", category: "Power Tools" },
            CommandDefinition { id: "tools.sync", title: "Directory Compare & Synchronize", shortcut: "Ctrl+Shift+S", category: "Power Tools" },
            CommandDefinition { id: "tools.treemap", title: "Graphical Disk Space Analyzer & Squarified Treemap", shortcut: "Ctrl+Shift+W", category: "Power Tools" },
            CommandDefinition { id: "tools.deepSearch", title: "Recursive Deep Search", shortcut: "Alt+F7", category: "Power Tools" },
            CommandDefinition { id: "tools.duplicates", title: "Duplicate File Finder (3-Stage)", shortcut: "Ctrl+Shift+D", category: "Power Tools" },
            CommandDefinition { id: "tools.archive", title: "Archive Browser & Packer (.zip / .tar.gz)", shortcut: "Alt+F5", category: "Power Tools" },
            CommandDefinition { id: "tools.remote", title: "Built-in FTP / FTPS / SFTP Client", shortcut: "Ctrl+Shift+C", category: "Power Tools" },
            CommandDefinition { id: "view.branchView", title: "Toggle Branch View (Flat View)", shortcut: "Ctrl+B", category: "View" },
            CommandDefinition { id: "view.previewPane", title: "Toggle Docked Quick Preview Pane", shortcut: "Ctrl+Q", category: "View" },
            CommandDefinition { id: "edit.undo", title: "Undo Last File Operation", shortcut: "Ctrl+Z", category: "Edit" },
            CommandDefinition { id: "app.settings", title: "Open Settings (Colors, Fonts, Languages)", shortcut: "Ctrl+,", category: "App" },
            CommandDefinition { id: "app.about", title: "About Modern Explorer & Check for Updates", shortcut: "", category: "App" },
        ]
    }
}

