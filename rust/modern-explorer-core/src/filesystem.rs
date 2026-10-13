use crate::features::FileTagService;
use chrono::{DateTime, Local};
use regex::Regex;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use std::fs::{self, File};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};
use sysinfo::Disks;
use walkdir::WalkDir;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileEntry {
    pub full_path: String,
    pub name: String,
    pub extension: String,
    pub size_bytes: u64,
    pub is_directory: bool,
    pub is_hidden: bool,
    pub is_readonly: bool,
    pub modified: DateTime<Local>,
    pub created: DateTime<Local>,
    pub relative_path: Option<String>,
    pub allocated_tree_size: Option<u64>,
}

impl FileEntry {
    pub fn try_from_path(path: impl AsRef<Path>) -> Option<Self> {
        let p = path.as_ref();
        let meta = fs::metadata(p).ok()?;
        let is_dir = meta.is_dir();
        let name = p
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_else(|| p.to_string_lossy().to_string());
        let extension = if is_dir {
            String::new()
        } else {
            p.extension()
                .map(|e| format!(".{}", e.to_string_lossy()))
                .unwrap_or_default()
        };
        let size_bytes = if is_dir { 0 } else { meta.len() };
        let modified: DateTime<Local> = meta
            .modified()
            .ok()
            .map(DateTime::<Local>::from)
            .unwrap_or_else(Local::now);
        let created: DateTime<Local> = meta
            .created()
            .ok()
            .map(DateTime::<Local>::from)
            .unwrap_or(modified);
        let is_readonly = meta.permissions().readonly();
        let is_hidden = Self::detect_hidden(p, &name);

        Some(Self {
            full_path: p.to_string_lossy().to_string(),
            name,
            extension,
            size_bytes,
            is_directory: is_dir,
            is_hidden,
            is_readonly,
            modified,
            created,
            relative_path: None,
            allocated_tree_size: None,
        })
    }

    #[cfg(target_os = "windows")]
    fn detect_hidden(path: &Path, name: &str) -> bool {
        use std::os::windows::fs::MetadataExt;
        if name.starts_with('.') {
            return true;
        }
        if let Ok(meta) = fs::metadata(path) {
            const FILE_ATTRIBUTE_HIDDEN: u32 = 0x2;
            return (meta.file_attributes() & FILE_ATTRIBUTE_HIDDEN) != 0;
        }
        false
    }

    #[cfg(not(target_os = "windows"))]
    fn detect_hidden(_path: &Path, name: &str) -> bool {
        name.starts_with('.')
    }

    pub fn display_size(&self) -> String {
        if self.is_directory {
            if let Some(tree_bytes) = self.allocated_tree_size {
                format_bytes(tree_bytes)
            } else {
                "<DIR>".to_string()
            }
        } else {
            format_bytes(self.size_bytes)
        }
    }

    pub fn effective_size(&self) -> u64 {
        if self.is_directory {
            self.allocated_tree_size.unwrap_or(0)
        } else {
            self.size_bytes
        }
    }
}

pub fn format_bytes(bytes: u64) -> String {
    const KB: f64 = 1024.0;
    const MB: f64 = KB * 1024.0;
    const GB: f64 = MB * 1024.0;
    const TB: f64 = GB * 1024.0;
    let b = bytes as f64;
    if b >= TB {
        format!("{:.2} TB", b / TB)
    } else if b >= GB {
        format!("{:.2} GB", b / GB)
    } else if b >= MB {
        format!("{:.1} MB", b / MB)
    } else if b >= KB {
        format!("{:.1} KB", b / KB)
    } else {
        format!("{} B", bytes)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SortColumn {
    Name,
    Size,
    Modified,
    Extension,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SortDirection {
    Ascending,
    Descending,
}

pub fn sort_entries(entries: &mut [FileEntry], column: SortColumn, direction: SortDirection) {
    sort_entries_with_options(entries, column, direction, true);
}

pub fn sort_entries_with_options(
    entries: &mut [FileEntry],
    column: SortColumn,
    direction: SortDirection,
    folders_first: bool,
) {
    entries.sort_by(|a, b| {
        if folders_first && a.is_directory != b.is_directory {
            return b.is_directory.cmp(&a.is_directory);
        }
        let ord = match column {
            SortColumn::Name => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
            SortColumn::Size => a
                .effective_size()
                .cmp(&b.effective_size())
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase())),
            SortColumn::Modified => a
                .modified
                .cmp(&b.modified)
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase())),
            SortColumn::Extension => a
                .extension
                .to_lowercase()
                .cmp(&b.extension.to_lowercase())
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase())),
        };
        match direction {
            SortDirection::Ascending => ord,
            SortDirection::Descending => ord.reverse(),
        }
    });
}

pub struct DirectoryEnumerator;

impl DirectoryEnumerator {
    pub fn list_directory(
        dir: impl AsRef<Path>,
        show_hidden: bool,
        branch_view: bool,
    ) -> Vec<FileEntry> {
        let root = dir.as_ref();
        let mut results = Vec::new();
        if branch_view {
            for entry in WalkDir::new(root)
                .min_depth(1)
                .max_depth(8)
                .into_iter()
                .filter_map(|e| e.ok())
            {
                if entry.file_type().is_dir() {
                    continue;
                }
                if let Some(mut fe) = FileEntry::try_from_path(entry.path()) {
                    if !show_hidden && fe.is_hidden {
                        continue;
                    }
                    if let Ok(rel) = entry.path().strip_prefix(root) {
                        fe.relative_path = Some(rel.to_string_lossy().to_string());
                    }
                    results.push(fe);
                }
            }
        } else if let Ok(rd) = fs::read_dir(root) {
            for item in rd.flatten() {
                if let Some(fe) = FileEntry::try_from_path(item.path()) {
                    if !show_hidden && fe.is_hidden {
                        continue;
                    }
                    results.push(fe);
                }
            }
        }
        results
    }

    pub fn filter_entries(
        entries: &[FileEntry],
        query: &str,
        tag_service: Option<&FileTagService>,
    ) -> Vec<FileEntry> {
        let q = query.trim();
        if q.is_empty() {
            return entries.to_vec();
        }
        let q_lower = q.to_lowercase();

        if let Some(tag_filter) = q_lower.strip_prefix("tag:") {
            if let Some(ts) = tag_service {
                return entries
                    .iter()
                    .filter(|e| ts.matches_tag_query(&e.full_path, tag_filter))
                    .cloned()
                    .collect();
            }
        }

        if let Some(note_filter) = q_lower.strip_prefix("note:") {
            if let Some(ts) = tag_service {
                return entries
                    .iter()
                    .filter(|e| ts.matches_note_query(&e.full_path, note_filter))
                    .cloned()
                    .collect();
            }
        }

        if let Some(ext_filter) = q_lower.strip_prefix("ext:") {
            let exts: Vec<String> = ext_filter
                .split([';', ',', ' '])
                .filter(|s| !s.is_empty())
                .map(|s| {
                    if s.starts_with('.') {
                        s.to_string()
                    } else {
                        format!(".{}", s)
                    }
                })
                .collect();
            return entries
                .iter()
                .filter(|e| {
                    exts.iter()
                        .any(|ext| e.extension.eq_ignore_ascii_case(ext))
                })
                .cloned()
                .collect();
        }

        if q.contains('*') || q.contains('?') {
            let pattern = wildcard_to_regex(&q_lower);
            if let Ok(re) = Regex::new(&pattern) {
                return entries
                    .iter()
                    .filter(|e| re.is_match(&e.name.to_lowercase()))
                    .cloned()
                    .collect();
            }
        }

        entries
            .iter()
            .filter(|e| e.name.to_lowercase().contains(&q_lower))
            .cloned()
            .collect()
    }
}

pub fn wildcard_to_regex(pattern: &str) -> String {
    let mut rx = String::from("^");
    for ch in pattern.chars() {
        match ch {
            '*' => rx.push_str(".*"),
            '?' => rx.push('.'),
            '.' | '+' | '(' | ')' | '[' | ']' | '{' | '}' | '^' | '$' | '|' | '\\' => {
                rx.push('\\');
                rx.push(ch);
            }
            _ => rx.push(ch),
        }
    }
    rx.push('$');
    rx
}

#[derive(Debug, Clone)]
pub struct DriveItem {
    pub root_path: String,
    pub name: String,
    pub total_bytes: u64,
    pub available_bytes: u64,
    pub kind_label: String,
}

impl DriveItem {
    pub fn enumerate_drives() -> Vec<Self> {
        let disks = Disks::new_with_refreshed_list();
        let mut list = Vec::new();
        for d in disks.list() {
            let root = d.mount_point().to_string_lossy().to_string();
            let label = d.name().to_string_lossy().to_string();
            let total = d.total_space();
            let avail = d.available_space();
            let kind = format!("{:?}", d.kind());
            list.push(Self {
                root_path: root,
                name: label,
                total_bytes: total,
                available_bytes: avail,
                kind_label: kind,
            });
        }
        if list.is_empty() {
            list.push(Self {
                root_path: std::env::current_dir()
                    .unwrap_or_else(|_| PathBuf::from("."))
                    .to_string_lossy()
                    .to_string(),
                name: "System".to_string(),
                total_bytes: 100 * 1024 * 1024 * 1024,
                available_bytes: 50 * 1024 * 1024 * 1024,
                kind_label: "SSD".to_string(),
            });
        }
        list
    }

    pub fn used_fraction(&self) -> f32 {
        if self.total_bytes == 0 {
            0.0
        } else {
            let used = self.total_bytes.saturating_sub(self.available_bytes);
            (used as f64 / self.total_bytes as f64).clamp(0.0, 1.0) as f32
        }
    }
}

#[derive(Debug, Clone)]
pub struct FileTemplate {
    pub id: &'static str,
    pub display_name: &'static str,
    pub default_file_name: &'static str,
    pub initial_content: &'static str,
}

pub struct FileCreationService;

impl FileCreationService {
    pub fn templates() -> &'static [FileTemplate] {
        &[
            FileTemplate {
                id: "txt",
                display_name: "Text Document (.txt)",
                default_file_name: "NewFile.txt",
                initial_content: "",
            },
            FileTemplate {
                id: "md",
                display_name: "Markdown Document (.md)",
                default_file_name: "Document.md",
                initial_content: "# Title\n\n",
            },
            FileTemplate {
                id: "json",
                display_name: "JSON File (.json)",
                default_file_name: "config.json",
                initial_content: "{\n  \"name\": \"ModernExplorer\"\n}\n",
            },
            FileTemplate {
                id: "rs",
                display_name: "Rust Source (.rs)",
                default_file_name: "main.rs",
                initial_content: "fn main() {\n    println!(\"Hello, ModernExplorer!\");\n}\n",
            },
            FileTemplate {
                id: "cs",
                display_name: "C# Class (.cs)",
                default_file_name: "NewClass.cs",
                initial_content: "namespace App;\n\npublic class NewClass\n{\n}\n",
            },
            FileTemplate {
                id: "ps1",
                display_name: "PowerShell Script (.ps1)",
                default_file_name: "script.ps1",
                initial_content: "$ErrorActionPreference = 'Stop'\n",
            },
        ]
    }

    pub fn create_file(dir: &Path, name: &str, content: &str) -> Result<PathBuf, String> {
        NameValidator::validate(name)?;
        let target = dir.join(name);
        if target.exists() {
            return Err(format!("File already exists: {}", target.display()));
        }
        fs::write(&target, content).map_err(|e| e.to_string())?;
        Ok(target)
    }

    pub fn create_folder(dir: &Path, name: &str) -> Result<PathBuf, String> {
        NameValidator::validate(name)?;
        let target = dir.join(name);
        if target.exists() {
            return Err(format!("Folder already exists: {}", target.display()));
        }
        fs::create_dir_all(&target).map_err(|e| e.to_string())?;
        Ok(target)
    }
}

pub struct NameValidator;

impl NameValidator {
    pub fn validate(name: &str) -> Result<(), String> {
        let trimmed = name.trim();
        if trimmed.is_empty() || trimmed == "." || trimmed == ".." {
            return Err("Name must not be empty or '.' / '..'".to_string());
        }
        let invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        if trimmed.chars().any(|c| invalid.contains(&c) || c.is_control()) {
            return Err("Name contains invalid file system characters.".to_string());
        }
        let stem = trimmed
            .split('.')
            .next()
            .unwrap_or(trimmed)
            .to_ascii_uppercase();
        let reserved = [
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
            "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ];
        if reserved.contains(&stem.as_str()) {
            return Err(format!("'{}' is a reserved system device name.", stem));
        }
        Ok(())
    }
}

#[derive(Debug, Clone, Default)]
pub struct DeepSearchCriteria {
    pub root_path: String,
    pub name_pattern: String,
    pub use_regex: bool,
    pub content_query: String,
    pub case_sensitive_content: bool,
    pub min_size_bytes: Option<u64>,
    pub max_size_bytes: Option<u64>,
    pub include_hidden: bool,
    pub max_results: usize,
}

pub struct DeepSearchService;

impl DeepSearchService {
    pub fn search(criteria: &DeepSearchCriteria) -> Vec<FileEntry> {
        let max_res = if criteria.max_results == 0 {
            2500
        } else {
            criteria.max_results
        };
        let mut results = Vec::new();
        let pattern_parts: Vec<Regex> = if criteria.name_pattern.trim().is_empty() {
            Vec::new()
        } else if criteria.use_regex {
            Regex::new(criteria.name_pattern.trim())
                .map(|r| vec![r])
                .unwrap_or_default()
        } else {
            criteria
                .name_pattern
                .split([';', ','])
                .map(|s| s.trim())
                .filter(|s| !s.is_empty())
                .filter_map(|part| {
                    let pat = if part.contains('*') || part.contains('?') {
                        wildcard_to_regex(&part.to_lowercase())
                    } else {
                        format!(".*{}.*", regex::escape(&part.to_lowercase()))
                    };
                    Regex::new(&pat).ok()
                })
                .collect()
        };

        let content_needle = criteria.content_query.trim();
        let content_needle_lower = content_needle.to_lowercase();

        for entry in WalkDir::new(&criteria.root_path)
            .min_depth(1)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            if results.len() >= max_res {
                break;
            }
            let Some(fe) = FileEntry::try_from_path(entry.path()) else {
                continue;
            };
            if !criteria.include_hidden && fe.is_hidden {
                continue;
            }
            if !pattern_parts.is_empty() {
                let candidate = if criteria.use_regex {
                    fe.name.clone()
                } else {
                    fe.name.to_lowercase()
                };
                if !pattern_parts.iter().any(|re| re.is_match(&candidate)) {
                    continue;
                }
            }
            if let Some(min_s) = criteria.min_size_bytes {
                if fe.is_directory || fe.size_bytes < min_s {
                    continue;
                }
            }
            if let Some(max_s) = criteria.max_size_bytes {
                if fe.is_directory || fe.size_bytes > max_s {
                    continue;
                }
            }
            if !content_needle.is_empty() {
                if fe.is_directory || fe.size_bytes > 25 * 1024 * 1024 {
                    continue;
                }
                if let Ok(text) = fs::read_to_string(entry.path()) {
                    let matched = if criteria.case_sensitive_content {
                        text.contains(content_needle)
                    } else {
                        text.to_lowercase().contains(&content_needle_lower)
                    };
                    if !matched {
                        continue;
                    }
                } else {
                    continue;
                }
            }
            results.push(fe);
        }
        results
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DuplicateScanProgress {
    pub stage: u8,
    pub stage_message: String,
    pub percentage: f32,
    pub processed_files: usize,
    pub total_files: usize,
    pub is_indeterminate: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DuplicateFileGroup {
    pub sha256_hash: String,
    pub file_size_bytes: u64,
    pub files: Vec<FileEntry>,
}

impl DuplicateFileGroup {
    pub fn wasted_bytes(&self) -> u64 {
        if self.files.len() <= 1 {
            0
        } else {
            self.file_size_bytes * (self.files.len() as u64 - 1)
        }
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct DuplicateScanResult {
    pub groups: Vec<DuplicateFileGroup>,
    pub total_files_scanned: usize,
    pub total_wasted_bytes: u64,
}

pub struct DuplicateFinderService;

impl DuplicateFinderService {
    /// 3-stage duplicate scan:
    /// Stage 1: Group non-empty files by exact byte length
    /// Stage 2: Group candidates by 4 KB partial SHA-256 hash
    /// Stage 3: Group remaining candidates by full SHA-256 hash
    pub fn scan_directory(root_dir: impl AsRef<Path>, min_size_bytes: u64) -> DuplicateScanResult {
        Self::scan_directory_with_progress(root_dir, min_size_bytes, |_| {}, None)
    }

    pub fn scan_directory_with_progress<F>(
        root_dir: impl AsRef<Path>,
        min_size_bytes: u64,
        mut on_progress: F,
        cancel: Option<Arc<AtomicBool>>,
    ) -> DuplicateScanResult
    where
        F: FnMut(&DuplicateScanProgress),
    {
        let is_cancelled = || {
            cancel
                .as_ref()
                .map(|c| c.load(Ordering::Relaxed))
                .unwrap_or(false)
        };

        on_progress(&DuplicateScanProgress {
            stage: 1,
            stage_message: "1/3 Sammle Dateien nach Dateigröße…".to_string(),
            percentage: 0.0,
            processed_files: 0,
            total_files: 0,
            is_indeterminate: true,
        });

        let mut by_size: HashMap<u64, Vec<FileEntry>> = HashMap::new();
        let mut total_scanned = 0usize;

        for entry in WalkDir::new(root_dir.as_ref())
            .min_depth(1)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            if is_cancelled() {
                return DuplicateScanResult::default();
            }
            if !entry.file_type().is_file() {
                continue;
            }
            if let Some(fe) = FileEntry::try_from_path(entry.path()) {
                total_scanned += 1;
                if fe.size_bytes >= min_size_bytes.max(1) {
                    by_size.entry(fe.size_bytes).or_default().push(fe);
                }
                if total_scanned % 50 == 0 {
                    on_progress(&DuplicateScanProgress {
                        stage: 1,
                        stage_message: format!(
                            "1/3 Sammle Dateien nach Dateigröße… ({} Dateien erfasst)",
                            total_scanned
                        ),
                        percentage: 0.0,
                        processed_files: total_scanned,
                        total_files: 0,
                        is_indeterminate: true,
                    });
                }
            }
        }

        let candidate_buckets: Vec<(u64, Vec<FileEntry>)> =
            by_size.into_iter().filter(|(_, v)| v.len() >= 2).collect();

        if candidate_buckets.is_empty() {
            on_progress(&DuplicateScanProgress {
                stage: 3,
                stage_message: "Scan abgeschlossen – keine Duplikate gefunden.".to_string(),
                percentage: 100.0,
                processed_files: total_scanned,
                total_files: total_scanned,
                is_indeterminate: false,
            });
            return DuplicateScanResult {
                groups: Vec::new(),
                total_files_scanned: total_scanned,
                total_wasted_bytes: 0,
            };
        }

        let total_stage2_files: usize = candidate_buckets.iter().map(|(_, v)| v.len()).sum();
        let mut processed_stage2_files = 0usize;

        on_progress(&DuplicateScanProgress {
            stage: 2,
            stage_message: format!(
                "2/3 Prüfe Schnell-Hashes für {} Größen-Gruppen ({} Dateien)…",
                candidate_buckets.len(),
                total_stage2_files
            ),
            percentage: 5.0,
            processed_files: 0,
            total_files: total_stage2_files,
            is_indeterminate: false,
        });

        // Stage 2: 4 KB partial hash
        let mut partial_candidates: Vec<(u64, Vec<FileEntry>)> = Vec::new();
        for (size, candidates) in candidate_buckets {
            if is_cancelled() {
                return DuplicateScanResult::default();
            }
            let mut by_partial: HashMap<String, Vec<FileEntry>> = HashMap::new();
            for item in candidates {
                if is_cancelled() {
                    return DuplicateScanResult::default();
                }
                if let Some(ph) = Self::compute_partial_hash(&item.full_path, 4096) {
                    by_partial.entry(ph).or_default().push(item);
                }
                processed_stage2_files += 1;
                let pct = 5.0 + (processed_stage2_files as f32 * 45.0 / (total_stage2_files.max(1) as f32));
                if processed_stage2_files % 10 == 0 || processed_stage2_files == total_stage2_files {
                    on_progress(&DuplicateScanProgress {
                        stage: 2,
                        stage_message: format!(
                            "2/3 Prüfe Schnell-Hashes ({}/{})…",
                            processed_stage2_files, total_stage2_files
                        ),
                        percentage: pct.min(50.0),
                        processed_files: processed_stage2_files,
                        total_files: total_stage2_files,
                        is_indeterminate: false,
                    });
                }
            }
            for (_ph, matches) in by_partial {
                if matches.len() >= 2 {
                    partial_candidates.push((size, matches));
                }
            }
        }

        if partial_candidates.is_empty() {
            on_progress(&DuplicateScanProgress {
                stage: 3,
                stage_message: "Scan abgeschlossen – keine Duplikate gefunden.".to_string(),
                percentage: 100.0,
                processed_files: total_scanned,
                total_files: total_scanned,
                is_indeterminate: false,
            });
            return DuplicateScanResult {
                groups: Vec::new(),
                total_files_scanned: total_scanned,
                total_wasted_bytes: 0,
            };
        }

        // Stage 3: Full SHA-256 hash
        let total_stage3_files: usize = partial_candidates.iter().map(|(_, v)| v.len()).sum();
        let mut processed_stage3_files = 0usize;

        on_progress(&DuplicateScanProgress {
            stage: 3,
            stage_message: format!(
                "3/3 Berechne vollständige SHA-256-Prüfsummen ({} Kandidaten)…",
                total_stage3_files
            ),
            percentage: 50.0,
            processed_files: 0,
            total_files: total_stage3_files,
            is_indeterminate: false,
        });

        let mut groups = Vec::new();
        for (size, candidates) in partial_candidates {
            if is_cancelled() {
                return DuplicateScanResult::default();
            }
            let mut by_full: HashMap<String, Vec<FileEntry>> = HashMap::new();
            for item in candidates {
                if is_cancelled() {
                    return DuplicateScanResult::default();
                }
                if let Some(fh) = Self::compute_full_sha256(&item.full_path) {
                    by_full.entry(fh).or_default().push(item);
                }
                processed_stage3_files += 1;
                let pct = 50.0 + (processed_stage3_files as f32 * 50.0 / (total_stage3_files.max(1) as f32));
                if processed_stage3_files % 5 == 0 || processed_stage3_files == total_stage3_files {
                    on_progress(&DuplicateScanProgress {
                        stage: 3,
                        stage_message: format!(
                            "3/3 Berechne SHA-256 ({}/{})…",
                            processed_stage3_files, total_stage3_files
                        ),
                        percentage: pct.min(100.0),
                        processed_files: processed_stage3_files,
                        total_files: total_stage3_files,
                        is_indeterminate: false,
                    });
                }
            }
            for (hash, mut dup_files) in by_full {
                if dup_files.len() >= 2 {
                    // Sort by modified time first (oldest is original), then by path
                    dup_files.sort_by(|a, b| {
                        a.modified
                            .cmp(&b.modified)
                            .then_with(|| a.full_path.cmp(&b.full_path))
                    });
                    groups.push(DuplicateFileGroup {
                        sha256_hash: hash,
                        file_size_bytes: size,
                        files: dup_files,
                    });
                }
            }
        }

        groups.sort_by(|a, b| b.wasted_bytes().cmp(&a.wasted_bytes()));
        let total_wasted = groups.iter().map(|g| g.wasted_bytes()).sum();

        on_progress(&DuplicateScanProgress {
            stage: 3,
            stage_message: format!(
                "Scan abgeschlossen – {} Duplikat-Gruppen gefunden ({} verschwendet).",
                groups.len(),
                format_bytes(total_wasted)
            ),
            percentage: 100.0,
            processed_files: total_stage3_files,
            total_files: total_stage3_files,
            is_indeterminate: false,
        });

        DuplicateScanResult {
            groups,
            total_files_scanned: total_scanned,
            total_wasted_bytes: total_wasted,
        }
    }

    fn compute_partial_hash(path: &str, max_bytes: usize) -> Option<String> {
        let mut f = File::open(path).ok()?;
        let mut buf = vec![0u8; max_bytes];
        let n = f.read(&mut buf).ok()?;
        let mut hasher = Sha256::new();
        hasher.update(&buf[..n]);
        Some(format!("{:X}", hasher.finalize()))
    }

    fn compute_full_sha256(path: &str) -> Option<String> {
        let mut f = File::open(path).ok()?;
        let mut hasher = Sha256::new();
        let mut buf = [0u8; 65536];
        loop {
            let n = f.read(&mut buf).ok()?;
            if n == 0 {
                break;
            }
            hasher.update(&buf[..n]);
        }
        Some(format!("{:X}", hasher.finalize()))
    }
}

