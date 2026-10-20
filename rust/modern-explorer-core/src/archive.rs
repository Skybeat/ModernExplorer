use crate::filesystem::FileEntry;
use chrono::{DateTime, Local};
use flate2::read::GzDecoder;
use flate2::write::GzEncoder;
use flate2::Compression;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fs::{self, File};
use std::io::{self, Cursor, Read, Write};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ArchiveFormat {
    Zip,
    TarGz,
    SevenZ,
    TarZst,
    TarXz,
}

impl ArchiveFormat {
    pub fn from_str_or_path(fmt: &str, dest: &Path) -> Self {
        let f = fmt.trim().to_ascii_lowercase();
        let d = dest.to_string_lossy().to_ascii_lowercase();
        if f.contains("7z") || d.ends_with(".7z") {
            Self::SevenZ
        } else if f.contains("zst") || d.ends_with(".tar.zst") || d.ends_with(".tzst") {
            Self::TarZst
        } else if f.contains("xz") || d.ends_with(".tar.xz") || d.ends_with(".txz") {
            Self::TarXz
        } else if f.contains("tar") || f.contains("gz") || d.ends_with(".tar.gz") || d.ends_with(".tgz") {
            Self::TarGz
        } else {
            Self::Zip
        }
    }
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
            || lower.ends_with(".7z")
            || lower.ends_with(".tar.zst")
            || lower.ends_with(".tzst")
            || lower.ends_with(".tar.xz")
            || lower.ends_with(".txz")
            || lower.ends_with(".rar")
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
            format!(
                "{}{}|{}",
                Self::VIRTUAL_SCHEME_PREFIX,
                archive_file,
                trimmed_inner
            )
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

    fn decode_tar_bytes(archive_path: &Path) -> Result<Vec<u8>, String> {
        let lower = archive_path.to_string_lossy().to_ascii_lowercase();
        if lower.ends_with(".tar.gz") || lower.ends_with(".tgz") {
            let f = File::open(archive_path).map_err(|e| e.to_string())?;
            let mut gz = GzDecoder::new(f);
            let mut out = Vec::new();
            gz.read_to_end(&mut out).map_err(|e| e.to_string())?;
            Ok(out)
        } else if lower.ends_with(".tar.zst") || lower.ends_with(".tzst") {
            let f = File::open(archive_path).map_err(|e| e.to_string())?;
            let mut dec = ruzstd::StreamingDecoder::new(f).map_err(|e| format!("{:?}", e))?;
            let mut out = Vec::new();
            dec.read_to_end(&mut out).map_err(|e| e.to_string())?;
            Ok(out)
        } else if lower.ends_with(".tar.xz") || lower.ends_with(".txz") {
            let f = File::open(archive_path).map_err(|e| e.to_string())?;
            let mut buf_reader = io::BufReader::new(f);
            let mut out = Vec::new();
            lzma_rs::xz_decompress(&mut buf_reader, &mut out).map_err(|e| format!("{:?}", e))?;
            Ok(out)
        } else {
            Err("Nicht unterstütztes Tar-Format".to_string())
        }
    }

    fn is_tar_variant(lower: &str) -> bool {
        lower.ends_with(".tar.gz")
            || lower.ends_with(".tgz")
            || lower.ends_with(".tar.zst")
            || lower.ends_with(".tzst")
            || lower.ends_with(".tar.xz")
            || lower.ends_with(".txz")
    }

    pub fn list_entries(archive_path: &Path) -> Result<Vec<ArchiveEntryInfo>, String> {
        let lower = archive_path.to_string_lossy().to_ascii_lowercase();
        if Self::is_tar_variant(&lower) {
            let raw_tar = Self::decode_tar_bytes(archive_path)?;
            let mut tar = tar::Archive::new(Cursor::new(raw_tar));
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

        if lower.ends_with(".7z") {
            let f = File::open(archive_path).map_err(|e| e.to_string())?;
            let len = f.metadata().map_err(|e| e.to_string())?.len();
            let reader = sevenz_rust::SevenZReader::new(f, len, sevenz_rust::Password::empty())
                .map_err(|e| e.to_string())?;
            let mut entries = Vec::new();
            for entry in &reader.archive().files {
                let full_name = entry.name().to_string();
                let trimmed = full_name.trim_end_matches('/');
                let name = Path::new(trimmed)
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| trimmed.to_string());
                entries.push(ArchiveEntryInfo {
                    full_name,
                    name,
                    size_bytes: entry.size(),
                    compressed_bytes: entry.compressed_size,
                    is_directory: entry.is_directory(),
                });
            }
            return Ok(entries);
        }

        if lower.ends_with(".rar") {
            return Self::list_rar_entries(archive_path);
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

    fn list_rar_entries(archive_path: &Path) -> Result<Vec<ArchiveEntryInfo>, String> {
        let mut cmd = Command::new("tar");
        cmd.arg("-tf").arg(archive_path);
        #[cfg(target_os = "windows")]
        {
            use std::os::windows::process::CommandExt;
            cmd.creation_flags(0x08000000);
        }
        if let Ok(out) = cmd.output() {
            if out.status.success() {
                let text = String::from_utf8_lossy(&out.stdout);
                let mut entries = Vec::new();
                for line in text.lines() {
                    let full_name = line.trim().replace('\\', "/");
                    if full_name.is_empty() {
                        continue;
                    }
                    let is_dir = full_name.ends_with('/');
                    let trimmed = full_name.trim_end_matches('/');
                    let name = Path::new(trimmed)
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_else(|| trimmed.to_string());
                    entries.push(ArchiveEntryInfo {
                        full_name,
                        name,
                        size_bytes: 0,
                        compressed_bytes: 0,
                        is_directory: is_dir,
                    });
                }
                return Ok(entries);
            }
        }
        Err("RAR-Archiv benötigt bsdtar (Windows 10/11 tar.exe) oder unrar".to_string())
    }

    pub fn pack_archive(
        source_paths: &[String],
        destination_archive: &Path,
        format: ArchiveFormat,
    ) -> Result<usize, String> {
        match format {
            ArchiveFormat::Zip => Self::pack_zip(source_paths, destination_archive),
            ArchiveFormat::TarGz => Self::pack_tar_gz(source_paths, destination_archive),
            ArchiveFormat::SevenZ => Self::pack_7z(source_paths, destination_archive),
            ArchiveFormat::TarZst => Self::pack_tar_zst(source_paths, destination_archive),
            ArchiveFormat::TarXz => Self::pack_tar_xz(source_paths, destination_archive),
        }
    }

    fn pack_zip(source_paths: &[String], destination_archive: &Path) -> Result<usize, String> {
        let f = File::create(destination_archive).map_err(|e| e.to_string())?;
        let mut zip = ZipWriter::new(f);
        let opts =
            SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
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

    fn build_tar_in_memory(source_paths: &[String]) -> Result<(Vec<u8>, usize), String> {
        let mut raw = Vec::new();
        let mut count = 0usize;
        {
            let mut tar = tar::Builder::new(&mut raw);
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
        }
        Ok((raw, count))
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

    fn pack_tar_xz(source_paths: &[String], destination_archive: &Path) -> Result<usize, String> {
        let (raw_tar, count) = Self::build_tar_in_memory(source_paths)?;
        let mut f = File::create(destination_archive).map_err(|e| e.to_string())?;
        let mut reader = Cursor::new(raw_tar);
        lzma_rs::xz_compress(&mut reader, &mut f).map_err(|e| format!("{:?}", e))?;
        Ok(count)
    }

    fn pack_tar_zst(source_paths: &[String], destination_archive: &Path) -> Result<usize, String> {
        let (raw_tar, count) = Self::build_tar_in_memory(source_paths)?;
        let mut out = File::create(destination_archive).map_err(|e| e.to_string())?;
        // RFC 8878 Zstandard frame (Magic + FHD + Window Descriptor + Raw Blocks)
        out.write_all(&[0x28, 0xB5, 0x2F, 0xFD, 0x00, 0x58])
            .map_err(|e| e.to_string())?;
        if raw_tar.is_empty() {
            out.write_all(&[0x01, 0x00, 0x00])
                .map_err(|e| e.to_string())?;
        } else {
            let chunks: Vec<&[u8]> = raw_tar.chunks(65536).collect();
            for (idx, chunk) in chunks.iter().enumerate() {
                let is_last = if idx + 1 == chunks.len() { 1u32 } else { 0u32 };
                let block_header = is_last | ((chunk.len() as u32) << 3);
                let hdr_bytes = [
                    (block_header & 0xFF) as u8,
                    ((block_header >> 8) & 0xFF) as u8,
                    ((block_header >> 16) & 0xFF) as u8,
                ];
                out.write_all(&hdr_bytes).map_err(|e| e.to_string())?;
                out.write_all(chunk).map_err(|e| e.to_string())?;
            }
        }
        Ok(count)
    }

    fn pack_7z(source_paths: &[String], destination_archive: &Path) -> Result<usize, String> {
        let f = File::create(destination_archive).map_err(|e| e.to_string())?;
        let mut sz = sevenz_rust::SevenZWriter::new(f).map_err(|e| e.to_string())?;
        let mut count = 0usize;

        for sp in source_paths {
            let src = Path::new(sp);
            if src.is_file() {
                let name = src
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| "file".to_string());
                let data = fs::read(src).map_err(|e| e.to_string())?;
                let entry = sevenz_rust::SevenZArchiveEntry::from_path(src, name);
                sz.push_archive_entry(entry, Some(Cursor::new(data)))
                    .map_err(|e| e.to_string())?;
                count += 1;
            } else if src.is_dir() {
                let parent = src.parent().unwrap_or(src);
                for walk_entry in WalkDir::new(src).into_iter().filter_map(|e| e.ok()) {
                    if walk_entry.file_type().is_file() {
                        let rel = walk_entry
                            .path()
                            .strip_prefix(parent)
                            .unwrap_or(walk_entry.path())
                            .to_string_lossy()
                            .replace('\\', "/");
                        let data = fs::read(walk_entry.path()).map_err(|e| e.to_string())?;
                        let entry =
                            sevenz_rust::SevenZArchiveEntry::from_path(walk_entry.path(), rel);
                        sz.push_archive_entry(entry, Some(Cursor::new(data)))
                            .map_err(|e| e.to_string())?;
                        count += 1;
                    }
                }
            }
        }
        sz.finish().map_err(|e| e.to_string())?;
        Ok(count)
    }

    pub fn extract_all(archive_path: &Path, destination_dir: &Path) -> Result<usize, String> {
        fs::create_dir_all(destination_dir).map_err(|e| e.to_string())?;
        let lower = archive_path.to_string_lossy().to_ascii_lowercase();
        if Self::is_tar_variant(&lower) {
            let raw_tar = Self::decode_tar_bytes(archive_path)?;
            let mut tar = tar::Archive::new(Cursor::new(raw_tar));
            tar.unpack(destination_dir).map_err(|e| e.to_string())?;
            return Ok(1);
        }

        if lower.ends_with(".7z") {
            sevenz_rust::decompress_file(archive_path, destination_dir)
                .map_err(|e| e.to_string())?;
            return Ok(1);
        }

        if lower.ends_with(".rar") {
            let mut cmd = Command::new("tar");
            cmd.arg("-xf").arg(archive_path).arg("-C").arg(destination_dir);
            #[cfg(target_os = "windows")]
            {
                use std::os::windows::process::CommandExt;
                cmd.creation_flags(0x08000000);
            }
            let status = cmd.status().map_err(|e| e.to_string())?;
            if status.success() {
                return Ok(1);
            }
            return Err("RAR-Entpacken fehlgeschlagen".to_string());
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

        let raw_entries = Self::list_entries(arch_path)?;
        let mut seen_folders = HashSet::new();
        let mut entries = Vec::new();

        for item in raw_entries {
            let raw_name = item.full_name.replace('\\', "/");
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
                        modified: fallback_time,
                        created: fallback_time,
                        relative_path: None,
                        allocated_tree_size: None,
                    });
                }
            } else if item.is_directory {
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
                        modified: fallback_time,
                        created: fallback_time,
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
                    size_bytes: item.size_bytes,
                    is_directory: false,
                    is_hidden: false,
                    is_readonly: true,
                    modified: fallback_time,
                    created: fallback_time,
                    relative_path: None,
                    allocated_tree_size: None,
                });
            }
        }

        Ok(entries)
    }

    pub fn extract_single_entry_to_temp(
        archive_file: &str,
        inner_path: &str,
    ) -> Result<PathBuf, String> {
        let arch_path = Path::new(archive_file);
        if !arch_path.is_file() {
            return Err(format!("Archivdatei nicht gefunden: {}", archive_file));
        }

        let temp_dir = std::env::temp_dir()
            .join("ModernExplorer")
            .join("archive_cache");
        fs::create_dir_all(&temp_dir).map_err(|e| e.to_string())?;

        let safe_name = Path::new(inner_path)
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_else(|| "extracted_file".to_string());
        let target_file = temp_dir.join(format!("{}_{}", std::process::id(), safe_name));

        let norm_target = inner_path
            .replace('\\', "/")
            .trim_matches('/')
            .to_ascii_lowercase();
        let lower = archive_file.to_ascii_lowercase();

        if Self::is_tar_variant(&lower) {
            let raw_tar = Self::decode_tar_bytes(arch_path)?;
            let mut tar = tar::Archive::new(Cursor::new(raw_tar));

            for item in tar.entries().map_err(|e| e.to_string())? {
                let Ok(mut entry) = item else { continue };
                let Ok(path) = entry.path() else { continue };
                let norm = path
                    .to_string_lossy()
                    .replace('\\', "/")
                    .trim_matches('/')
                    .to_ascii_lowercase();
                if norm == norm_target {
                    let mut outfile = File::create(&target_file).map_err(|e| e.to_string())?;
                    io::copy(&mut entry, &mut outfile).map_err(|e| e.to_string())?;
                    return Ok(target_file);
                }
            }
            return Err(format!(
                "Eintrag '{}' nicht im Tar-Archiv gefunden",
                inner_path
            ));
        }

        if lower.ends_with(".7z") {
            let extract_sub = temp_dir.join(format!("7z_{}", std::process::id()));
            let _ = fs::remove_dir_all(&extract_sub);
            fs::create_dir_all(&extract_sub).map_err(|e| e.to_string())?;
            sevenz_rust::decompress_file(arch_path, &extract_sub).map_err(|e| e.to_string())?;
            let candidate = extract_sub.join(inner_path.replace('/', std::path::MAIN_SEPARATOR_STR));
            if candidate.is_file() {
                fs::copy(&candidate, &target_file).map_err(|e| e.to_string())?;
                let _ = fs::remove_dir_all(&extract_sub);
                return Ok(target_file);
            }
            let _ = fs::remove_dir_all(&extract_sub);
            return Err(format!("Eintrag '{}' nicht im 7z-Archiv gefunden", inner_path));
        }

        let f = File::open(arch_path).map_err(|e| e.to_string())?;
        let mut zip = ZipArchive::new(f).map_err(|e| e.to_string())?;

        for i in 0..zip.len() {
            let Ok(mut file) = zip.by_index(i) else {
                continue;
            };
            let raw_name = file.name().replace('\\', "/");
            let norm = raw_name.trim_matches('/').to_ascii_lowercase();
            if norm == norm_target {
                let mut outfile = File::create(&target_file).map_err(|e| e.to_string())?;
                io::copy(&mut file, &mut outfile).map_err(|e| e.to_string())?;
                return Ok(target_file);
            }
        }

        Err(format!(
            "Eintrag '{}' nicht im Archiv gefunden",
            inner_path
        ))
    }

    pub fn match_archive_entry_target(
        raw_entry_path: &str,
        _is_dir: bool,
        inner_paths: &[String],
        destination_dir: &Path,
    ) -> Option<PathBuf> {
        let entry_norm = raw_entry_path.replace('\\', "/");
        let entry_trimmed = entry_norm.trim_matches('/');
        if entry_trimmed.is_empty() {
            return None;
        }
        let entry_lower = entry_trimmed.to_ascii_lowercase();

        for item in inner_paths {
            let item_norm = item.replace('\\', "/");
            let item_trimmed = item_norm.trim_matches('/');
            let item_lower = item_trimmed.to_ascii_lowercase();

            if item_trimmed.is_empty() {
                let safe_rel: PathBuf = Path::new(entry_trimmed)
                    .components()
                    .filter(|c| matches!(c, std::path::Component::Normal(_)))
                    .collect();
                return Some(destination_dir.join(safe_rel));
            }

            if entry_lower == item_lower {
                let name = Path::new(entry_trimmed)
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| entry_trimmed.to_string());
                return Some(destination_dir.join(name));
            }

            let prefix = format!("{}/", item_lower);
            if entry_lower.starts_with(&prefix) {
                let folder_name = Path::new(item_trimmed)
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| item_trimmed.to_string());
                let rel_inside = &entry_trimmed[item_trimmed.len() + 1..];
                let safe_rel: PathBuf = Path::new(rel_inside)
                    .components()
                    .filter(|c| matches!(c, std::path::Component::Normal(_)))
                    .collect();
                return Some(destination_dir.join(folder_name).join(safe_rel));
            }
        }
        None
    }

    pub fn extract_virtual_entries(
        archive_path: &Path,
        inner_paths: &[String],
        destination_dir: &Path,
    ) -> Result<usize, String> {
        Self::extract_virtual_entries_with_progress(archive_path, inner_paths, destination_dir, None, None)
    }

    pub fn extract_virtual_entries_with_progress(
        archive_path: &Path,
        inner_paths: &[String],
        destination_dir: &Path,
        cancel_flag: Option<&Arc<AtomicBool>>,
        on_progress: Option<&dyn Fn(&str, usize, usize)>,
    ) -> Result<usize, String> {
        if !archive_path.is_file() {
            return Err(format!("Archivdatei nicht gefunden: {}", archive_path.display()));
        }
        if inner_paths.is_empty() {
            return Ok(0);
        }

        let is_cancelled = || -> bool {
            if let Some(cf) = cancel_flag {
                cf.load(Ordering::Relaxed)
            } else {
                false
            }
        };

        if is_cancelled() {
            return Err("Vorgang durch Benutzer abgebrochen".to_string());
        }

        fs::create_dir_all(destination_dir).map_err(|e| e.to_string())?;
        let lower = archive_path.to_string_lossy().to_ascii_lowercase();

        if lower.ends_with(".7z") {
            let temp_dir = std::env::temp_dir()
                .join("ModernExplorer")
                .join(format!("7z_ext_{}_{}", std::process::id(), chrono::Utc::now().timestamp_nanos_opt().unwrap_or(0)));
            let _ = fs::remove_dir_all(&temp_dir);
            fs::create_dir_all(&temp_dir).map_err(|e| e.to_string())?;

            if let Some(cb) = on_progress {
                cb("7z-Archiv wird dekomprimiert…", 0, 0);
            }

            let res = sevenz_rust::decompress_file(archive_path, &temp_dir);
            if let Err(e) = res {
                let _ = fs::remove_dir_all(&temp_dir);
                return Err(e.to_string());
            }

            if is_cancelled() {
                let _ = fs::remove_dir_all(&temp_dir);
                return Err("Vorgang durch Benutzer abgebrochen".to_string());
            }

            let all_entries: Vec<_> = WalkDir::new(&temp_dir).min_depth(1).into_iter().filter_map(|e| e.ok()).collect();
            let total_entries = all_entries.len();
            let mut count = 0usize;
            let mut extracted_targets = HashSet::new();

            for entry in all_entries {
                if is_cancelled() {
                    let _ = fs::remove_dir_all(&temp_dir);
                    return Err("Vorgang durch Benutzer abgebrochen".to_string());
                }
                let Ok(rel) = entry.path().strip_prefix(&temp_dir) else { continue };
                let rel_str = rel.to_string_lossy().replace('\\', "/");
                let is_dir = entry.file_type().is_dir();

                if let Some(target_path) = Self::match_archive_entry_target(&rel_str, is_dir, inner_paths, destination_dir) {
                    if !extracted_targets.insert(target_path.clone()) {
                        continue;
                    }
                    if is_dir {
                        let _ = fs::create_dir_all(&target_path);
                    } else {
                        if let Some(p) = target_path.parent() {
                            let _ = fs::create_dir_all(p);
                        }
                        if fs::copy(entry.path(), &target_path).is_ok() {
                            count += 1;
                            if let Some(cb) = on_progress {
                                cb(&rel_str, count, total_entries);
                            }
                        }
                    }
                }
            }

            let _ = fs::remove_dir_all(&temp_dir);
            return Ok(count.max(if inner_paths.is_empty() { 0 } else { 1 }));
        }

        if lower.ends_with(".rar") {
            let temp_dir = std::env::temp_dir()
                .join("ModernExplorer")
                .join(format!("rar_ext_{}_{}", std::process::id(), chrono::Utc::now().timestamp_nanos_opt().unwrap_or(0)));
            let _ = fs::remove_dir_all(&temp_dir);
            fs::create_dir_all(&temp_dir).map_err(|e| e.to_string())?;

            if let Some(cb) = on_progress {
                cb("RAR-Archiv wird dekomprimiert…", 0, 0);
            }

            let mut cmd = Command::new("tar");
            cmd.arg("-xf").arg(archive_path).arg("-C").arg(&temp_dir);
            #[cfg(target_os = "windows")]
            {
                use std::os::windows::process::CommandExt;
                cmd.creation_flags(0x08000000);
            }
            let status = cmd.status().map_err(|e| e.to_string())?;
            if !status.success() {
                let _ = fs::remove_dir_all(&temp_dir);
                return Err("RAR-Entpacken fehlgeschlagen".to_string());
            }

            if is_cancelled() {
                let _ = fs::remove_dir_all(&temp_dir);
                return Err("Vorgang durch Benutzer abgebrochen".to_string());
            }

            let all_entries: Vec<_> = WalkDir::new(&temp_dir).min_depth(1).into_iter().filter_map(|e| e.ok()).collect();
            let total_entries = all_entries.len();
            let mut count = 0usize;
            let mut extracted_targets = HashSet::new();

            for entry in all_entries {
                if is_cancelled() {
                    let _ = fs::remove_dir_all(&temp_dir);
                    return Err("Vorgang durch Benutzer abgebrochen".to_string());
                }
                let Ok(rel) = entry.path().strip_prefix(&temp_dir) else { continue };
                let rel_str = rel.to_string_lossy().replace('\\', "/");
                let is_dir = entry.file_type().is_dir();

                if let Some(target_path) = Self::match_archive_entry_target(&rel_str, is_dir, inner_paths, destination_dir) {
                    if !extracted_targets.insert(target_path.clone()) {
                        continue;
                    }
                    if is_dir {
                        let _ = fs::create_dir_all(&target_path);
                    } else {
                        if let Some(p) = target_path.parent() {
                            let _ = fs::create_dir_all(p);
                        }
                        if fs::copy(entry.path(), &target_path).is_ok() {
                            count += 1;
                            if let Some(cb) = on_progress {
                                cb(&rel_str, count, total_entries);
                            }
                        }
                    }
                }
            }

            let _ = fs::remove_dir_all(&temp_dir);
            return Ok(count.max(if inner_paths.is_empty() { 0 } else { 1 }));
        }

        if Self::is_tar_variant(&lower) {
            if let Some(cb) = on_progress {
                cb("Tar-Archiv wird dekomprimiert…", 0, 0);
            }
            let raw_tar = Self::decode_tar_bytes(archive_path)?;
            let mut tar = tar::Archive::new(Cursor::new(raw_tar));
            let mut count = 0usize;
            let mut extracted_targets = HashSet::new();

            for item in tar.entries().map_err(|e| e.to_string())? {
                if is_cancelled() {
                    return Err("Vorgang durch Benutzer abgebrochen".to_string());
                }
                let mut entry = item.map_err(|e| e.to_string())?;
                let path = entry.path().map_err(|e| e.to_string())?;
                let raw_name = path.to_string_lossy().to_string();
                let is_dir = entry.header().entry_type().is_dir();

                if let Some(target_path) = Self::match_archive_entry_target(&raw_name, is_dir, inner_paths, destination_dir) {
                    if !extracted_targets.insert(target_path.clone()) {
                        continue;
                    }
                    if is_dir {
                        fs::create_dir_all(&target_path).map_err(|e| e.to_string())?;
                    } else {
                        if let Some(p) = target_path.parent() {
                            fs::create_dir_all(p).map_err(|e| e.to_string())?;
                        }
                        let mut outfile = File::create(&target_path).map_err(|e| e.to_string())?;
                        io::copy(&mut entry, &mut outfile).map_err(|e| e.to_string())?;
                        count += 1;
                        if let Some(cb) = on_progress {
                            cb(&raw_name, count, 0);
                        }
                    }
                }
            }
            return Ok(count.max(if inner_paths.is_empty() { 0 } else { 1 }));
        }

        // Default ZIP (.zip, .jar, .nupkg)
        let f = File::open(archive_path).map_err(|e| e.to_string())?;
        let mut zip = ZipArchive::new(f).map_err(|e| e.to_string())?;
        let total_entries = zip.len();
        let mut count = 0usize;
        let mut extracted_targets = HashSet::new();

        for i in 0..total_entries {
            if is_cancelled() {
                return Err("Vorgang durch Benutzer abgebrochen".to_string());
            }
            let mut file = zip.by_index(i).map_err(|e| e.to_string())?;
            let raw_name = file.name().to_string();
            let is_dir = file.is_dir();

            if let Some(target_path) = Self::match_archive_entry_target(&raw_name, is_dir, inner_paths, destination_dir) {
                if !extracted_targets.insert(target_path.clone()) {
                    continue;
                }
                if is_dir {
                    fs::create_dir_all(&target_path).map_err(|e| e.to_string())?;
                } else {
                    if let Some(p) = target_path.parent() {
                        fs::create_dir_all(p).map_err(|e| e.to_string())?;
                    }
                    let mut outfile = File::create(&target_path).map_err(|e| e.to_string())?;
                    io::copy(&mut file, &mut outfile).map_err(|e| e.to_string())?;
                    count += 1;
                    if let Some(cb) = on_progress {
                        cb(&raw_name, count, total_entries);
                    }
                }
            }
        }
        Ok(count.max(if inner_paths.is_empty() { 0 } else { 1 }))
    }
}
