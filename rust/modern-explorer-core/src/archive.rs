use crate::filesystem::FileEntry;
use chrono::{DateTime, Local, TimeZone};
use flate2::read::GzDecoder;
use flate2::write::GzEncoder;
use flate2::Compression;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use walkdir::WalkDir;
use zip::write::SimpleFileOptions;
use zip::{ZipArchive, ZipWriter};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ArchiveEntryInfo {
    pub full_name: String,
    pub name: String,
    pub size_bytes: u64,
    pub compressed_bytes: u64,
    pub is_directory: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ArchiveFormat {
    Zip,
    TarGz,
}

pub struct ArchiveService;

impl ArchiveService {
    pub const VIRTUAL_SCHEME_PREFIX: &'static str = "archive://";

    pub fn is_supported_archive(path: &Path) -> bool {
        let lower = path.to_string_lossy().to_ascii_lowercase();
        lower.ends_with(".zip")
            || lower.ends_with(".jar")
            || lower.ends_with(".nupkg")
            || lower.ends_with(".tar.gz")
            || lower.ends_with(".tgz")
    }

    pub fn is_virtual_archive_path(path: &str) -> bool {
        path.to_ascii_lowercase().starts_with(Self::VIRTUAL_SCHEME_PREFIX)
    }

    pub fn build_virtual_archive_path(archive_file: &str, inner_relative_path: &str) -> String {
        let clean_inner = inner_relative_path.replace('\\', "/");
        let trimmed_inner = clean_inner.trim_matches('/');
        if trimmed_inner.is_empty() {
            format!("{}{}", Self::VIRTUAL_SCHEME_PREFIX, archive_file)
        } else {
            format!("{}{}|{}", Self::VIRTUAL_SCHEME_PREFIX, archive_file, trimmed_inner)
        }
    }

    pub fn parse_virtual_archive_path(virtual_path: &str) -> Option<(String, String)> {
        if !Self::is_virtual_archive_path(virtual_path) {
            return None;
        }
        let payload = &virtual_path[Self::VIRTUAL_SCHEME_PREFIX.len()..];
        let (archive_file, inner_rel) = match payload.find('|') {
            Some(idx) => (&payload[..idx], &payload[idx + 1..]),
            None => (payload, ""),
        };
        let clean_inner = inner_rel.replace('\\', "/");
        let trimmed_inner = clean_inner.trim_matches('/').to_string();
        if archive_file.trim().is_empty() {
            None
        } else {
            Some((archive_file.to_string(), trimmed_inner))
        }
    }

    pub fn get_parent_path_for_virtual_archive(virtual_path: &str) -> String {
        if let Some((archive_file, inner)) = Self::parse_virtual_archive_path(virtual_path) {
            if inner.is_empty() {
                Path::new(&archive_file)
                    .parent()
                    .map(|p| p.to_string_lossy().to_string())
                    .unwrap_or(archive_file)
            } else {
                match inner.rfind('/') {
                    Some(idx) => Self::build_virtual_archive_path(&archive_file, &inner[..idx]),
                    None => Self::build_virtual_archive_path(&archive_file, ""),
                }
            }
        } else {
            String::new()
        }
    }

    pub fn list_entries(archive_path: &Path) -> Result<Vec<ArchiveEntryInfo>, String> {
        let lower = archive_path.to_string_lossy().to_ascii_lowercase();
        if lower.ends_with(".tar.gz") || lower.ends_with(".tgz") {
            let f = File::open(archive_path).map_err(|e| e.to_string())?;
            let gz = GzDecoder::new(f);
            let mut tar = tar::Archive::new(gz);
            let mut entries = Vec::new();
            for item in tar.entries().map_err(|e| e.to_string())? {
                if let Ok(entry) = item {
                    let path_str = entry
                        .path()
                        .map(|p| p.to_string_lossy().to_string())
                        .unwrap_or_default();
                    let name = Path::new(&path_str)
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_else(|| path_str.clone());
                    let size = entry.size();
                    let is_dir = entry.header().entry_type().is_dir();
                    entries.push(ArchiveEntryInfo {
                        full_name: path_str,
                        name,
                        size_bytes: size,
                        compressed_bytes: size,
                        is_directory: is_dir,
                    });
                }
            }
            return Ok(entries);
        }

        let f = File::open(archive_path).map_err(|e| e.to_string())?;
        let mut zip = ZipArchive::new(f).map_err(|e| e.to_string())?;
        let mut entries = Vec::new();
        for i in 0..zip.len() {
            if let Ok(item) = zip.by_index(i) {
                let full_name = item.name().to_string();
                let trimmed = full_name.trim_end_matches('/');
                let name = Path::new(trimmed)
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| trimmed.to_string());
                entries.push(ArchiveEntryInfo {
                    full_name,
                    name,
                    size_bytes: item.size(),
                    compressed_bytes: item.compressed_size(),
                    is_directory: item.is_dir(),
                });
            }
        }
        Ok(entries)
    }

    pub fn pack_archive(
        source_paths: &[String],
        destination_archive: &Path,
        format: ArchiveFormat,
    ) -> Result<usize, String> {
        match format {
            ArchiveFormat::Zip => Self::pack_zip(source_paths, destination_archive),
            ArchiveFormat::TarGz => Self::pack_tar_gz(source_paths, destination_archive),
        }
    }

    fn pack_zip(source_paths: &[String], destination_archive: &Path) -> Result<usize, String> {
        let f = File::create(destination_archive).map_err(|e| e.to_string())?;
        let mut zip = ZipWriter::new(f);
        let opts = SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
        let mut count = 0usize;
        let mut buf = Vec::new();

        for sp in source_paths {
            let src = Path::new(sp);
            if src.is_file() {
                let name = src
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| "file".to_string());
                zip.start_file(name, opts).map_err(|e| e.to_string())?;
                buf.clear();
                File::open(src)
                    .and_then(|mut file| file.read_to_end(&mut buf))
                    .map_err(|e| e.to_string())?;
                zip.write_all(&buf).map_err(|e| e.to_string())?;
                count += 1;
            } else if src.is_dir() {
                let parent = src.parent().unwrap_or(src);
                for entry in WalkDir::new(src).into_iter().filter_map(|e| e.ok()) {
                    if entry.file_type().is_file() {
                        let rel = entry
                            .path()
                            .strip_prefix(parent)
                            .unwrap_or(entry.path())
                            .to_string_lossy()
                            .replace('\\', "/");
                        zip.start_file(rel, opts).map_err(|e| e.to_string())?;
                        buf.clear();
                        File::open(entry.path())
                            .and_then(|mut file| file.read_to_end(&mut buf))
                            .map_err(|e| e.to_string())?;
                        zip.write_all(&buf).map_err(|e| e.to_string())?;
                        count += 1;
                    }
                }
            }
        }
        zip.finish().map_err(|e| e.to_string())?;
        Ok(count)
    }

    fn pack_tar_gz(source_paths: &[String], destination_archive: &Path) -> Result<usize, String> {
        let f = File::create(destination_archive).map_err(|e| e.to_string())?;
        let enc = GzEncoder::new(f, Compression::default());
        let mut tar = tar::Builder::new(enc);
        let mut count = 0usize;

        for sp in source_paths {
            let src = Path::new(sp);
            if src.is_file() {
                if let Some(name) = src.file_name() {
                    tar.append_path_with_name(src, name)
                        .map_err(|e| e.to_string())?;
                    count += 1;
                }
            } else if src.is_dir() {
                if let Some(name) = src.file_name() {
                    tar.append_dir_all(name, src).map_err(|e| e.to_string())?;
                    count += 1;
                }
            }
        }
        tar.finish().map_err(|e| e.to_string())?;
        Ok(count)
    }

    pub fn extract_all(archive_path: &Path, destination_dir: &Path) -> Result<usize, String> {
        fs::create_dir_all(destination_dir).map_err(|e| e.to_string())?;
        let lower = archive_path.to_string_lossy().to_ascii_lowercase();
        if lower.ends_with(".tar.gz") || lower.ends_with(".tgz") {
            let f = File::open(archive_path).map_err(|e| e.to_string())?;
            let gz = GzDecoder::new(f);
            let mut tar = tar::Archive::new(gz);
            tar.unpack(destination_dir).map_err(|e| e.to_string())?;
            return Ok(1);
        }

        let f = File::open(archive_path).map_err(|e| e.to_string())?;
        let mut zip = ZipArchive::new(f).map_err(|e| e.to_string())?;
        let mut count = 0usize;
        for i in 0..zip.len() {
            let mut file = zip.by_index(i).map_err(|e| e.to_string())?;
            let Some(enclosed) = file.enclosed_name().map(|p| p.to_path_buf()) else {
                continue;
            };
            let outpath = destination_dir.join(enclosed);
            if file.is_dir() {
                fs::create_dir_all(&outpath).map_err(|e| e.to_string())?;
            } else {
                if let Some(p) = outpath.parent() {
                    fs::create_dir_all(p).map_err(|e| e.to_string())?;
                }
                let mut outfile = File::create(&outpath).map_err(|e| e.to_string())?;
                io::copy(&mut file, &mut outfile).map_err(|e| e.to_string())?;
                count += 1;
            }
        }
        Ok(count)
    }

    pub fn list_virtual_archive_entries(virtual_path: &str) -> Result<Vec<FileEntry>, String> {
        let (archive_file, current_prefix) = Self::parse_virtual_archive_path(virtual_path)
            .ok_or_else(|| "Ungültiger virtueller Archiv-Pfad".to_string())?;

        let arch_path = Path::new(&archive_file);
        if !arch_path.is_file() {
            return Err(format!("Archivdatei nicht gefunden: {}", archive_file));
        }

        let fallback_time = fs::metadata(arch_path)
            .and_then(|m| m.modified())
            .ok()
            .map(DateTime::<Local>::from)
            .unwrap_or_else(Local::now);

        let prefix_with_slash = if current_prefix.is_empty() {
            String::new()
        } else {
            format!("{}/", current_prefix.to_ascii_lowercase())
        };

        let mut seen_folders = HashSet::new();
        let mut entries = Vec::new();
        let lower = archive_file.to_ascii_lowercase();

        if lower.ends_with(".tar.gz") || lower.ends_with(".tgz") {
            let f = File::open(arch_path).map_err(|e| e.to_string())?;
            let gz = GzDecoder::new(f);
            let mut tar = tar::Archive::new(gz);

            for item in tar.entries().map_err(|e| e.to_string())? {
                let Ok(entry) = item else { continue; };
                let Ok(path) = entry.path() else { continue; };
                let norm = path.to_string_lossy().replace('\\', "/");
                let norm_key = norm.trim_matches('/').to_string();
                if norm_key.is_empty() {
                    continue;
                }

                let norm_lower = norm_key.to_ascii_lowercase();
                let rest = if !prefix_with_slash.is_empty() {
                    if !norm_lower.starts_with(&prefix_with_slash) {
                        continue;
                    }
                    &norm_key[prefix_with_slash.len()..]
                } else {
                    &norm_key[..]
                };

                if rest.is_empty() {
                    continue;
                }

                let entry_time = DateTime::from_timestamp(entry.header().mtime().unwrap_or(0) as i64, 0)
                    .map(DateTime::<Local>::from)
                    .unwrap_or(fallback_time);

                if let Some(slash_idx) = rest.find('/') {
                    let folder_name = &rest[..slash_idx];
                    if seen_folders.insert(folder_name.to_ascii_lowercase()) {
                        let child_inner = if current_prefix.is_empty() {
                            folder_name.to_string()
                        } else {
                            format!("{}/{}", current_prefix, folder_name)
                        };
                        entries.push(FileEntry {
                            full_path: Self::build_virtual_archive_path(&archive_file, &child_inner),
                            name: folder_name.to_string(),
                            extension: String::new(),
                            size_bytes: 0,
                            is_directory: true,
                            is_hidden: false,
                            is_readonly: true,
                            modified: entry_time,
                            created: entry_time,
                            relative_path: None,
                            allocated_tree_size: None,
                        });
                    }
                } else if entry.header().entry_type().is_dir() {
                    if seen_folders.insert(rest.to_ascii_lowercase()) {
                        let child_inner = if current_prefix.is_empty() {
                            rest.to_string()
                        } else {
                            format!("{}/{}", current_prefix, rest)
                        };
                        entries.push(FileEntry {
                            full_path: Self::build_virtual_archive_path(&archive_file, &child_inner),
                            name: rest.to_string(),
                            extension: String::new(),
                            size_bytes: 0,
                            is_directory: true,
                            is_hidden: false,
                            is_readonly: true,
                            modified: entry_time,
                            created: entry_time,
                            relative_path: None,
                            allocated_tree_size: None,
                        });
                    }
                } else {
                    let child_inner = if current_prefix.is_empty() {
                        rest.to_string()
                    } else {
                        format!("{}/{}", current_prefix, rest)
                    };
                    let ext = Path::new(rest)
                        .extension()
                        .map(|e| format!(".{}", e.to_string_lossy()))
                        .unwrap_or_default();
                    entries.push(FileEntry {
                        full_path: Self::build_virtual_archive_path(&archive_file, &child_inner),
                        name: rest.to_string(),
                        extension: ext,
                        size_bytes: entry.size(),
                        is_directory: false,
                        is_hidden: false,
                        is_readonly: true,
                        modified: entry_time,
                        created: entry_time,
                        relative_path: None,
                        allocated_tree_size: None,
                    });
                }
            }
            return Ok(entries);
        }

        let f = File::open(arch_path).map_err(|e| e.to_string())?;
        let mut zip = ZipArchive::new(f).map_err(|e| e.to_string())?;

        for i in 0..zip.len() {
            let Ok(item) = zip.by_index(i) else { continue; };
            let raw_name = item.name().replace('\\', "/");
            let norm_key = raw_name.trim_matches('/').to_string();
            if norm_key.is_empty() {
                continue;
            }

            let norm_lower = norm_key.to_ascii_lowercase();
            let rest = if !prefix_with_slash.is_empty() {
                if !norm_lower.starts_with(&prefix_with_slash) {
                    continue;
                }
                &norm_key[prefix_with_slash.len()..]
            } else {
                &norm_key[..]
            };

            if rest.is_empty() {
                continue;
            }

            let entry_time = item
                .last_modified()
                .and_then(|dt| {
                    chrono::NaiveDate::from_ymd_opt(dt.year() as i32, dt.month() as u32, dt.day() as u32)
                        .and_then(|d| d.and_hms_opt(dt.hour() as u32, dt.minute() as u32, dt.second() as u32))
                        .and_then(|naive| Local.from_local_datetime(&naive).single())
                })
                .unwrap_or(fallback_time);

            if let Some(slash_idx) = rest.find('/') {
                let folder_name = &rest[..slash_idx];
                if seen_folders.insert(folder_name.to_ascii_lowercase()) {
                    let child_inner = if current_prefix.is_empty() {
                        folder_name.to_string()
                    } else {
                        format!("{}/{}", current_prefix, folder_name)
                    };
                    entries.push(FileEntry {
                        full_path: Self::build_virtual_archive_path(&archive_file, &child_inner),
                        name: folder_name.to_string(),
                        extension: String::new(),
                        size_bytes: 0,
                        is_directory: true,
                        is_hidden: false,
                        is_readonly: true,
                        modified: entry_time,
                        created: entry_time,
                        relative_path: None,
                        allocated_tree_size: None,
                    });
                }
            } else if item.is_dir() {
                if seen_folders.insert(rest.to_ascii_lowercase()) {
                    let child_inner = if current_prefix.is_empty() {
                        rest.to_string()
                    } else {
                        format!("{}/{}", current_prefix, rest)
                    };
                    entries.push(FileEntry {
                        full_path: Self::build_virtual_archive_path(&archive_file, &child_inner),
                        name: rest.to_string(),
                        extension: String::new(),
                        size_bytes: 0,
                        is_directory: true,
                        is_hidden: false,
                        is_readonly: true,
                        modified: entry_time,
                        created: entry_time,
                        relative_path: None,
                        allocated_tree_size: None,
                    });
                }
            } else {
                let child_inner = if current_prefix.is_empty() {
                    rest.to_string()
                } else {
                    format!("{}/{}", current_prefix, rest)
                };
                let ext = Path::new(rest)
                    .extension()
                    .map(|e| format!(".{}", e.to_string_lossy()))
                    .unwrap_or_default();
                entries.push(FileEntry {
                    full_path: Self::build_virtual_archive_path(&archive_file, &child_inner),
                    name: rest.to_string(),
                    extension: ext,
                    size_bytes: item.size(),
                    is_directory: false,
                    is_hidden: false,
                    is_readonly: true,
                    modified: entry_time,
                    created: entry_time,
                    relative_path: None,
                    allocated_tree_size: None,
                });
            }
        }

        Ok(entries)
    }

    pub fn extract_single_entry_to_temp(archive_file: &str, inner_path: &str) -> Result<PathBuf, String> {
        let arch_path = Path::new(archive_file);
        if !arch_path.is_file() {
            return Err(format!("Archivdatei nicht gefunden: {}", archive_file));
        }

        let temp_dir = std::env::temp_dir().join("ModernExplorer").join("archive_cache");
        fs::create_dir_all(&temp_dir).map_err(|e| e.to_string())?;

        let safe_name = Path::new(inner_path)
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_else(|| "extracted_file".to_string());
        let target_file = temp_dir.join(format!("{}_{}", std::process::id(), safe_name));

        let norm_target = inner_path.replace('\\', "/").trim_matches('/').to_ascii_lowercase();
        let lower = archive_file.to_ascii_lowercase();

        if lower.ends_with(".tar.gz") || lower.ends_with(".tgz") {
            let f = File::open(arch_path).map_err(|e| e.to_string())?;
            let gz = GzDecoder::new(f);
            let mut tar = tar::Archive::new(gz);

            for item in tar.entries().map_err(|e| e.to_string())? {
                let Ok(mut entry) = item else { continue; };
                let Ok(path) = entry.path() else { continue; };
                let norm = path.to_string_lossy().replace('\\', "/").trim_matches('/').to_ascii_lowercase();
                if norm == norm_target {
                    let mut outfile = File::create(&target_file).map_err(|e| e.to_string())?;
                    io::copy(&mut entry, &mut outfile).map_err(|e| e.to_string())?;
                    return Ok(target_file);
                }
            }
            return Err(format!("Eintrag '{}' nicht im Tar-Archiv gefunden", inner_path));
        }

        let f = File::open(arch_path).map_err(|e| e.to_string())?;
        let mut zip = ZipArchive::new(f).map_err(|e| e.to_string())?;

        for i in 0..zip.len() {
            let Ok(mut file) = zip.by_index(i) else { continue; };
            let raw_name = file.name().replace('\\', "/");
            let norm = raw_name.trim_matches('/').to_ascii_lowercase();
            if norm == norm_target {
                let mut outfile = File::create(&target_file).map_err(|e| e.to_string())?;
                io::copy(&mut file, &mut outfile).map_err(|e| e.to_string())?;
                return Ok(target_file);
            }
        }

        Err(format!("Eintrag '{}' nicht im ZIP-Archiv gefunden", inner_path))
    }
}

