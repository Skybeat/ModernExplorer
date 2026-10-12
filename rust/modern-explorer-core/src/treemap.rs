use crate::filesystem::format_bytes;
use crate::settings::AppSettings;
use chrono::{DateTime, Duration, Local};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum TreemapFileCategory {
    VideoMedia,
    ArchiveImage,
    VirtualDiskSystem,
    CodeDocument,
    PictureGraphic,
    BinaryExecutable,
    OtherTemp,
}

impl TreemapFileCategory {
    pub fn from_extension(ext: &str) -> Self {
        let lower = ext.trim().trim_start_matches('.').to_ascii_lowercase();
        match lower.as_str() {
            "mp4" | "mkv" | "avi" | "mov" | "wmv" | "webm" | "mp3" | "wav" | "flac" | "ogg"
            | "m4a" | "aac" => Self::VideoMedia,
            "zip" | "7z" | "rar" | "tar" | "gz" | "bz2" | "xz" | "tgz" | "iso" | "img"
            | "cab" | "nupkg" => Self::ArchiveImage,
            "vhdx" | "vhd" | "vmdk" | "vdi" | "qcow2" | "db" | "sqlite" | "sqlite3" | "mdf"
            | "ldf" | "bak" | "sys" => Self::VirtualDiskSystem,
            "cs" | "rs" | "ts" | "js" | "py" | "cpp" | "c" | "h" | "hpp" | "java" | "go"
            | "json" | "xml" | "yaml" | "yml" | "toml" | "md" | "txt" | "pdf" | "docx"
            | "xlsx" | "pptx" | "csv" | "html" | "css" | "xaml" => Self::CodeDocument,
            "png" | "jpg" | "jpeg" | "gif" | "webp" | "bmp" | "ico" | "svg" | "tiff" | "psd"
            | "raw" => Self::PictureGraphic,
            "exe" | "dll" | "msi" | "pdb" | "so" | "dylib" | "app" | "deb" | "rpm" | "wasm" => {
                Self::BinaryExecutable
            }
            _ => Self::OtherTemp,
        }
    }

    pub fn color_hex(self, settings: &AppSettings) -> String {
        match self {
            Self::VideoMedia => settings.treemap_video_media_color_hex.clone(),
            Self::ArchiveImage => settings.treemap_archive_image_color_hex.clone(),
            Self::VirtualDiskSystem => settings.treemap_virtual_disk_system_color_hex.clone(),
            Self::CodeDocument => settings.treemap_code_document_color_hex.clone(),
            Self::PictureGraphic => settings.treemap_picture_graphic_color_hex.clone(),
            Self::BinaryExecutable => settings.treemap_binary_executable_color_hex.clone(),
            Self::OtherTemp => settings.treemap_other_temp_color_hex.clone(),
        }
    }

    pub fn all_categories() -> &'static [TreemapFileCategory] {
        &[
            Self::VideoMedia,
            Self::ArchiveImage,
            Self::VirtualDiskSystem,
            Self::CodeDocument,
            Self::PictureGraphic,
            Self::BinaryExecutable,
            Self::OtherTemp,
        ]
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TreemapColorMode {
    Category,
    FileAgeHeatmap,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TreemapNode {
    pub name: String,
    pub full_path: String,
    pub size_bytes: u64,
    pub is_directory: bool,
    pub file_count: usize,
    pub folder_count: usize,
    pub category: TreemapFileCategory,
    pub modified: DateTime<Local>,
    pub children: Vec<TreemapNode>,
}

#[derive(Debug, Clone)]
pub struct TreemapRect {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
    pub node: TreemapNode,
}

pub struct StorageTreemapService;

impl StorageTreemapService {
    pub fn scan_directory_tree(root_path: impl AsRef<Path>, max_depth: usize) -> TreemapNode {
        Self::scan_node(root_path.as_ref(), 0, max_depth.max(1))
    }

    fn scan_node(path: &Path, depth: usize, max_depth: usize) -> TreemapNode {
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_else(|| path.to_string_lossy().to_string());
        let full_path = path.to_string_lossy().to_string();
        let meta = fs::metadata(path).ok();
        let modified = meta
            .as_ref()
            .and_then(|m| m.modified().ok())
            .map(DateTime::<Local>::from)
            .unwrap_or_else(Local::now);
        let is_dir = meta.as_ref().map(|m| m.is_dir()).unwrap_or(false);

        if !is_dir {
            let size = meta.map(|m| m.len()).unwrap_or(0);
            let ext = path
                .extension()
                .map(|e| e.to_string_lossy().to_string())
                .unwrap_or_default();
            return TreemapNode {
                name,
                full_path,
                size_bytes: size,
                is_directory: false,
                file_count: 1,
                folder_count: 0,
                category: TreemapFileCategory::from_extension(&ext),
                modified,
                children: Vec::new(),
            };
        }

        let mut children = Vec::new();
        let mut total_size = 0u64;
        let mut total_files = 0usize;
        let mut total_folders = 0usize;

        if depth < max_depth {
            if let Ok(rd) = fs::read_dir(path) {
                for entry in rd.flatten() {
                    let child = Self::scan_node(&entry.path(), depth + 1, max_depth);
                    total_size = total_size.saturating_add(child.size_bytes);
                    if child.is_directory {
                        total_folders += 1 + child.folder_count;
                        total_files += child.file_count;
                    } else {
                        total_files += 1;
                    }
                    children.push(child);
                }
            }
        }

        children.sort_by(|a, b| b.size_bytes.cmp(&a.size_bytes));

        TreemapNode {
            name,
            full_path,
            size_bytes: total_size,
            is_directory: true,
            file_count: total_files,
            folder_count: total_folders,
            category: TreemapFileCategory::OtherTemp,
            modified,
            children,
        }
    }

    pub fn compute_squarified_layout(
        root: &TreemapNode,
        width: f32,
        height: f32,
        max_rects: usize,
    ) -> Vec<TreemapRect> {
        let mut rects = Vec::new();
        if width <= 2.0 || height <= 2.0 || root.size_bytes == 0 {
            return rects;
        }
        let mut leaves = Vec::new();
        Self::collect_leaf_files(root, &mut leaves);
        leaves.sort_by(|a, b| b.size_bytes.cmp(&a.size_bytes));
        if leaves.len() > max_rects.max(10) {
            leaves.truncate(max_rects.max(10));
        }
        Self::slice_and_dice(&leaves, 0.0, 0.0, width, height, true, &mut rects);
        rects
    }

    fn collect_leaf_files(node: &TreemapNode, out: &mut Vec<TreemapNode>) {
        if !node.is_directory {
            if node.size_bytes > 0 {
                out.push(node.clone());
            }
            return;
        }
        for child in &node.children {
            Self::collect_leaf_files(child, out);
        }
    }

    fn slice_and_dice(
        nodes: &[TreemapNode],
        x: f32,
        y: f32,
        w: f32,
        h: f32,
        horizontal: bool,
        out: &mut Vec<TreemapRect>,
    ) {
        if nodes.is_empty() || w <= 1.0 || h <= 1.0 {
            return;
        }
        if nodes.len() == 1 {
            out.push(TreemapRect {
                x,
                y,
                width: w,
                height: h,
                node: nodes[0].clone(),
            });
            return;
        }
        let total: u64 = nodes.iter().map(|n| n.size_bytes).sum();
        if total == 0 {
            return;
        }
        let half = total / 2;
        let mut acc = 0u64;
        let mut split_idx = 1usize;
        for (i, n) in nodes.iter().enumerate() {
            acc += n.size_bytes;
            if acc >= half {
                split_idx = (i + 1).clamp(1, nodes.len() - 1);
                break;
            }
        }
        let left_sum: u64 = nodes[..split_idx].iter().map(|n| n.size_bytes).sum();
        let ratio = (left_sum as f32 / total as f32).clamp(0.05, 0.95);

        if horizontal {
            let w1 = w * ratio;
            Self::slice_and_dice(&nodes[..split_idx], x, y, w1, h, !horizontal, out);
            Self::slice_and_dice(&nodes[split_idx..], x + w1, y, w - w1, h, !horizontal, out);
        } else {
            let h1 = h * ratio;
            Self::slice_and_dice(&nodes[..split_idx], x, y, w, h1, !horizontal, out);
            Self::slice_and_dice(&nodes[split_idx..], x, y + h1, w, h - h1, !horizontal, out);
        }
    }

    pub fn get_top_largest_files(root: &TreemapNode, count: usize) -> Vec<TreemapNode> {
        let mut files = Vec::new();
        Self::collect_leaf_files(root, &mut files);
        files.sort_by(|a, b| b.size_bytes.cmp(&a.size_bytes));
        files.truncate(count.max(1));
        files
    }

    pub fn get_age_heatmap_color_hex(modified: DateTime<Local>) -> &'static str {
        let age: Duration = Local::now().signed_duration_since(modified);
        let days = age.num_days();
        if days < 7 {
            "#EF4444" // Hot / < 7 days
        } else if days < 30 {
            "#F97316" // < 30 days
        } else if days < 180 {
            "#EAB308" // < 6 months
        } else if days < 365 {
            "#10B981" // < 1 year
        } else if days < 1095 {
            "#0EA5E9" // 1-3 years
        } else {
            "#6366F1" // Cold / > 3 years
        }
    }

    pub fn remove_paths_from_tree(root: &mut TreemapNode, deleted_paths: &HashSet<String>) {
        root.children
            .retain(|c| !deleted_paths.contains(&c.full_path));
        for child in &mut root.children {
            if child.is_directory {
                Self::remove_paths_from_tree(child, deleted_paths);
            }
        }
        if root.is_directory {
            root.size_bytes = root.children.iter().map(|c| c.size_bytes).sum();
            root.file_count = root
                .children
                .iter()
                .map(|c| if c.is_directory { c.file_count } else { 1 })
                .sum();
            root.folder_count = root
                .children
                .iter()
                .filter(|c| c.is_directory)
                .map(|c| 1 + c.folder_count)
                .sum();
            root.children.sort_by(|a, b| b.size_bytes.cmp(&a.size_bytes));
        }
    }

    pub fn export_storage_report(root: &TreemapNode, markdown_format: bool) -> String {
        let top = Self::get_top_largest_files(root, 50);
        if markdown_format {
            let mut out = String::new();
            out.push_str(&format!("# Storage Report – {}\n\n", root.full_path));
            out.push_str(&format!(
                "- **Total Size**: {} ({} bytes)\n- **Files**: {}\n- **Folders**: {}\n\n",
                format_bytes(root.size_bytes),
                root.size_bytes,
                root.file_count,
                root.folder_count
            ));
            out.push_str("## Top Largest Files\n\n| Name | Size | Category | Path |\n| :--- | ---: | :--- | :--- |\n");
            for item in top {
                out.push_str(&format!(
                    "| {} | {} | {:?} | `{}` |\n",
                    item.name,
                    format_bytes(item.size_bytes),
                    item.category,
                    item.full_path
                ));
            }
            out
        } else {
            let mut out = String::from("Name,SizeBytes,FormattedSize,Category,FullPath\n");
            for item in top {
                out.push_str(&format!(
                    "\"{}\",{},{},\"{:?}\",\"{}\"\n",
                    item.name.replace('"', "\"\""),
                    item.size_bytes,
                    format_bytes(item.size_bytes),
                    item.category,
                    item.full_path.replace('"', "\"\"")
                ));
            }
            out
        }
    }
}

