use std::fs;
use std::path::PathBuf;

pub struct AppPaths;

impl AppPaths {
    pub fn app_data_dir() -> PathBuf {
        let base = dirs::data_local_dir().unwrap_or_else(|| PathBuf::from("."));
        let dir = base.join("ModernExplorer");
        let _ = fs::create_dir_all(&dir);
        dir
    }

    pub fn settings_file() -> PathBuf {
        Self::app_data_dir().join("settings.json")
    }

    pub fn session_file() -> PathBuf {
        Self::app_data_dir().join("session.json")
    }

    pub fn favorites_file() -> PathBuf {
        Self::app_data_dir().join("favorites.json")
    }

    pub fn keybindings_file() -> PathBuf {
        Self::app_data_dir().join("keybindings.json")
    }

    pub fn file_tags_file() -> PathBuf {
        Self::app_data_dir().join("file-tags.json")
    }

    pub fn workspaces_file() -> PathBuf {
        Self::app_data_dir().join("workspaces.json")
    }

    pub fn remote_connections_file() -> PathBuf {
        Self::app_data_dir().join("remote-connections.json")
    }

    pub fn frecency_file() -> PathBuf {
        Self::app_data_dir().join("directory-frecency.json")
    }

    pub fn user_commands_file() -> PathBuf {
        Self::app_data_dir().join("user-commands.json")
    }

    pub fn selection_presets_file() -> PathBuf {
        Self::app_data_dir().join("selection-presets.json")
    }

    pub fn clipboard_history_file() -> PathBuf {
        Self::app_data_dir().join("clipboard-history.json")
    }
}


