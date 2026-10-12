use crate::features::MetadataAndAttributeService;
use crate::filesystem::FileEntry;
use regex::Regex;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::{HashMap, HashSet};
use std::fs;
use std::io::Read;
use std::path::{Path, PathBuf};
use walkdir::WalkDir;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum CaseTransform {
    Unchanged,
    Lowercase,
    Uppercase,
    TitleCase,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BatchRenameOptions {
    pub name_pattern: String,
    pub extension_pattern: String,
    pub search_for: String,
    pub replace_with: String,
    pub use_regex: bool,
    pub case_sensitive: bool,
    pub case_transform: CaseTransform,
    pub counter_start: usize,
    pub counter_step: usize,
    pub counter_digits: usize,
}

impl Default for BatchRenameOptions {
    fn default() -> Self {
        Self {
            name_pattern: "[N]".to_string(),
            extension_pattern: "[E]".to_string(),
            search_for: String::new(),
            replace_with: String::new(),
            use_regex: false,
            case_sensitive: false,
            case_transform: CaseTransform::Unchanged,
            counter_start: 1,
            counter_step: 1,
            counter_digits: 3,
        }
    }
}

#[derive(Debug, Clone)]
pub struct BatchRenamePreviewItem {
    pub original_path: String,
    pub original_name: String,
    pub new_name: String,
    pub has_conflict: bool,
    pub error_message: Option<String>,
}

pub struct BatchRenameService;

impl BatchRenameService {
    pub fn generate_preview(
        items: &[FileEntry],
        options: &BatchRenameOptions,
    ) -> Vec<BatchRenamePreviewItem> {
        let mut results = Vec::with_capacity(items.len());
        let mut seen_names = HashSet::new();
        let substring_re = Regex::new(r"\[N(\d+)-(\d+)\]").unwrap();

        for (idx, item) in items.iter().enumerate() {
            let p = Path::new(&item.full_path);
            let stem = if item.is_directory {
                item.name.clone()
            } else {
                p.file_stem()
                    .map(|s| s.to_string_lossy().to_string())
                    .unwrap_or_else(|| item.name.clone())
            };
            let ext_no_dot = item.extension.trim_start_matches('.').to_string();
            let parent_name = p
                .parent()
                .and_then(|par| par.file_name())
                .map(|n| n.to_string_lossy().to_string())
                .unwrap_or_default();

            let counter_val = options.counter_start + idx * options.counter_step.max(1);
            let counter_str = format!(
                "{:0width$}",
                counter_val,
                width = options.counter_digits.max(1)
            );

            let meta = MetadataAndAttributeService::inspect_media_metadata(&item.full_path);
            let width_str = meta.width.map(|w| w.to_string()).unwrap_or_default();
            let height_str = meta.height.map(|h| h.to_string()).unwrap_or_default();
            let res_str = meta.resolution_text;
            let exif_date_str = meta
                .date_taken
                .unwrap_or_else(|| item.modified.format("%Y-%m-%d").to_string());
            let camera_str = meta.camera_model.unwrap_or_default();

            let mut base = options.name_pattern.clone();
            base = substring_re
                .replace_all(&base, |caps: &regex::Captures| {
                    let start = caps[1].parse::<usize>().unwrap_or(1).saturating_sub(1);
                    let end = caps[2].parse::<usize>().unwrap_or(stem.len());
                    let chars: Vec<char> = stem.chars().collect();
                    let s = start.min(chars.len());
                    let e = end.min(chars.len()).max(s);
                    chars[s..e].iter().collect::<String>()
                })
                .to_string();

            base = base
                .replace("[N]", &stem)
                .replace("[C]", &counter_str)
                .replace("[P]", &parent_name)
                .replace("[Y]", &item.modified.format("%Y").to_string())
                .replace("[M]", &item.modified.format("%m").to_string())
                .replace("[D]", &item.modified.format("%d").to_string())
                .replace("[h]", &item.modified.format("%H").to_string())
                .replace("[m]", &item.modified.format("%M").to_string())
                .replace("[s]", &item.modified.format("%S").to_string())
                .replace("[Width]", &width_str)
                .replace("[Height]", &height_str)
                .replace("[Resolution]", &res_str)
                .replace("[ExifDate]", &exif_date_str)
                .replace("[Camera]", &camera_str);

            let mut ext_part = options
                .extension_pattern
                .replace("[E]", &ext_no_dot)
                .replace("[C]", &counter_str);

            if !options.search_for.is_empty() {
                if options.use_regex {
                    let pattern = if options.case_sensitive {
                        options.search_for.clone()
                    } else {
                        format!("(?i){}", options.search_for)
                    };
                    if let Ok(re) = Regex::new(&pattern) {
                        base = re.replace_all(&base, options.replace_with.as_str()).to_string();
                    }
                } else if options.case_sensitive {
                    base = base.replace(&options.search_for, &options.replace_with);
                } else {
                    let escaped = format!("(?i){}", regex::escape(&options.search_for));
                    if let Ok(re) = Regex::new(&escaped) {
                        base = re.replace_all(&base, options.replace_with.as_str()).to_string();
                    }
                }
            }

            base = Self::apply_case(&base, options.case_transform);
            ext_part = Self::apply_case(&ext_part, options.case_transform);

            let new_name = if item.is_directory || ext_part.trim().is_empty() {
                base
            } else {
                format!("{}.{}", base, ext_part.trim_start_matches('.'))
            };

            let key = new_name.to_lowercase();
            let has_conflict = !seen_names.insert(key) || new_name.trim().is_empty();
            let error_message = if has_conflict {
                Some("Duplicate or empty target name".to_string())
            } else {
                None
            };

            results.push(BatchRenamePreviewItem {
                original_path: item.full_path.clone(),
                original_name: item.name.clone(),
                new_name,
                has_conflict,
                error_message,
            });
        }

        results
    }

    fn apply_case(text: &str, mode: CaseTransform) -> String {
        match mode {
            CaseTransform::Unchanged => text.to_string(),
            CaseTransform::Lowercase => text.to_lowercase(),
            CaseTransform::Uppercase => text.to_uppercase(),
            CaseTransform::TitleCase => text
                .split_whitespace()
                .map(|w| {
                    let mut chars = w.chars();
                    match chars.next() {
                        None => String::new(),
                        Some(first) => {
                            first.to_uppercase().collect::<String>()
                                + &chars.as_str().to_lowercase()
                        }
                    }
                })
                .collect::<Vec<_>>()
                .join(" "),
        }
    }

    pub fn execute_rename(
        previews: &[BatchRenamePreviewItem],
    ) -> Result<Vec<(String, String)>, String> {
        if previews.iter().any(|p| p.has_conflict) {
            return Err("Cannot execute batch rename while conflicts exist.".to_string());
        }
        let mut renamed_pairs = Vec::new();
        for item in previews {
            if item.original_name == item.new_name {
                continue;
            }
            let src = Path::new(&item.original_path);
            if let Some(parent) = src.parent() {
                let dst = parent.join(&item.new_name);
                fs::rename(src, &dst).map_err(|e| e.to_string())?;
                renamed_pairs.push((
                    item.original_path.clone(),
                    dst.to_string_lossy().to_string(),
                ));
            }
        }
        Ok(renamed_pairs)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SyncComparisonMode {
    SizeAndTimestamp,
    SizeOnly,
    Sha256Content,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SyncDirectionMode {
    Bidirectional,
    LeftToRight,
    RightToLeft,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SyncAction {
    Equal,
    CopyLeftToRight,
    CopyRightToLeft,
    DeleteLeft,
    DeleteRight,
}

#[derive(Debug, Clone)]
pub struct SyncComparisonItem {
    pub relative_path: String,
    pub left_path: Option<String>,
    pub right_path: Option<String>,
    pub left_size: Option<u64>,
    pub right_size: Option<u64>,
    pub action: SyncAction,
}

pub struct DirectorySyncService;

impl DirectorySyncService {
    pub fn compare_directories(
        left_root: &Path,
        right_root: &Path,
        comp_mode: SyncComparisonMode,
        dir_mode: SyncDirectionMode,
        delete_orphans: bool,
    ) -> Vec<SyncComparisonItem> {
        let left_files = Self::collect_relative_files(left_root);
        let right_files = Self::collect_relative_files(right_root);
        let mut all_keys: Vec<String> = left_files
            .keys()
            .chain(right_files.keys())
            .cloned()
            .collect::<HashSet<_>>()
            .into_iter()
            .collect();
        all_keys.sort();

        let mut items = Vec::new();
        for rel in all_keys {
            let l = left_files.get(&rel);
            let r = right_files.get(&rel);
            let action = match (l, r) {
                (Some(_le), None) => match dir_mode {
                    SyncDirectionMode::Bidirectional | SyncDirectionMode::LeftToRight => {
                        SyncAction::CopyLeftToRight
                    }
                    SyncDirectionMode::RightToLeft => {
                        if delete_orphans {
                            SyncAction::DeleteLeft
                        } else {
                            SyncAction::Equal
                        }
                    }
                },
                (None, Some(_re)) => match dir_mode {
                    SyncDirectionMode::Bidirectional | SyncDirectionMode::RightToLeft => {
                        SyncAction::CopyRightToLeft
                    }
                    SyncDirectionMode::LeftToRight => {
                        if delete_orphans {
                            SyncAction::DeleteRight
                        } else {
                            SyncAction::Equal
                        }
                    }
                },
                (Some(le), Some(re)) => {
                    let is_same = match comp_mode {
                        SyncComparisonMode::SizeOnly => le.size_bytes == re.size_bytes,
                        SyncComparisonMode::SizeAndTimestamp => {
                            le.size_bytes == re.size_bytes
                                && (le.modified - re.modified).num_seconds().abs() <= 2
                        }
                        SyncComparisonMode::Sha256Content => {
                            le.size_bytes == re.size_bytes
                                && Self::file_sha256(&le.full_path)
                                    == Self::file_sha256(&re.full_path)
                        }
                    };
                    if is_same {
                        SyncAction::Equal
                    } else {
                        match dir_mode {
                            SyncDirectionMode::LeftToRight => SyncAction::CopyLeftToRight,
                            SyncDirectionMode::RightToLeft => SyncAction::CopyRightToLeft,
                            SyncDirectionMode::Bidirectional => {
                                if le.modified >= re.modified {
                                    SyncAction::CopyLeftToRight
                                } else {
                                    SyncAction::CopyRightToLeft
                                }
                            }
                        }
                    }
                }
                (None, None) => SyncAction::Equal,
            };

            items.push(SyncComparisonItem {
                relative_path: rel,
                left_path: l.map(|x| x.full_path.clone()),
                right_path: r.map(|x| x.full_path.clone()),
                left_size: l.map(|x| x.size_bytes),
                right_size: r.map(|x| x.size_bytes),
                action,
            });
        }
        items
    }

    pub fn execute_sync(
        left_root: &Path,
        right_root: &Path,
        items: &[SyncComparisonItem],
    ) -> Result<usize, String> {
        let mut count = 0usize;
        for item in items {
            match item.action {
                SyncAction::Equal => {}
                SyncAction::CopyLeftToRight => {
                    let src = left_root.join(&item.relative_path);
                    let dst = right_root.join(&item.relative_path);
                    if let Some(par) = dst.parent() {
                        fs::create_dir_all(par).map_err(|e| e.to_string())?;
                    }
                    fs::copy(&src, &dst).map_err(|e| e.to_string())?;
                    count += 1;
                }
                SyncAction::CopyRightToLeft => {
                    let src = right_root.join(&item.relative_path);
                    let dst = left_root.join(&item.relative_path);
                    if let Some(par) = dst.parent() {
                        fs::create_dir_all(par).map_err(|e| e.to_string())?;
                    }
                    fs::copy(&src, &dst).map_err(|e| e.to_string())?;
                    count += 1;
                }
                SyncAction::DeleteLeft => {
                    let target = left_root.join(&item.relative_path);
                    if target.exists() {
                        fs::remove_file(&target).map_err(|e| e.to_string())?;
                        count += 1;
                    }
                }
                SyncAction::DeleteRight => {
                    let target = right_root.join(&item.relative_path);
                    if target.exists() {
                        fs::remove_file(&target).map_err(|e| e.to_string())?;
                        count += 1;
                    }
                }
            }
        }
        Ok(count)
    }

    fn collect_relative_files(root: &Path) -> HashMap<String, FileEntry> {
        let mut map = HashMap::new();
        for entry in WalkDir::new(root)
            .min_depth(1)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            if entry.file_type().is_file() {
                if let (Ok(rel), Some(fe)) = (
                    entry.path().strip_prefix(root),
                    FileEntry::try_from_path(entry.path()),
                ) {
                    let norm_rel = rel.to_string_lossy().replace('\\', "/");
                    map.insert(norm_rel, fe);
                }
            }
        }
        map
    }

    fn file_sha256(path: &str) -> Option<String> {
        let mut f = fs::File::open(path).ok()?;
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ConflictResolution {
    Overwrite,
    Skip,
    KeepBoth,
}

pub struct FileOperationExecutor;

impl FileOperationExecutor {
    pub fn copy_entry(
        src: &Path,
        dest_dir: &Path,
        conflict: ConflictResolution,
    ) -> Result<Option<PathBuf>, String> {
        let file_name = src
            .file_name()
            .ok_or_else(|| "Invalid source path".to_string())?;
        let mut target = dest_dir.join(file_name);
        if target.exists() {
            match conflict {
                ConflictResolution::Skip => return Ok(None),
                ConflictResolution::Overwrite => {}
                ConflictResolution::KeepBoth => {
                    target = Self::unique_destination_path(dest_dir, src);
                }
            }
        }

        if src.is_dir() {
            Self::copy_dir_recursive(src, &target)?;
        } else {
            if let Some(p) = target.parent() {
                fs::create_dir_all(p).map_err(|e| e.to_string())?;
            }
            fs::copy(src, &target).map_err(|e| e.to_string())?;
        }
        Ok(Some(target))
    }

    pub fn move_entry(
        src: &Path,
        dest_dir: &Path,
        conflict: ConflictResolution,
    ) -> Result<Option<PathBuf>, String> {
        let file_name = src
            .file_name()
            .ok_or_else(|| "Invalid source path".to_string())?;
        let mut target = dest_dir.join(file_name);
        if target.exists() {
            match conflict {
                ConflictResolution::Skip => return Ok(None),
                ConflictResolution::Overwrite => {
                    if target.is_dir() {
                        let _ = fs::remove_dir_all(&target);
                    } else {
                        let _ = fs::remove_file(&target);
                    }
                }
                ConflictResolution::KeepBoth => {
                    target = Self::unique_destination_path(dest_dir, src);
                }
            }
        }
        if fs::rename(src, &target).is_err() {
            if src.is_dir() {
                Self::copy_dir_recursive(src, &target)?;
                fs::remove_dir_all(src).map_err(|e| e.to_string())?;
            } else {
                fs::copy(src, &target).map_err(|e| e.to_string())?;
                fs::remove_file(src).map_err(|e| e.to_string())?;
            }
        }
        Ok(Some(target))
    }

    pub fn delete_entry(path: &Path) -> Result<(), String> {
        if path.is_dir() {
            fs::remove_dir_all(path).map_err(|e| e.to_string())
        } else {
            fs::remove_file(path).map_err(|e| e.to_string())
        }
    }

    fn copy_dir_recursive(src: &Path, dst: &Path) -> Result<(), String> {
        fs::create_dir_all(dst).map_err(|e| e.to_string())?;
        for entry in fs::read_dir(src).map_err(|e| e.to_string())? {
            let entry = entry.map_err(|e| e.to_string())?;
            let child_src = entry.path();
            let child_dst = dst.join(entry.file_name());
            if child_src.is_dir() {
                Self::copy_dir_recursive(&child_src, &child_dst)?;
            } else {
                fs::copy(&child_src, &child_dst).map_err(|e| e.to_string())?;
            }
        }
        Ok(())
    }

    pub fn unique_destination_path(dest_dir: &Path, src: &Path) -> PathBuf {
        let stem = src
            .file_stem()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| "Item".to_string());
        let ext = src
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy()))
            .unwrap_or_default();
        for idx in 1..10000 {
            let candidate = dest_dir.join(format!("{} ({}){}", stem, idx, ext));
            if !candidate.exists() {
                return candidate;
            }
        }
        dest_dir.join(format!("{}_copy{}", stem, ext))
    }
}

#[derive(Debug, Clone)]
pub enum UndoAction {
    Rename {
        original_path: String,
        current_path: String,
    },
    BatchRename {
        pairs: Vec<(String, String)>,
    },
    CreatedFileOrDir {
        created_path: String,
    },
    Copied {
        copied_path: String,
    },
    Moved {
        original_path: String,
        moved_path: String,
    },
}

#[derive(Debug, Clone, Default)]
pub struct UndoHistoryService {
    stack: Vec<(String, UndoAction)>,
}

impl UndoHistoryService {
    pub fn record(&mut self, description: impl Into<String>, action: UndoAction) {
        self.stack.push((description.into(), action));
    }

    pub fn can_undo(&self) -> bool {
        !self.stack.is_empty()
    }

    pub fn last_description(&self) -> Option<&str> {
        self.stack.last().map(|(d, _)| d.as_str())
    }

    pub fn undo_last(&mut self) -> Result<String, String> {
        let Some((desc, action)) = self.stack.pop() else {
            return Err("Nothing to undo.".to_string());
        };
        match action {
            UndoAction::Rename {
                original_path,
                current_path,
            } => {
                fs::rename(&current_path, &original_path).map_err(|e| e.to_string())?;
            }
            UndoAction::BatchRename { pairs } => {
                for (orig, curr) in pairs.into_iter().rev() {
                    let _ = fs::rename(&curr, &orig);
                }
            }
            UndoAction::CreatedFileOrDir { created_path } => {
                let p = Path::new(&created_path);
                if p.exists() {
                    FileOperationExecutor::delete_entry(p)?;
                }
            }
            UndoAction::Copied { copied_path } => {
                let p = Path::new(&copied_path);
                if p.exists() {
                    FileOperationExecutor::delete_entry(p)?;
                }
            }
            UndoAction::Moved {
                original_path,
                moved_path,
            } => {
                fs::rename(&moved_path, &original_path).map_err(|e| e.to_string())?;
            }
        }
        Ok(desc)
    }
}

