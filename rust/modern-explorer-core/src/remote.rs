use crate::app_paths::AppPaths;
use chrono::Utc;
use hmac::{Hmac, Mac};
use regex::Regex;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs;
use std::io::Read;
use std::path::Path;

type HmacSha256 = Hmac<Sha256>;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum RemoteProtocol {
    Sftp,
    Ftps,
    Ftp,
    WebDav,
    S3,
}

impl RemoteProtocol {
    pub fn scheme(self) -> &'static str {
        match self {
            Self::Sftp => "sftp",
            Self::Ftps => "ftps",
            Self::Ftp => "ftp",
            Self::WebDav => "webdav",
            Self::S3 => "s3",
        }
    }

    pub fn default_port(self) -> u16 {
        match self {
            Self::Sftp => 22,
            Self::Ftps | Self::Ftp => 21,
            Self::WebDav | Self::S3 => 443,
        }
    }
}

fn default_true() -> bool {
    true
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
    #[serde(default)]
    pub password_or_secret: String,
    #[serde(default)]
    pub bucket: String,
    #[serde(default)]
    pub region: String,
    #[serde(default)]
    pub endpoint_url: String,
    #[serde(default = "default_true")]
    pub use_tls: bool,
}

impl RemoteConnectionProfile {
    pub fn to_virtual_uri(&self) -> String {
        let clean_path = if self.remote_path.starts_with('/') {
            self.remote_path.clone()
        } else {
            format!("/{}", self.remote_path)
        };
        match self.protocol {
            RemoteProtocol::S3 => {
                let b = if self.bucket.is_empty() {
                    "bucket"
                } else {
                    &self.bucket
                };
                format!("s3://{}{} ({})", b, clean_path, self.host)
            }
            RemoteProtocol::WebDav => {
                let http_scheme = if self.use_tls { "https" } else { "http" };
                format!(
                    "webdav://{}@{}:{}{} ({})",
                    self.username, self.host, self.port, clean_path, http_scheme
                )
            }
            _ => format!(
                "{}://{}@{}:{}{}",
                self.protocol.scheme(),
                self.username,
                self.host,
                self.port,
                clean_path
            ),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RemoteEntryDto {
    pub name: String,
    pub full_path: String,
    pub size_bytes: u64,
    pub is_directory: bool,
    pub modified_text: String,
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
        lower.starts_with("sftp://")
            || lower.starts_with("ftps://")
            || lower.starts_with("ftp://")
            || lower.starts_with("webdav://")
            || lower.starts_with("webdavs://")
            || lower.starts_with("s3://")
    }

    fn base_encode64(input: &[u8]) -> String {
        const TABLE: &[u8; 64] =
            b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        let mut out = String::with_capacity((input.len() + 2) / 3 * 4);
        for chunk in input.chunks(3) {
            let b0 = chunk[0] as u32;
            let b1 = if chunk.len() > 1 { chunk[1] as u32 } else { 0 };
            let b2 = if chunk.len() > 2 { chunk[2] as u32 } else { 0 };
            let triple = (b0 << 16) | (b1 << 8) | b2;
            out.push(TABLE[((triple >> 18) & 0x3F) as usize] as char);
            out.push(TABLE[((triple >> 12) & 0x3F) as usize] as char);
            if chunk.len() > 1 {
                out.push(TABLE[((triple >> 6) & 0x3F) as usize] as char);
            } else {
                out.push('=');
            }
            if chunk.len() > 2 {
                out.push(TABLE[(triple & 0x3F) as usize] as char);
            } else {
                out.push('=');
            }
        }
        out
    }

    fn hex_lower(bytes: &[u8]) -> String {
        bytes.iter().map(|b| format!("{:02x}", b)).collect()
    }

    fn hmac_sha256(key: &[u8], data: &[u8]) -> Vec<u8> {
        let mut mac = HmacSha256::new_from_slice(key).expect("HMAC key");
        mac.update(data);
        mac.finalize().into_bytes().to_vec()
    }

    pub fn list_remote_entries(
        profile: &RemoteConnectionProfile,
        subpath: &str,
    ) -> Result<Vec<RemoteEntryDto>, String> {
        match profile.protocol {
            RemoteProtocol::WebDav => Self::webdav_list(profile, subpath),
            RemoteProtocol::S3 => Self::s3_list(profile, subpath),
            _ => Ok(vec![
                RemoteEntryDto {
                    name: "remote_readme.txt".to_string(),
                    full_path: format!(
                        "{}/remote_readme.txt",
                        subpath.trim_end_matches('/')
                    ),
                    size_bytes: 1024,
                    is_directory: false,
                    modified_text: "Remote Server".to_string(),
                },
            ]),
        }
    }

    fn resolve_webdav_url(profile: &RemoteConnectionProfile, subpath: &str) -> String {
        if !profile.endpoint_url.trim().is_empty() {
            let base = profile.endpoint_url.trim_end_matches('/');
            let rel = subpath.trim_start_matches('/');
            if rel.is_empty() {
                format!("{}/", base)
            } else {
                format!("{}/{}", base, rel)
            }
        } else {
            let scheme = if profile.use_tls || profile.port == 443 {
                "https"
            } else {
                "http"
            };
            let rel = if subpath.is_empty() {
                profile.remote_path.trim_start_matches('/')
            } else {
                subpath.trim_start_matches('/')
            };
            format!("{}://{}:{}/{}", scheme, profile.host, profile.port, rel)
        }
    }

    pub fn webdav_list(
        profile: &RemoteConnectionProfile,
        subpath: &str,
    ) -> Result<Vec<RemoteEntryDto>, String> {
        let url = Self::resolve_webdav_url(profile, subpath);
        let mut req = ureq::request("PROPFIND", &url).set("Depth", "1");
        if !profile.username.is_empty() {
            let cred = format!("{}:{}", profile.username, profile.password_or_secret);
            req = req.set(
                "Authorization",
                &format!("Basic {}", Self::base_encode64(cred.as_bytes())),
            );
        }
        let body = r#"<?xml version="1.0" encoding="utf-8" ?><D:propfind xmlns:D="DAV:"><D:allprop/></D:propfind>"#;
        let resp = req
            .set("Content-Type", "application/xml")
            .send_string(body)
            .map_err(|e| format!("WebDAV PROPFIND Fehler: {}", e))?;
        let xml = resp.into_string().map_err(|e| e.to_string())?;
        Ok(Self::parse_webdav_propfind_xml(&xml))
    }

    pub fn parse_webdav_propfind_xml(xml: &str) -> Vec<RemoteEntryDto> {
        let mut entries = Vec::new();
        let re_resp =
            Regex::new(r"(?is)<(?:[a-zA-Z0-9]+:)?response[^>]*>(.*?)</(?:[a-zA-Z0-9]+:)?response>")
                .unwrap();
        let re_href =
            Regex::new(r"(?is)<(?:[a-zA-Z0-9]+:)?href[^>]*>(.*?)</(?:[a-zA-Z0-9]+:)?href>")
                .unwrap();
        let re_len = Regex::new(
            r"(?is)<(?:[a-zA-Z0-9]+:)?getcontentlength[^>]*>(\d+)</(?:[a-zA-Z0-9]+:)?getcontentlength>",
        )
        .unwrap();
        let re_col = Regex::new(r"(?is)<(?:[a-zA-Z0-9]+:)?collection\s*/?>").unwrap();
        let re_mod = Regex::new(
            r"(?is)<(?:[a-zA-Z0-9]+:)?getlastmodified[^>]*>(.*?)</(?:[a-zA-Z0-9]+:)?getlastmodified>",
        )
        .unwrap();

        for (idx, cap) in re_resp.captures_iter(xml).enumerate() {
            let block = &cap[1];
            let href = re_href
                .captures(block)
                .map(|c| c[1].trim().to_string())
                .unwrap_or_default();
            if href.is_empty() || idx == 0 {
                continue;
            }
            let is_dir = re_col.is_match(block) || href.ends_with('/');
            let trimmed = href.trim_end_matches('/');
            let name = trimmed
                .rsplit('/')
                .next()
                .unwrap_or(trimmed)
                .to_string();
            if name.is_empty() {
                continue;
            }
            let size_bytes = re_len
                .captures(block)
                .and_then(|c| c[1].parse::<u64>().ok())
                .unwrap_or(0);
            let modified_text = re_mod
                .captures(block)
                .map(|c| c[1].trim().to_string())
                .unwrap_or_else(|| "-".to_string());

            entries.push(RemoteEntryDto {
                name,
                full_path: href,
                size_bytes,
                is_directory: is_dir,
                modified_text,
            });
        }
        entries
    }

    pub fn webdav_upload(
        profile: &RemoteConnectionProfile,
        local_file: &Path,
        remote_subpath: &str,
    ) -> Result<(), String> {
        let url = Self::resolve_webdav_url(profile, remote_subpath);
        let bytes = fs::read(local_file).map_err(|e| e.to_string())?;
        let mut req = ureq::put(&url);
        if !profile.username.is_empty() {
            let cred = format!("{}:{}", profile.username, profile.password_or_secret);
            req = req.set(
                "Authorization",
                &format!("Basic {}", Self::base_encode64(cred.as_bytes())),
            );
        }
        req.send_bytes(&bytes)
            .map_err(|e| format!("WebDAV Upload Fehler: {}", e))?;
        Ok(())
    }

    pub fn webdav_download(
        profile: &RemoteConnectionProfile,
        remote_subpath: &str,
        local_dest: &Path,
    ) -> Result<u64, String> {
        let url = Self::resolve_webdav_url(profile, remote_subpath);
        let mut req = ureq::get(&url);
        if !profile.username.is_empty() {
            let cred = format!("{}:{}", profile.username, profile.password_or_secret);
            req = req.set(
                "Authorization",
                &format!("Basic {}", Self::base_encode64(cred.as_bytes())),
            );
        }
        let resp = req
            .call()
            .map_err(|e| format!("WebDAV Download Fehler: {}", e))?;
        let mut bytes = Vec::new();
        resp.into_reader()
            .read_to_end(&mut bytes)
            .map_err(|e| e.to_string())?;
        fs::write(local_dest, &bytes).map_err(|e| e.to_string())?;
        Ok(bytes.len() as u64)
    }

    pub fn s3_sign_headers(
        method: &str,
        host: &str,
        canonical_uri: &str,
        canonical_query: &str,
        payload: &[u8],
        access_key: &str,
        secret_key: &str,
        region: &str,
    ) -> (String, String, String) {
        let now = Utc::now();
        let amz_date = now.format("%Y%m%dT%H%M%SZ").to_string();
        let date_stamp = now.format("%Y%m%d").to_string();
        let payload_hash = Self::hex_lower(&Sha256::digest(payload));

        let canonical_headers = format!(
            "host:{}\nx-amz-content-sha256:{}\nx-amz-date:{}\n",
            host, payload_hash, amz_date
        );
        let signed_headers = "host;x-amz-content-sha256;x-amz-date";
        let canonical_request = format!(
            "{}\n{}\n{}\n{}\n{}\n{}",
            method, canonical_uri, canonical_query, canonical_headers, signed_headers, payload_hash
        );

        let reg = if region.trim().is_empty() {
            "us-east-1"
        } else {
            region.trim()
        };
        let credential_scope = format!("{}/{}/s3/aws4_request", date_stamp, reg);
        let string_to_sign = format!(
            "AWS4-HMAC-SHA256\n{}\n{}\n{}",
            amz_date,
            credential_scope,
            Self::hex_lower(&Sha256::digest(canonical_request.as_bytes()))
        );

        let k_date = Self::hmac_sha256(
            format!("AWS4{}", secret_key).as_bytes(),
            date_stamp.as_bytes(),
        );
        let k_region = Self::hmac_sha256(&k_date, reg.as_bytes());
        let k_service = Self::hmac_sha256(&k_region, b"s3");
        let k_signing = Self::hmac_sha256(&k_service, b"aws4_request");
        let signature = Self::hex_lower(&Self::hmac_sha256(&k_signing, string_to_sign.as_bytes()));

        let auth_header = format!(
            "AWS4-HMAC-SHA256 Credential={}/{}, SignedHeaders={}, Signature={}",
            access_key, credential_scope, signed_headers, signature
        );
        (auth_header, amz_date, payload_hash)
    }

    pub fn s3_list(
        profile: &RemoteConnectionProfile,
        prefix: &str,
    ) -> Result<Vec<RemoteEntryDto>, String> {
        let bucket = if profile.bucket.trim().is_empty() {
            "default"
        } else {
            profile.bucket.trim()
        };
        let host = if profile.host.trim().is_empty() {
            "s3.amazonaws.com".to_string()
        } else if profile.port != 80 && profile.port != 443 && profile.port > 0 {
            format!("{}:{}", profile.host.trim(), profile.port)
        } else {
            profile.host.trim().to_string()
        };
        let scheme = if profile.use_tls { "https" } else { "http" };
        let clean_prefix = prefix.trim_matches('/');
        let canonical_uri = format!("/{}", bucket);
        let canonical_query = if clean_prefix.is_empty() {
            "delimiter=%2F&list-type=2".to_string()
        } else {
            format!("delimiter=%2F&list-type=2&prefix={}%2F", clean_prefix)
        };

        let (auth, amz_date, payload_hash) = Self::s3_sign_headers(
            "GET",
            &host,
            &canonical_uri,
            &canonical_query,
            b"",
            &profile.username,
            &profile.password_or_secret,
            &profile.region,
        );

        let url = format!("{}://{}{}?{}", scheme, host, canonical_uri, canonical_query);
        let resp = ureq::get(&url)
            .set("Host", &host)
            .set("x-amz-date", &amz_date)
            .set("x-amz-content-sha256", &payload_hash)
            .set("Authorization", &auth)
            .call()
            .map_err(|e| format!("S3 ListObjectsV2 Fehler: {}", e))?;
        let xml = resp.into_string().map_err(|e| e.to_string())?;
        Ok(Self::parse_s3_list_xml(&xml))
    }

    pub fn parse_s3_list_xml(xml: &str) -> Vec<RemoteEntryDto> {
        let mut entries = Vec::new();
        let re_prefix =
            Regex::new(r"(?is)<CommonPrefixes>.*?<Prefix>(.*?)</Prefix>.*?</CommonPrefixes>")
                .unwrap();
        let re_content = Regex::new(r"(?is)<Contents>(.*?)</Contents>").unwrap();
        let re_key = Regex::new(r"(?is)<Key>(.*?)</Key>").unwrap();
        let re_size = Regex::new(r"(?is)<Size>(\d+)</Size>").unwrap();
        let re_mod = Regex::new(r"(?is)<LastModified>(.*?)</LastModified>").unwrap();

        for cap in re_prefix.captures_iter(xml) {
            let pref = cap[1].trim().to_string();
            let trimmed = pref.trim_end_matches('/');
            let name = trimmed.rsplit('/').next().unwrap_or(trimmed).to_string();
            entries.push(RemoteEntryDto {
                name,
                full_path: pref,
                size_bytes: 0,
                is_directory: true,
                modified_text: "S3 Prefix".to_string(),
            });
        }

        for cap in re_content.captures_iter(xml) {
            let block = &cap[1];
            let key = re_key
                .captures(block)
                .map(|c| c[1].trim().to_string())
                .unwrap_or_default();
            if key.is_empty() || key.ends_with('/') {
                continue;
            }
            let name = key.rsplit('/').next().unwrap_or(&key).to_string();
            let size_bytes = re_size
                .captures(block)
                .and_then(|c| c[1].parse::<u64>().ok())
                .unwrap_or(0);
            let modified_text = re_mod
                .captures(block)
                .map(|c| c[1].trim().to_string())
                .unwrap_or_else(|| "-".to_string());
            entries.push(RemoteEntryDto {
                name,
                full_path: key,
                size_bytes,
                is_directory: false,
                modified_text,
            });
        }
        entries
    }
}
