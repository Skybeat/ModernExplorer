use crate::app_paths::AppPaths;
use serde::{Deserialize, Serialize};
use std::fs;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum RemoteProtocol {
    Sftp,
    Ftps,
    Ftp,
}

impl RemoteProtocol {
    pub fn scheme(self) -> &'static str {
        match self {
            Self::Sftp => "sftp",
            Self::Ftps => "ftps",
            Self::Ftp => "ftp",
        }
    }

    pub fn default_port(self) -> u16 {
        match self {
            Self::Sftp => 22,
            Self::Ftps | Self::Ftp => 21,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RemoteConnectionProfile {
    pub name: String,
    pub protocol: RemoteProtocol,
    pub host: String,
    pub port: u16,
    pub username: String,
    pub remote_path: String,
    pub ssh_private_key_path: String,
}

impl RemoteConnectionProfile {
    pub fn to_virtual_uri(&self) -> String {
        let clean_path = if self.remote_path.starts_with('/') {
            self.remote_path.clone()
        } else {
            format!("/{}", self.remote_path)
        };
        format!(
            "{}://{}@{}:{}{}",
            self.protocol.scheme(),
            self.username,
            self.host,
            self.port,
            clean_path
        )
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct RemoteFileSystemService {
    pub profiles: Vec<RemoteConnectionProfile>,
}

impl RemoteFileSystemService {
    pub fn load() -> Self {
        let path = AppPaths::remote_connections_file();
        if let Ok(text) = fs::read_to_string(&path) {
            if let Ok(svc) = serde_json::from_str::<RemoteFileSystemService>(&text) {
                return svc;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let path = AppPaths::remote_connections_file();
        if let Ok(json) = serde_json::to_string_pretty(self) {
            let _ = fs::write(path, json);
        }
    }

    pub fn upsert(&mut self, profile: RemoteConnectionProfile) {
        self.profiles
            .retain(|p| !p.name.eq_ignore_ascii_case(&profile.name));
        self.profiles.push(profile);
        self.save();
    }

    pub fn remove(&mut self, name: &str) {
        self.profiles
            .retain(|p| !p.name.eq_ignore_ascii_case(name));
        self.save();
    }

    pub fn is_remote_uri(path: &str) -> bool {
        let lower = path.trim().to_ascii_lowercase();
        lower.starts_with("sftp://") || lower.starts_with("ftps://") || lower.starts_with("ftp://")
    }
}

