use flate2::read::GzDecoder;
use flate2::write::GzEncoder;
use flate2::Compression;
use serde::{Deserialize, Serialize};
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::Path;
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
    pub fn is_supported_archive(path: &Path) -> bool {
        let lower = path.to_string_lossy().to_ascii_lowercase();
        lower.ends_with(".zip")
            || lower.ends_with(".jar")
            || lower.ends_with(".nupkg")
            || lower.ends_with(".tar.gz")
            || lower.ends_with(".tgz")
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
}

