use crate::filesystem::format_bytes;
use crate::settings::AppSettings;
use chrono::{DateTime, Duration, Local};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};
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
            "mp4" | "mkv" | "avi" | "mov" | "wmv" | "webm" | "flv" | "m4v" | "mpg" | "mpeg"
            | "mp3" | "wav" | "flac" | "aac" | "ogg" | "m4a" | "wma" | "opus" => Self::VideoMedia,
            "zip" | "7z" | "rar" | "tar" | "gz" | "bz2" | "xz" | "tgz" | "tbz2" | "zst"
            | "iso" | "img" | "cab" | "nupkg" | "jar" | "war" | "apk" | "dmg" | "deb" | "rpm" => {
                Self::ArchiveImage
            }
            "vhdx" | "vhd" | "vmdk" | "vdi" | "qcow2" | "ova" | "ovf" | "sys" | "dmp" | "bak"
            | "old" | "hiberfil" | "swap" | "bin" | "dat" | "db" | "sqlite" | "sqlite3" | "mdf"
            | "ldf" => Self::VirtualDiskSystem,
            "cs" | "vb" | "fs" | "cpp" | "c" | "h" | "hpp" | "rs" | "go" | "py" | "js" | "ts"
            | "tsx" | "jsx" | "html" | "htm" | "css" | "scss" | "json" | "xml" | "yaml" | "yml"
            | "toml" | "ini" | "cfg" | "conf" | "md" | "txt" | "rtf" | "pdf" | "doc" | "docx"
            | "xls" | "xlsx" | "ppt" | "pptx" | "csv" | "tsv" | "sql" | "sh" | "ps1" | "bat"
            | "cmd" | "xaml" => Self::CodeDocument,
            "png" | "jpg" | "jpeg" | "webp" | "gif" | "bmp" | "svg" | "ico" | "tif" | "tiff"
            | "psd" | "ai" | "eps" | "raw" | "cr2" | "nef" | "dng" | "heic" | "avif" | "dds"
            | "tga" => Self::PictureGraphic,
            "exe" | "dll" | "msi" | "msix" | "appx" | "pdb" | "so" | "dylib" | "lib" | "a"
            | "obj" | "o" | "wasm" | "winmd" | "app" => Self::BinaryExecutable,
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

    pub fn display_name(self, is_de: bool) -> &'static str {
        match (self, is_de) {
            (Self::VideoMedia, true) => "🎬 Video & Audio",
            (Self::VideoMedia, false) => "🎬 Video & Audio",
            (Self::ArchiveImage, true) => "📦 Archive & Disk-Images",
            (Self::ArchiveImage, false) => "📦 Archives & Disk Images",
            (Self::VirtualDiskSystem, true) => "💿 Virtuelle Disks, DB & System",
            (Self::VirtualDiskSystem, false) => "💿 Virtual Disks, DB & System",
            (Self::CodeDocument, true) => "📝 Code, Text & Dokumente",
            (Self::CodeDocument, false) => "📝 Code, Text & Documents",
            (Self::PictureGraphic, true) => "🖼 Bilder & Grafiken",
            (Self::PictureGraphic, false) => "🖼 Pictures & Graphics",
            (Self::BinaryExecutable, true) => "⚙ Programme & Bibliotheken",
            (Self::BinaryExecutable, false) => "⚙ Binaries & Libraries",
            (Self::OtherTemp, true) => "📄 Sonstige & Temporäre Dateien",
            (Self::OtherTemp, false) => "📄 Other & Temporary Files",
        }
    }

    pub fn short_name(self, is_de: bool) -> &'static str {
        match (self, is_de) {
            (Self::VideoMedia, _) => "🎬 Video",
            (Self::ArchiveImage, _) => "📦 Archive",
            (Self::VirtualDiskSystem, _) => "💿 Disk/DB",
            (Self::CodeDocument, _) => "📝 Code/Doc",
            (Self::PictureGraphic, true) => "🖼 Bilder",
            (Self::PictureGraphic, false) => "🖼 Images",
            (Self::BinaryExecutable, true) => "⚙ Binär",
            (Self::BinaryExecutable, false) => "⚙ Binary",
            (Self::OtherTemp, true) => "📄 Sonstige",
            (Self::OtherTemp, false) => "📄 Other",
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
pub struct StorageExtensionStat {
    pub extension: String,
    pub category: TreemapFileCategory,
    pub category_label: String,
    pub color_hex: String,
    pub total_bytes: u64,
    pub size_text: String,
    pub percentage: f64,
    pub file_count: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TreemapNode {
    pub name: String,
    pub full_path: String,
    pub relative_path: String,
    pub extension: String,
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
    pub depth: usize,
    pub is_group_container: bool,
    pub node: TreemapNode,
}

pub struct StorageTreemapService;

impl StorageTreemapService {
    pub fn scan_directory_tree(root_path: impl AsRef<Path>, max_depth: usize) -> TreemapNode {
        let root_ref = root_path.as_ref();
        let canonical_root = fs::canonicalize(root_ref)
            .map(|p| {
                let s = p.to_string_lossy().to_string();
                s.strip_prefix(r"\\?\").unwrap_or(&s).to_string()
            })
            .unwrap_or_else(|_| root_ref.to_string_lossy().to_string());
        let base_path = Path::new(&canonical_root);
        Self::scan_node(base_path, base_path, 0, max_depth.max(1))
    }

    fn scan_node(path: &Path, root_base: &Path, depth: usize, max_depth: usize) -> TreemapNode {
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| path.to_string_lossy().to_string());
        let full_path = path.to_string_lossy().to_string();
        let relative_path = path
            .strip_prefix(root_base)
            .ok()
            .map(|p| {
                let s = p.to_string_lossy().to_string();
                if s.is_empty() { ".".to_string() } else { s }
            })
            .unwrap_or_else(|| name.clone());

        let symlink_meta = fs::symlink_metadata(path).ok();
        let is_symlink = symlink_meta
            .as_ref()
            .map(|m| m.file_type().is_symlink())
            .unwrap_or(false);
        let meta = symlink_meta.or_else(|| fs::metadata(path).ok());

        let modified = meta
            .as_ref()
            .and_then(|m| m.modified().ok())
            .map(DateTime::<Local>::from)
            .unwrap_or_else(Local::now);
        let is_dir = meta.as_ref().map(|m| m.is_dir()).unwrap_or(false);

        if !is_dir {
            let size = meta.map(|m| m.len()).unwrap_or(0);
            let raw_ext = path
                .extension()
                .map(|e| e.to_string_lossy().to_string())
                .unwrap_or_default();
            let ext_with_dot = if raw_ext.is_empty() {
                String::new()
            } else {
                format!(".{}", raw_ext.to_ascii_lowercase())
            };
            return TreemapNode {
                name,
                full_path,
                relative_path,
                extension: ext_with_dot,
                size_bytes: size,
                is_directory: false,
                file_count: 1,
                folder_count: 0,
                category: TreemapFileCategory::from_extension(&raw_ext),
                modified,
                children: Vec::new(),
            };
        }

        let mut children = Vec::new();
        let mut total_size = 0u64;
        let mut total_files = 0usize;
        let mut total_folders = 0usize;

        if !is_symlink && depth < max_depth {
            if let Ok(rd) = fs::read_dir(path) {
                for entry in rd.flatten() {
                    let entry_path = entry.path();
                    if let Ok(ft) = entry.file_type() {
                        if ft.is_symlink() {
                            continue;
                        }
                    }
                    let child = Self::scan_node(&entry_path, root_base, depth + 1, max_depth);
                    if child.is_directory && child.file_count == 0 && child.size_bytes == 0 {
                        continue;
                    }
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
            relative_path,
            extension: String::new(),
            size_bytes: total_size,
            is_directory: true,
            file_count: total_files,
            folder_count: total_folders,
            category: TreemapFileCategory::OtherTemp,
            modified,
            children,
        }
    }

    pub fn find_node_by_path<'a>(root: &'a TreemapNode, target_path: &str) -> Option<&'a TreemapNode> {
        let norm_target = target_path.trim_end_matches(&['\\', '/'][..]);
        let norm_root = root.full_path.trim_end_matches(&['\\', '/'][..]);
        if norm_root.eq_ignore_ascii_case(norm_target) {
            return Some(root);
        }
        for child in &root.children {
            if let Some(found) = Self::find_node_by_path(child, target_path) {
                return Some(found);
            }
        }
        None
    }

    pub fn find_parent_path(root: &TreemapNode, target_path: &str) -> Option<String> {
        let norm_target = target_path.trim_end_matches(&['\\', '/'][..]);
        for child in &root.children {
            let norm_child = child.full_path.trim_end_matches(&['\\', '/'][..]);
            if norm_child.eq_ignore_ascii_case(norm_target) {
                return Some(root.full_path.clone());
            }
            if child.is_directory {
                if let Some(p) = Self::find_parent_path(child, target_path) {
                    return Some(p);
                }
            }
        }
        None
    }

    /// Computes a 2D Squarified Treemap layout (Bruls-Huizing-van Wijk algorithm) with hierarchical Group Containers,
    /// matching .NET's `StorageTreemapService.BuildSquarifiedTreemapLayout`.
    pub fn build_squarified_treemap_layout(
        root_node: &TreemapNode,
        width: f32,
        height: f32,
        max_depth: usize,
        max_tiles: usize,
        min_tile_size: f32,
    ) -> Vec<TreemapRect> {
        let mut results = Vec::new();
        if width <= 4.0 || height <= 4.0 || root_node.size_bytes == 0 || root_node.children.is_empty() {
            return results;
        }
        let mut leaf_count = 0usize;
        Self::layout_directory(
            root_node,
            0.0,
            0.0,
            width,
            height,
            0,
            max_depth,
            max_tiles,
            min_tile_size,
            &mut leaf_count,
            &mut results,
        );
        results
    }

    #[allow(clippy::too_many_arguments)]
    fn layout_directory(
        dir_node: &TreemapNode,
        x: f32,
        y: f32,
        w: f32,
        h: f32,
        depth: usize,
        max_depth: usize,
        max_tiles: usize,
        min_tile_size: f32,
        leaf_count: &mut usize,
        results: &mut Vec<TreemapRect>,
    ) {
        if w < 3.0 || h < 3.0 || dir_node.size_bytes == 0 || dir_node.children.is_empty() {
            return;
        }

        let non_empty: Vec<&TreemapNode> = dir_node
            .children
            .iter()
            .filter(|c| c.size_bytes > 0)
            .collect();
        if non_empty.is_empty() {
            return;
        }

        let child_rects = Self::squarify_children(&non_empty, x, y, w, h, dir_node.size_bytes);

        for (child, cx, cy, cw, ch) in child_rects {
            if cw < min_tile_size || ch < min_tile_size {
                continue;
            }

            let can_recurse = child.is_directory
                && depth < max_depth
                && cw >= 28.0
                && ch >= 28.0
                && !child.children.is_empty()
                && *leaf_count < max_tiles;

            if can_recurse {
                let mut shallow = child.clone();
                shallow.children.clear();
                results.push(TreemapRect {
                    x: cx,
                    y: cy,
                    width: cw,
                    height: ch,
                    depth,
                    is_group_container: true,
                    node: shallow,
                });

                let header_h = if ch >= 42.0 { 17.0 } else { 3.0 };
                let pad = 2.0;
                let inner_x = cx + pad;
                let inner_y = cy + header_h;
                let inner_w = (cw - pad * 2.0).max(0.0);
                let inner_h = (ch - header_h - pad).max(0.0);
                if inner_w >= 6.0 && inner_h >= 6.0 {
                    Self::layout_directory(
                        child,
                        inner_x,
                        inner_y,
                        inner_w,
                        inner_h,
                        depth + 1,
                        max_depth,
                        max_tiles,
                        min_tile_size,
                        leaf_count,
                        results,
                    );
                }
            } else {
                if *leaf_count >= max_tiles {
                    break;
                }
                *leaf_count += 1;
                let mut shallow = child.clone();
                shallow.children.clear();
                results.push(TreemapRect {
                    x: cx,
                    y: cy,
                    width: cw,
                    height: ch,
                    depth,
                    is_group_container: false,
                    node: shallow,
                });
            }
        }
    }

    fn squarify_children<'a>(
        children: &[&'a TreemapNode],
        x: f32,
        y: f32,
        w: f32,
        h: f32,
        total_bytes: u64,
    ) -> Vec<(&'a TreemapNode, f32, f32, f32, f32)> {
        let mut output = Vec::with_capacity(children.len());
        let total_area = (w as f64) * (h as f64);
        if total_area <= 0.0 || total_bytes == 0 {
            return output;
        }

        let scaled: Vec<(&'a TreemapNode, f64)> = children
            .iter()
            .map(|&c| {
                let area = ((c.size_bytes as f64) / (total_bytes as f64) * total_area).max(0.0001);
                (c, area)
            })
            .collect();

        let mut cur_x = x as f64;
        let mut cur_y = y as f64;
        let mut cur_w = w as f64;
        let mut cur_h = h as f64;

        let mut current_row: Vec<(&'a TreemapNode, f64)> = Vec::new();

        fn layout_row<'a>(
            row: &[(&'a TreemapNode, f64)],
            cur_x: &mut f64,
            cur_y: &mut f64,
            cur_w: &mut f64,
            cur_h: &mut f64,
            output: &mut Vec<(&'a TreemapNode, f32, f32, f32, f32)>,
        ) {
            if row.is_empty() || *cur_w <= 0.0 || *cur_h <= 0.0 {
                return;
            }
            let row_area: f64 = row.iter().map(|r| r.1).sum();
            let horizontal = *cur_w >= *cur_h;

            if horizontal {
                let strip_w = if *cur_h > 0.0 { (row_area / *cur_h).min(*cur_w) } else { 0.0 };
                let mut offset_y = *cur_y;
                for &(node, area) in row {
                    let item_h = if strip_w > 0.0 {
                        (area / strip_w).min(*cur_y + *cur_h - offset_y)
                    } else {
                        0.0
                    };
                    output.push((
                        node,
                        *cur_x as f32,
                        offset_y as f32,
                        strip_w as f32,
                        item_h.max(0.0) as f32,
                    ));
                    offset_y += item_h;
                }
                *cur_x += strip_w;
                *cur_w = (*cur_w - strip_w).max(0.0);
            } else {
                let strip_h = if *cur_w > 0.0 { (row_area / *cur_w).min(*cur_h) } else { 0.0 };
                let mut offset_x = *cur_x;
                for &(node, area) in row {
                    let item_w = if strip_h > 0.0 {
                        (area / strip_h).min(*cur_x + *cur_w - offset_x)
                    } else {
                        0.0
                    };
                    output.push((
                        node,
                        offset_x as f32,
                        *cur_y as f32,
                        item_w.max(0.0) as f32,
                        strip_h as f32,
                    ));
                    offset_x += item_w;
                }
                *cur_y += strip_h;
                *cur_h = (*cur_h - strip_h).max(0.0);
            }
        }

        fn worst_aspect_ratio(row: &[(&TreemapNode, f64)], side_length: f64) -> f64 {
            if row.is_empty() || side_length <= 0.0 {
                return f64::MAX;
            }
            let mut sum = 0.0;
            let mut max_a = 0.0f64;
            let mut min_a = f64::MAX;
            for &(_, area) in row {
                sum += area;
                if area > max_a {
                    max_a = area;
                }
                if area < min_a {
                    min_a = area;
                }
            }
            if sum <= 0.0 || min_a <= 0.0 {
                return f64::MAX;
            }
            let s2 = sum * sum;
            let w2 = side_length * side_length;
            ((w2 * max_a) / s2).max(s2 / (w2 * min_a))
        }

        for item in scaled {
            if cur_w <= 0.5 || cur_h <= 0.5 {
                break;
            }
            let short_side = cur_w.min(cur_h);
            if current_row.is_empty() {
                current_row.push(item);
                continue;
            }
            let current_worst = worst_aspect_ratio(&current_row, short_side);
            current_row.push(item);
            let next_worst = worst_aspect_ratio(&current_row, short_side);

            if next_worst > current_worst {
                current_row.pop();
                layout_row(&current_row, &mut cur_x, &mut cur_y, &mut cur_w, &mut cur_h, &mut output);
                current_row.clear();
                current_row.push(item);
            }
        }

        if !current_row.is_empty() {
            layout_row(&current_row, &mut cur_x, &mut cur_y, &mut cur_w, &mut cur_h, &mut output);
        }

        output
    }

    pub fn compute_squarified_layout(
        root: &TreemapNode,
        width: f32,
        height: f32,
        max_rects: usize,
    ) -> Vec<TreemapRect> {
        Self::build_squarified_treemap_layout(root, width, height, 2, max_rects.max(60), 4.0)
    }

    pub fn compute_extension_stats(
        root: &TreemapNode,
        settings: &AppSettings,
        is_de: bool,
        top_count: usize,
    ) -> Vec<StorageExtensionStat> {
        let no_ext_label = if is_de { "<keine Endung>" } else { "<no extension>" };
        let mut map: HashMap<String, (TreemapFileCategory, u64, usize)> = HashMap::new();

        fn walk(node: &TreemapNode, no_ext: &str, map: &mut HashMap<String, (TreemapFileCategory, u64, usize)>) {
            if !node.is_directory {
                let ext = if node.extension.is_empty() {
                    no_ext.to_string()
                } else {
                    node.extension.to_ascii_lowercase()
                };
                let entry = map.entry(ext).or_insert((node.category, 0, 0));
                entry.1 = entry.1.saturating_add(node.size_bytes);
                entry.2 += 1;
                return;
            }
            for child in &node.children {
                walk(child, no_ext, map);
            }
        }

        walk(root, no_ext_label, &mut map);

        let total = root.size_bytes.max(1) as f64;
        let mut stats: Vec<StorageExtensionStat> = map
            .into_iter()
            .map(|(ext, (cat, bytes, count))| StorageExtensionStat {
                extension: ext,
                category: cat,
                category_label: cat.display_name(is_de).to_string(),
                color_hex: cat.color_hex(settings),
                total_bytes: bytes,
                size_text: format_bytes(bytes),
                percentage: ((bytes as f64) * 100.0 / total).clamp(0.0, 100.0),
                file_count: count,
            })
            .collect();

        stats.sort_by(|a, b| {
            b.total_bytes
                .cmp(&a.total_bytes)
                .then_with(|| b.file_count.cmp(&a.file_count))
        });
        stats.truncate(top_count.max(1));
        stats
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
        if days <= 7 {
            "#EF4444" // Hot / <= 7 days
        } else if days <= 30 {
            "#F97316" // <= 30 days
        } else if days <= 180 {
            "#EAB308" // <= 6 months
        } else if days <= 365 {
            "#10B981" // <= 1 year
        } else if days <= 1095 {
            "#3B82F6" // 1-3 years
        } else {
            "#8B5CF6" // > 3 years
        }
    }

    pub fn remove_paths_from_tree(root: &mut TreemapNode, deleted_paths: &HashSet<String>) {
        let norm_deleted: Vec<String> = deleted_paths
            .iter()
            .map(|p| p.trim_end_matches(&['\\', '/'][..]).to_ascii_lowercase())
            .collect();
        Self::remove_paths_recursive(root, &norm_deleted);
    }

    fn remove_paths_recursive(root: &mut TreemapNode, norm_deleted: &[String]) {
        root.children.retain(|c| {
            let norm_c = c
                .full_path
                .trim_end_matches(&['\\', '/'][..])
                .to_ascii_lowercase();
            !norm_deleted.contains(&norm_c)
        });
        for child in &mut root.children {
            if child.is_directory {
                Self::remove_paths_recursive(child, norm_deleted);
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
        let default_settings = AppSettings::default();
        let top = Self::get_top_largest_files(root, 50);
        let ext_stats = Self::compute_extension_stats(root, &default_settings, true, 30);
        let total = root.size_bytes.max(1) as f64;

        if markdown_format {
            let mut out = String::new();
            out.push_str(&format!("# ModernExplorer Storage Report — `{}`\n", root.full_path));
            out.push_str(&format!(
                "- **Gesamtgröße:** {} ({} Bytes)\n",
                format_bytes(root.size_bytes),
                root.size_bytes
            ));
            out.push_str(&format!(
                "- **Dateien:** {} | **Ordner:** {}\n",
                root.file_count, root.folder_count
            ));
            out.push_str(&format!(
                "- **Erstellt am:** {}\n\n",
                Local::now().format("%Y-%m-%d %H:%M:%S")
            ));

            out.push_str("## Top-Unterordner\n");
            out.push_str("| Name | Größe | Anteil | Dateien |\n| :--- | ---: | ---: | ---: |\n");
            for child in root.children.iter().take(25) {
                let pct = ((child.size_bytes as f64) * 100.0 / total).clamp(0.0, 100.0);
                out.push_str(&format!(
                    "| `{}` | {} | {:.1}% | {} |\n",
                    child.name,
                    format_bytes(child.size_bytes),
                    pct,
                    child.file_count
                ));
            }
            out.push('\n');

            out.push_str("## Dateityp-Statistik\n");
            out.push_str("| Endung | Kategorie | Größe | Anteil | Anzahl |\n| :--- | :--- | ---: | ---: | ---: |\n");
            for ext in ext_stats {
                out.push_str(&format!(
                    "| `{}` | {} | {} | {:.1}% | {} |\n",
                    ext.extension, ext.category_label, ext.size_text, ext.percentage, ext.file_count
                ));
            }
            out.push('\n');

            out.push_str("## Top 50 Größte Dateien\n");
            out.push_str("| Datei | Größe | Geändert | Relativer Pfad |\n| :--- | ---: | :--- | :--- |\n");
            for item in top {
                out.push_str(&format!(
                    "| `{}` | {} | {} | `{}` |\n",
                    item.name,
                    format_bytes(item.size_bytes),
                    item.modified.format("%Y-%m-%d"),
                    item.relative_path
                ));
            }
            out
        } else {
            let mut out = String::from("Category,Name,RelativePath,SizeBytes,SizeFormatted,Percentage,FileCount,Modified\n");
            for child in &root.children {
                let pct = ((child.size_bytes as f64) * 100.0 / total).clamp(0.0, 100.0);
                out.push_str(&format!(
                    "Subfolder,\"{}\",\"{}\",{},{},{:.2},{},{}\n",
                    child.name.replace('"', "\"\""),
                    child.relative_path.replace('"', "\"\""),
                    child.size_bytes,
                    format_bytes(child.size_bytes),
                    pct,
                    child.file_count,
                    child.modified.format("%Y-%m-%d %H:%M:%S")
                ));
            }
            for item in top {
                let pct = ((item.size_bytes as f64) * 100.0 / total).clamp(0.0, 100.0);
                out.push_str(&format!(
                    "TopFile,\"{}\",\"{}\",{},{},{:.2},1,{}\n",
                    item.name.replace('"', "\"\""),
                    item.relative_path.replace('"', "\"\""),
                    item.size_bytes,
                    format_bytes(item.size_bytes),
                    pct,
                    item.modified.format("%Y-%m-%d %H:%M:%S")
                ));
            }
            out
        }
    }
}
