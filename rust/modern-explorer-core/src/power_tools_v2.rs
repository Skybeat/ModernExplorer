use crate::app_paths::AppPaths;
use crate::filesystem::{format_bytes, FileEntry};
use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use chrono::Local;
use image::{DynamicImage, GenericImageView, ImageFormat, Rgba, RgbaImage};
use lopdf::{Document, Object, ObjectId};
use notify::{Config, Event, RecommendedWatcher, RecursiveMode, Watcher};
use pbkdf2::pbkdf2_hmac;
use qrcodegen::{QrCode, QrCodeEcc};
use rand::RngCore;
use regex::Regex;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::{BTreeMap, HashSet};
use std::fs::{self, File};
use std::io::{Read, Write};
use std::net::{TcpListener, UdpSocket};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{
    atomic::{AtomicBool, AtomicU64, Ordering},
    Arc, Mutex,
};
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};
use sysinfo::System;

// ============================================================================
// 2. ERWEITERTE AUSWAHL-ENGINE (Num +, Num -, Num *, Presets & Pane Compare)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SelectionRule {
    pub name: String,
    pub pattern: String,
    pub is_regex: bool,
    pub min_size_bytes: Option<u64>,
    pub max_size_bytes: Option<u64>,
    pub max_age_hours: Option<u64>,
    pub min_age_hours: Option<u64>,
    pub target_type: String, // "all", "files", "folders"
}

impl Default for SelectionRule {
    fn default() -> Self {
        Self {
            name: "Standard".to_string(),
            pattern: "*".to_string(),
            is_regex: false,
            min_size_bytes: None,
            max_size_bytes: None,
            max_age_hours: None,
            min_age_hours: None,
            target_type: "all".to_string(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SelectionPresetsStore {
    pub presets: Vec<SelectionRule>,
}

impl Default for SelectionPresetsStore {
    fn default() -> Self {
        Self {
            presets: vec![
                SelectionRule {
                    name: "Heute geändert (< 24h)".to_string(),
                    pattern: "*".to_string(),
                    is_regex: false,
                    min_size_bytes: None,
                    max_size_bytes: None,
                    max_age_hours: Some(24),
                    min_age_hours: None,
                    target_type: "files".to_string(),
                },
                SelectionRule {
                    name: "Große Dateien (> 100 MB)".to_string(),
                    pattern: "*".to_string(),
                    is_regex: false,
                    min_size_bytes: Some(100 * 1024 * 1024),
                    max_size_bytes: None,
                    max_age_hours: None,
                    min_age_hours: None,
                    target_type: "files".to_string(),
                },
                SelectionRule {
                    name: "Quellcode (*.rs;*.ts;*.js;*.py;*.cs;*.json;*.toml)".to_string(),
                    pattern: "*.rs;*.ts;*.js;*.py;*.cs;*.json;*.toml".to_string(),
                    is_regex: false,
                    min_size_bytes: None,
                    max_size_bytes: None,
                    max_age_hours: None,
                    min_age_hours: None,
                    target_type: "files".to_string(),
                },
                SelectionRule {
                    name: "Bilder (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.svg)".to_string(),
                    pattern: "*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.svg".to_string(),
                    is_regex: false,
                    min_size_bytes: None,
                    max_size_bytes: None,
                    max_age_hours: None,
                    min_age_hours: None,
                    target_type: "files".to_string(),
                },
                SelectionRule {
                    name: "Archive (*.zip;*.7z;*.rar;*.tar.gz;*.tar.zst;*.tar.xz)".to_string(),
                    pattern: "*.zip;*.7z;*.rar;*.tar.gz;*.tar.zst;*.tar.xz".to_string(),
                    is_regex: false,
                    min_size_bytes: None,
                    max_size_bytes: None,
                    max_age_hours: None,
                    min_age_hours: None,
                    target_type: "files".to_string(),
                },
            ],
        }
    }
}

impl SelectionPresetsStore {
    pub fn load() -> Self {
        let p = AppPaths::selection_presets_file();
        if let Ok(txt) = fs::read_to_string(&p) {
            if let Ok(s) = serde_json::from_str::<Self>(&txt) {
                return s;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let p = AppPaths::selection_presets_file();
        if let Ok(txt) = serde_json::to_string_pretty(self) {
            let _ = fs::write(p, txt);
        }
    }

    pub fn evaluate_entries(entries: &[FileEntry], rule: &SelectionRule) -> Vec<String> {
        let now = Local::now();
        let compiled_regexes: Vec<Regex> = if rule.is_regex {
            Regex::new(&format!("(?i){}", rule.pattern)).into_iter().collect()
        } else {
            rule.pattern
                .split(|c| c == ';' || c == ',')
                .map(|s| s.trim())
                .filter(|s| !s.is_empty())
                .filter_map(|pat| {
                    let mut rx = String::from("(?i)^");
                    for ch in pat.chars() {
                        match ch {
                            '*' => rx.push_str(".*"),
                            '?' => rx.push('.'),
                            _ => rx.push_str(&regex::escape(&ch.to_string())),
                        }
                    }
                    rx.push('$');
                    Regex::new(&rx).ok()
                })
                .collect()
        };

        entries
            .iter()
            .filter(|e| {
                match rule.target_type.as_str() {
                    "files" if e.is_directory => return false,
                    "folders" if !e.is_directory => return false,
                    _ => {}
                }
                if let Some(min_sz) = rule.min_size_bytes {
                    if e.effective_size() < min_sz {
                        return false;
                    }
                }
                if let Some(max_sz) = rule.max_size_bytes {
                    if e.effective_size() > max_sz {
                        return false;
                    }
                }
                let age_hours = now
                    .signed_duration_since(e.modified)
                    .num_minutes()
                    .max(0) as u64
                    / 60;
                if let Some(max_h) = rule.max_age_hours {
                    if age_hours > max_h {
                        return false;
                    }
                }
                if let Some(min_h) = rule.min_age_hours {
                    if age_hours < min_h {
                        return false;
                    }
                }
                if compiled_regexes.is_empty() {
                    true
                } else {
                    compiled_regexes.iter().any(|r| r.is_match(&e.name))
                }
            })
            .map(|e| e.full_path.clone())
            .collect()
    }
}

// ============================================================================
// 3. WARTESCHLANGEN-MANAGER (Background Transfer Queue mit Pause & Speed-Limit)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TransferQueueItem {
    pub id: u64,
    pub sources: Vec<String>,
    pub destination_dir: String,
    pub is_move: bool,
    pub status: String, // "Queued", "Running", "Paused", "Done", "Error", "Cancelled"
    pub current_file: String,
    pub bytes_done: u64,
    pub bytes_total: u64,
    pub speed_bps: u64,
    pub error: Option<String>,
}

pub struct TransferQueueManager {
    pub items: Arc<Mutex<Vec<TransferQueueItem>>>,
    pub paused: Arc<AtomicBool>,
    pub speed_limit_kbps: Arc<AtomicU64>, // 0 = unlimited
    pub worker_running: Arc<AtomicBool>,
    next_id: u64,
}

impl Default for TransferQueueManager {
    fn default() -> Self {
        Self::new()
    }
}

impl TransferQueueManager {
    pub fn new() -> Self {
        Self {
            items: Arc::new(Mutex::new(Vec::new())),
            paused: Arc::new(AtomicBool::new(false)),
            speed_limit_kbps: Arc::new(AtomicU64::new(0)),
            worker_running: Arc::new(AtomicBool::new(false)),
            next_id: 1,
        }
    }

    pub fn enqueue(&mut self, sources: Vec<String>, destination_dir: String, is_move: bool) -> u64 {
        let id = self.next_id;
        self.next_id += 1;
        let mut total = 0u64;
        for s in &sources {
            let p = Path::new(s);
            if p.is_file() {
                total += fs::metadata(p).map(|m| m.len()).unwrap_or(0);
            } else if p.is_dir() {
                for e in walkdir::WalkDir::new(p).into_iter().filter_map(|e| e.ok()) {
                    if e.file_type().is_file() {
                        total += e.metadata().map(|m| m.len()).unwrap_or(0);
                    }
                }
            }
        }

        if let Ok(mut guard) = self.items.lock() {
            guard.push(TransferQueueItem {
                id,
                sources,
                destination_dir,
                is_move,
                status: "Queued".to_string(),
                current_file: String::new(),
                bytes_done: 0,
                bytes_total: total.max(1),
                speed_bps: 0,
                error: None,
            });
        }
        self.ensure_worker();
        id
    }

    fn ensure_worker(&self) {
        if self
            .worker_running
            .compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
            .is_err()
        {
            return;
        }
        let items_arc = self.items.clone();
        let paused_arc = self.paused.clone();
        let limit_arc = self.speed_limit_kbps.clone();
        let running_arc = self.worker_running.clone();

        thread::spawn(move || {
            loop {
                while paused_arc.load(Ordering::Relaxed) {
                    thread::sleep(Duration::from_millis(120));
                }

                let next_job = {
                    let mut g = items_arc.lock().unwrap();
                    if let Some(pos) = g.iter().position(|i| i.status == "Queued") {
                        g[pos].status = "Running".to_string();
                        Some(g[pos].clone())
                    } else {
                        None
                    }
                };

                let Some(job) = next_job else {
                    running_arc.store(false, Ordering::SeqCst);
                    break;
                };

                let res = Self::run_single_job(&job, &items_arc, &paused_arc, &limit_arc);
                if let Ok(mut g) = items_arc.lock() {
                    if let Some(it) = g.iter_mut().find(|i| i.id == job.id) {
                        if it.status != "Cancelled" {
                            match res {
                                Ok(()) => {
                                    it.status = "Done".to_string();
                                    it.bytes_done = it.bytes_total;
                                    it.speed_bps = 0;
                                }
                                Err(e) => {
                                    it.status = "Error".to_string();
                                    it.error = Some(e);
                                    it.speed_bps = 0;
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    fn run_single_job(
        job: &TransferQueueItem,
        items_arc: &Arc<Mutex<Vec<TransferQueueItem>>>,
        paused_arc: &Arc<AtomicBool>,
        limit_arc: &Arc<AtomicU64>,
    ) -> Result<(), String> {
        let dest_root = Path::new(&job.destination_dir);
        fs::create_dir_all(dest_root).map_err(|e| e.to_string())?;
        let mut bytes_done = 0u64;
        let start_time = Instant::now();

        for src_str in &job.sources {
            let src_path = Path::new(src_str);
            if !src_path.exists() {
                continue;
            }
            let file_name = src_path
                .file_name()
                .ok_or_else(|| "Ungültiger Dateiname".to_string())?;
            let target_path = dest_root.join(file_name);

            if src_path.is_file() {
                Self::copy_file_throttled(
                    src_path,
                    &target_path,
                    job.id,
                    &mut bytes_done,
                    start_time,
                    items_arc,
                    paused_arc,
                    limit_arc,
                )?;
                if job.is_move {
                    let _ = fs::remove_file(src_path);
                }
            } else if src_path.is_dir() {
                for entry in walkdir::WalkDir::new(src_path)
                    .into_iter()
                    .filter_map(|e| e.ok())
                {
                    let rel = entry.path().strip_prefix(src_path).unwrap_or(entry.path());
                    let out = target_path.join(rel);
                    if entry.file_type().is_dir() {
                        fs::create_dir_all(&out).map_err(|e| e.to_string())?;
                    } else if entry.file_type().is_file() {
                        Self::copy_file_throttled(
                            entry.path(),
                            &out,
                            job.id,
                            &mut bytes_done,
                            start_time,
                            items_arc,
                            paused_arc,
                            limit_arc,
                        )?;
                    }
                }
                if job.is_move {
                    let _ = fs::remove_dir_all(src_path);
                }
            }
        }
        Ok(())
    }

    #[allow(clippy::too_many_arguments)]
    fn copy_file_throttled(
        src: &Path,
        dst: &Path,
        job_id: u64,
        bytes_done: &mut u64,
        start_time: Instant,
        items_arc: &Arc<Mutex<Vec<TransferQueueItem>>>,
        paused_arc: &Arc<AtomicBool>,
        limit_arc: &Arc<AtomicU64>,
    ) -> Result<(), String> {
        if let Some(p) = dst.parent() {
            fs::create_dir_all(p).map_err(|e| e.to_string())?;
        }
        let mut reader = File::open(src).map_err(|e| e.to_string())?;
        let mut writer = File::create(dst).map_err(|e| e.to_string())?;
        let mut buf = vec![0u8; 65536];
        let name = src
            .file_name()
            .map(|n| n.to_string_lossy().to_string())
            .unwrap_or_default();

        loop {
            while paused_arc.load(Ordering::Relaxed) {
                thread::sleep(Duration::from_millis(100));
            }
            if let Ok(g) = items_arc.lock() {
                if let Some(it) = g.iter().find(|i| i.id == job_id) {
                    if it.status == "Cancelled" {
                        return Err("Abgebrochen".to_string());
                    }
                }
            }

            let chunk_start = Instant::now();
            let n = reader.read(&mut buf).map_err(|e| e.to_string())?;
            if n == 0 {
                break;
            }
            writer.write_all(&buf[..n]).map_err(|e| e.to_string())?;
            *bytes_done += n as u64;

            let elapsed_secs = start_time.elapsed().as_secs_f64().max(0.05);
            let speed = (*bytes_done as f64 / elapsed_secs) as u64;

            if let Ok(mut g) = items_arc.lock() {
                if let Some(it) = g.iter_mut().find(|i| i.id == job_id) {
                    it.current_file = name.clone();
                    it.bytes_done = *bytes_done;
                    it.speed_bps = speed;
                }
            }

            let limit_kbps = limit_arc.load(Ordering::Relaxed);
            if limit_kbps > 0 {
                let target_secs = (n as f64) / ((limit_kbps as f64) * 1024.0);
                let actual_secs = chunk_start.elapsed().as_secs_f64();
                if target_secs > actual_secs {
                    thread::sleep(Duration::from_secs_f64(target_secs - actual_secs));
                }
            }
        }
        Ok(())
    }
}

// ============================================================================
// 4 & 5. ORDNER-BAUM & VERZEICHNIS-VERLAUF (zoxide / Frecency Smart Jump)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DirectoryTreeNodeDto {
    pub name: String,
    pub full_path: String,
    pub has_children: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FrecencyEntry {
    pub path: String,
    pub visit_count: u64,
    pub last_visited_unix: u64,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct FrecencyStore {
    pub entries: Vec<FrecencyEntry>,
}

impl FrecencyStore {
    pub fn load() -> Self {
        let p = AppPaths::frecency_file();
        if let Ok(txt) = fs::read_to_string(&p) {
            if let Ok(s) = serde_json::from_str::<Self>(&txt) {
                return s;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let p = AppPaths::frecency_file();
        if let Ok(txt) = serde_json::to_string_pretty(self) {
            let _ = fs::write(p, txt);
        }
    }

    pub fn record_visit(&mut self, dir_path: &str) {
        if dir_path.is_empty() || dir_path.starts_with("archive://") {
            return;
        }
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        if let Some(existing) = self
            .entries
            .iter_mut()
            .find(|e| e.path.eq_ignore_ascii_case(dir_path))
        {
            existing.visit_count += 1;
            existing.last_visited_unix = now;
        } else {
            self.entries.push(FrecencyEntry {
                path: dir_path.to_string(),
                visit_count: 1,
                last_visited_unix: now,
            });
        }
        if self.entries.len() > 500 {
            self.entries
                .sort_by(|a, b| b.last_visited_unix.cmp(&a.last_visited_unix));
            self.entries.truncate(500);
        }
        self.save();
    }

    pub fn query_smart_jump(&self, query: &str, max_results: usize) -> Vec<FrecencyEntry> {
        let tokens: Vec<String> = query
            .split_whitespace()
            .map(|t| t.to_lowercase())
            .collect();
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);

        let mut scored: Vec<(f64, FrecencyEntry)> = self
            .entries
            .iter()
            .filter(|e| {
                let lower = e.path.to_lowercase();
                tokens.iter().all(|t| lower.contains(t))
            })
            .map(|e| {
                let age_hours = ((now.saturating_sub(e.last_visited_unix)) as f64) / 3600.0;
                let recency_weight = if age_hours < 1.0 {
                    4.0
                } else if age_hours < 24.0 {
                    2.0
                } else if age_hours < 168.0 {
                    1.0
                } else {
                    0.5
                };
                let score = (e.visit_count as f64) * recency_weight;
                (score, e.clone())
            })
            .collect();

        scored.sort_by(|a, b| b.0.partial_cmp(&a.0).unwrap_or(std::cmp::Ordering::Equal));
        scored
            .into_iter()
            .take(max_results.max(1))
            .map(|(_, e)| e)
            .collect()
    }

    pub fn list_subdirectories(parent: &Path, show_hidden: bool) -> Vec<DirectoryTreeNodeDto> {
        let mut dirs = Vec::new();
        if let Ok(rd) = fs::read_dir(parent) {
            for entry in rd.flatten() {
                let p = entry.path();
                if p.is_dir() {
                    let name = p
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_default();
                    if !show_hidden && name.starts_with('.') {
                        continue;
                    }
                    let has_children = fs::read_dir(&p)
                        .map(|mut sub| sub.any(|s| s.ok().map(|x| x.path().is_dir()).unwrap_or(false)))
                        .unwrap_or(false);
                    dirs.push(DirectoryTreeNodeDto {
                        name,
                        full_path: p.to_string_lossy().to_string(),
                        has_children,
                    });
                }
            }
        }
        dirs.sort_by(|a, b| a.name.to_lowercase().cmp(&b.name.to_lowercase()));
        dirs
    }
}

// ============================================================================
// 6. BATCH-BILDKONVERTER & OPTIMIZER (image Crate)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ImageConvertOptions {
    pub target_format: String, // "png", "jpg", "webp", "bmp", "ico", "tiff"
    pub scale_percent: Option<u32>,
    pub max_width: Option<u32>,
    pub max_height: Option<u32>,
    pub rotate_degrees: u32, // 0, 90, 180, 270
    pub flip_horizontal: bool,
    pub flip_vertical: bool,
    pub grayscale: bool,
    pub watermark_banner: bool,
    pub output_dir: String,
    pub filename_suffix: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ImageConvertResultItem {
    pub source_path: String,
    pub output_path: String,
    pub original_bytes: u64,
    pub output_bytes: u64,
    pub dimensions: String,
    pub ok: bool,
    pub error: Option<String>,
}

pub struct ImageConverterService;

impl ImageConverterService {
    pub fn convert_batch(
        paths: &[String],
        opts: &ImageConvertOptions,
    ) -> Vec<ImageConvertResultItem> {
        let mut results = Vec::new();
        for p_str in paths {
            let src = Path::new(p_str);
            let orig_bytes = fs::metadata(src).map(|m| m.len()).unwrap_or(0);
            match Self::convert_single(src, opts) {
                Ok((out_path, out_bytes, dims)) => results.push(ImageConvertResultItem {
                    source_path: p_str.clone(),
                    output_path: out_path,
                    original_bytes: orig_bytes,
                    output_bytes: out_bytes,
                    dimensions: dims,
                    ok: true,
                    error: None,
                }),
                Err(e) => results.push(ImageConvertResultItem {
                    source_path: p_str.clone(),
                    output_path: String::new(),
                    original_bytes: orig_bytes,
                    output_bytes: 0,
                    dimensions: String::new(),
                    ok: false,
                    error: Some(e),
                }),
            }
        }
        results
    }

    fn convert_single(
        src: &Path,
        opts: &ImageConvertOptions,
    ) -> Result<(String, u64, String), String> {
        let mut img = image::open(src).map_err(|e| format!("Bild laden fehlgeschlagen: {}", e))?;

        if let Some(pct) = opts.scale_percent {
            if pct > 0 && pct != 100 {
                let (w, h) = img.dimensions();
                let nw = ((w as u64 * pct as u64) / 100).max(1) as u32;
                let nh = ((h as u64 * pct as u64) / 100).max(1) as u32;
                img = img.resize_exact(nw, nh, image::imageops::FilterType::Lanczos3);
            }
        } else if opts.max_width.is_some() || opts.max_height.is_some() {
            let (w, h) = img.dimensions();
            let mw = opts.max_width.unwrap_or(w).max(1);
            let mh = opts.max_height.unwrap_or(h).max(1);
            img = img.resize(mw, mh, image::imageops::FilterType::Lanczos3);
        }

        match opts.rotate_degrees {
            90 => img = img.rotate90(),
            180 => img = img.rotate180(),
            270 => img = img.rotate270(),
            _ => {}
        }

        if opts.flip_horizontal {
            img = img.fliph();
        }
        if opts.flip_vertical {
            img = img.flipv();
        }
        if opts.grayscale {
            img = DynamicImage::ImageLuma8(img.to_luma8());
        }
        if opts.watermark_banner {
            let mut rgba: RgbaImage = img.to_rgba8();
            let (w, h) = rgba.dimensions();
            let bar_h = (h / 12).clamp(16, 48);
            for y in (h.saturating_sub(bar_h))..h {
                for x in 0..w {
                    let px = rgba.get_pixel_mut(x, y);
                    px[0] = ((px[0] as u16 + 30) / 2) as u8;
                    px[1] = ((px[1] as u16 + 120) / 2) as u8;
                    px[2] = ((px[2] as u16 + 220) / 2) as u8;
                    px[3] = 255;
                    if (x + y) % 8 == 0 {
                        *px = Rgba([255, 255, 255, 220]);
                    }
                }
            }
            img = DynamicImage::ImageRgba8(rgba);
        }

        let ext = match opts.target_format.to_ascii_lowercase().as_str() {
            "jpg" | "jpeg" => "jpg",
            "webp" => "webp",
            "bmp" => "bmp",
            "ico" => "ico",
            "tiff" | "tif" => "tiff",
            _ => "png",
        };
        let fmt = match ext {
            "jpg" => ImageFormat::Jpeg,
            "webp" => ImageFormat::WebP,
            "bmp" => ImageFormat::Bmp,
            "ico" => {
                let (w, h) = img.dimensions();
                if w > 256 || h > 256 {
                    img = img.resize(256, 256, image::imageops::FilterType::Lanczos3);
                }
                ImageFormat::Ico
            }
            "tiff" => ImageFormat::Tiff,
            _ => ImageFormat::Png,
        };

        let stem = src
            .file_stem()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| "image".to_string());
        let out_dir = if opts.output_dir.trim().is_empty() {
            src.parent().unwrap_or_else(|| Path::new(".")).to_path_buf()
        } else {
            PathBuf::from(&opts.output_dir)
        };
        fs::create_dir_all(&out_dir).map_err(|e| e.to_string())?;
        let out_path = out_dir.join(format!("{}{}.{}", stem, opts.filename_suffix, ext));

        let (fw, fh) = img.dimensions();
        img.save_with_format(&out_path, fmt)
            .map_err(|e| format!("Speichern fehlgeschlagen: {}", e))?;
        let out_bytes = fs::metadata(&out_path).map(|m| m.len()).unwrap_or(0);
        Ok((
            out_path.to_string_lossy().to_string(),
            out_bytes,
            format!("{}x{}", fw, fh),
        ))
    }
}

// ============================================================================
// 8. PDF-WERKZEUGKASTEN (Split, Merge, Rotate, Inspect via lopdf)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PdfInfoDto {
    pub path: String,
    pub page_count: usize,
    pub version: String,
    pub size_text: String,
}

pub struct PdfToolkitService;

impl PdfToolkitService {
    pub fn inspect_pdf(path: &Path) -> Result<PdfInfoDto, String> {
        let doc = Document::load(path).map_err(|e| format!("PDF Ladefehler: {}", e))?;
        let size = fs::metadata(path).map(|m| m.len()).unwrap_or(0);
        Ok(PdfInfoDto {
            path: path.to_string_lossy().to_string(),
            page_count: doc.get_pages().len(),
            version: doc.version.clone(),
            size_text: format_bytes(size),
        })
    }

    pub fn rotate_pdf_pages(
        input_path: &Path,
        output_path: &Path,
        degrees: i64,
    ) -> Result<usize, String> {
        let mut doc = Document::load(input_path).map_err(|e| format!("PDF Ladefehler: {}", e))?;
        let pages = doc.get_pages();
        let mut rotated = 0usize;
        for (_, page_id) in pages {
            if let Ok(page_obj) = doc.get_object_mut(page_id) {
                if let Ok(dict) = page_obj.as_dict_mut() {
                    let current = dict
                        .get(b"Rotate")
                        .ok()
                        .and_then(|o| o.as_i64().ok())
                        .unwrap_or(0);
                    let next_rot = (current + degrees).rem_euclid(360);
                    dict.set("Rotate", Object::Integer(next_rot));
                    rotated += 1;
                }
            }
        }
        doc.save(output_path)
            .map_err(|e| format!("PDF Speicherfehler: {}", e))?;
        Ok(rotated)
    }

    pub fn extract_page_range(
        input_path: &Path,
        output_path: &Path,
        keep_pages_1_based: &[u32],
    ) -> Result<usize, String> {
        let mut doc = Document::load(input_path).map_err(|e| format!("PDF Ladefehler: {}", e))?;
        let all_pages = doc.get_pages();
        let keep_set: HashSet<u32> = keep_pages_1_based.iter().copied().collect();
        let to_delete: Vec<u32> = all_pages
            .keys()
            .copied()
            .filter(|p| !keep_set.contains(p))
            .collect();
        if to_delete.len() == all_pages.len() {
            return Err("Mindestens eine gültige Seite muss erhalten bleiben".to_string());
        }
        doc.delete_pages(&to_delete);
        doc.prune_objects();
        doc.save(output_path)
            .map_err(|e| format!("PDF Speicherfehler: {}", e))?;
        Ok(all_pages.len() - to_delete.len())
    }

    pub fn merge_pdfs(input_paths: &[String], output_path: &Path) -> Result<usize, String> {
        if input_paths.is_empty() {
            return Err("Keine PDF-Dateien ausgewählt".to_string());
        }
        let mut max_id = 1;
        let mut documents_pages: BTreeMap<ObjectId, Object> = BTreeMap::new();
        let mut documents_objects: BTreeMap<ObjectId, Object> = BTreeMap::new();
        let mut document = Document::with_version("1.5");

        for p_str in input_paths {
            let mut doc = Document::load(p_str).map_err(|e| format!("PDF Fehler ({}): {}", p_str, e))?;
            doc.renumber_objects_with(max_id);
            max_id = doc.max_id + 1;
            for (_, object_id) in doc.get_pages() {
                if let Ok(obj) = doc.get_object(object_id) {
                    documents_pages.insert(object_id, obj.to_owned());
                }
            }
            documents_objects.extend(doc.objects);
        }

        let mut catalog_object: Option<(ObjectId, Object)> = None;
        let mut pages_object: Option<(ObjectId, Object)> = None;

        for (object_id, object) in documents_objects.iter() {
            match object.type_name().unwrap_or("") {
                "Catalog" => {
                    catalog_object = Some((
                        if let Some((id, _)) = catalog_object {
                            id
                        } else {
                            *object_id
                        },
                        object.clone(),
                    ));
                }
                "Pages" => {
                    if let Ok(dictionary) = object.as_dict() {
                        let mut dictionary = dictionary.clone();
                        if let Some((_, ref object)) = pages_object {
                            if let Ok(old_dictionary) = object.as_dict() {
                                dictionary.extend(old_dictionary);
                            }
                        }
                        pages_object = Some((
                            if let Some((id, _)) = pages_object {
                                id
                            } else {
                                *object_id
                            },
                            Object::Dictionary(dictionary),
                        ));
                    }
                }
                "Page" | "Outlines" | "Outline" => {}
                _ => {
                    document.objects.insert(*object_id, object.clone());
                }
            }
        }

        let (page_id, page_obj) = pages_object.ok_or_else(|| "Pages root fehlt".to_string())?;
        for (object_id, object) in documents_pages.iter() {
            if let Ok(dictionary) = object.as_dict() {
                let mut dictionary = dictionary.clone();
                dictionary.set("Parent", page_id);
                document
                    .objects
                    .insert(*object_id, Object::Dictionary(dictionary));
            }
        }

        let (catalog_id, catalog_obj) =
            catalog_object.ok_or_else(|| "Catalog root fehlt".to_string())?;
        if let Ok(dictionary) = page_obj.as_dict() {
            let mut dictionary = dictionary.clone();
            dictionary.set("Count", documents_pages.len() as u32);
            dictionary.set(
                "Kids",
                documents_pages
                    .into_keys()
                    .map(Object::Reference)
                    .collect::<Vec<_>>(),
            );
            document
                .objects
                .insert(page_id, Object::Dictionary(dictionary));
        }

        if let Ok(dictionary) = catalog_obj.as_dict() {
            let mut dictionary = dictionary.clone();
            dictionary.set("Pages", page_id);
            dictionary.remove(b"Outlines");
            document
                .objects
                .insert(catalog_id, Object::Dictionary(dictionary));
        }

        document.trailer.set("Root", catalog_id);
        document.max_id = document.objects.len() as u32;
        document.renumber_objects();
        document.compress();
        document
            .save(output_path)
            .map_err(|e| format!("PDF Merge Speicherfehler: {}", e))?;
        Ok(input_paths.len())
    }
}

// ============================================================================
// 9. DATEIVERSCHLÜSSELUNG (Vault / AES-256-GCM .menc)
// ============================================================================

pub struct FileVaultService;

impl FileVaultService {
    const MAGIC: &'static [u8; 8] = b"MENC0001";
    const PBKDF2_ITERS: u32 = 100_000;

    fn derive_key(password: &str, keyfile: Option<&Path>, salt: &[u8; 16]) -> Result<[u8; 32], String> {
        let mut secret = password.as_bytes().to_vec();
        if let Some(kf) = keyfile {
            if kf.is_file() {
                let kf_bytes = fs::read(kf).map_err(|e| format!("Keyfile Fehler: {}", e))?;
                let kf_hash = Sha256::digest(&kf_bytes);
                secret.extend_from_slice(&kf_hash);
            }
        }
        let mut key = [0u8; 32];
        pbkdf2_hmac::<Sha256>(&secret, salt, Self::PBKDF2_ITERS, &mut key);
        Ok(key)
    }

    pub fn encrypt_file(
        source_path: &Path,
        output_menc_path: &Path,
        password: &str,
        keyfile: Option<&Path>,
    ) -> Result<u64, String> {
        if password.is_empty() && keyfile.is_none() {
            return Err("Passwort oder Keyfile erforderlich".to_string());
        }
        let plaintext = fs::read(source_path).map_err(|e| e.to_string())?;
        let mut salt = [0u8; 16];
        let mut nonce_bytes = [0u8; 12];
        rand::thread_rng().fill_bytes(&mut salt);
        rand::thread_rng().fill_bytes(&mut nonce_bytes);

        let key = Self::derive_key(password, keyfile, &salt)?;
        let cipher = Aes256Gcm::new_from_slice(&key).map_err(|e| e.to_string())?;
        let nonce = Nonce::from_slice(&nonce_bytes);
        let ciphertext = cipher
            .encrypt(nonce, plaintext.as_ref())
            .map_err(|_| "AES-256-GCM Verschlüsselung fehlgeschlagen".to_string())?;

        let mut out = File::create(output_menc_path).map_err(|e| e.to_string())?;
        out.write_all(Self::MAGIC).map_err(|e| e.to_string())?;
        out.write_all(&salt).map_err(|e| e.to_string())?;
        out.write_all(&nonce_bytes).map_err(|e| e.to_string())?;
        out.write_all(&ciphertext).map_err(|e| e.to_string())?;
        Ok((Self::MAGIC.len() + 16 + 12 + ciphertext.len()) as u64)
    }

    pub fn decrypt_file(
        source_menc_path: &Path,
        output_path: &Path,
        password: &str,
        keyfile: Option<&Path>,
    ) -> Result<u64, String> {
        let data = fs::read(source_menc_path).map_err(|e| e.to_string())?;
        if data.len() < 8 + 16 + 12 + 16 || &data[0..8] != Self::MAGIC {
            return Err("Ungültiges .menc Tresor-Format".to_string());
        }
        let mut salt = [0u8; 16];
        salt.copy_from_slice(&data[8..24]);
        let nonce = Nonce::from_slice(&data[24..36]);
        let ciphertext = &data[36..];

        let key = Self::derive_key(password, keyfile, &salt)?;
        let cipher = Aes256Gcm::new_from_slice(&key).map_err(|e| e.to_string())?;
        let plaintext = cipher
            .decrypt(nonce, ciphertext)
            .map_err(|_| "Entschlüsselung fehlgeschlagen: Falsches Passwort / Keyfile oder manipulierte Datei".to_string())?;

        fs::write(output_path, &plaintext).map_err(|e| e.to_string())?;
        Ok(plaintext.len() as u64)
    }
}

// ============================================================================
// 10. BERECHTIGUNGS- & NTFS-INSPECTOR (File Locksmith / ADS / ACL)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LockedProcessInfo {
    pub pid: u32,
    pub process_name: String,
    pub exe_path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdsStreamInfo {
    pub stream_name: String,
    pub size_bytes: u64,
    pub preview_text: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NtfsLockInspectResult {
    pub path: String,
    pub is_readonly: bool,
    pub acl_summary: String,
    pub locking_processes: Vec<LockedProcessInfo>,
    pub ads_streams: Vec<AdsStreamInfo>,
}

pub struct NtfsAndLockInspectorService;

impl NtfsAndLockInspectorService {
    pub fn inspect_path(path: &Path) -> NtfsLockInspectResult {
        let is_readonly = fs::metadata(path)
            .map(|m| m.permissions().readonly())
            .unwrap_or(false);
        let acl_summary = Self::read_acl_summary(path);
        let locking_processes = Self::find_locking_processes(path);
        let ads_streams = Self::list_ads_streams(path);

        NtfsLockInspectResult {
            path: path.to_string_lossy().to_string(),
            is_readonly,
            acl_summary,
            locking_processes,
            ads_streams,
        }
    }

    fn read_acl_summary(path: &Path) -> String {
        #[cfg(target_os = "windows")]
        {
            use std::os::windows::process::CommandExt;
            if let Ok(out) = Command::new("icacls")
                .arg(path)
                .creation_flags(0x08000000)
                .output()
            {
                let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
                if !s.is_empty() {
                    return s;
                }
            }
        }
        #[cfg(not(target_os = "windows"))]
        {
            if let Ok(out) = Command::new("ls").arg("-ld").arg(path).output() {
                let s = String::from_utf8_lossy(&out.stdout).trim().to_string();
                if !s.is_empty() {
                    return s;
                }
            }
        }
        "Standard-Berechtigungen".to_string()
    }

    pub fn find_locking_processes(path: &Path) -> Vec<LockedProcessInfo> {
        let mut results = Vec::new();
        let target_norm = path.to_string_lossy().to_lowercase();
        let mut sys = System::new_all();
        sys.refresh_all();

        for (pid, proc_info) in sys.processes() {
            let exe_str = proc_info
                .exe()
                .map(|p| p.to_string_lossy().to_string())
                .unwrap_or_default();
            let cwd_str = proc_info
                .cwd()
                .map(|p| p.to_string_lossy().to_string())
                .unwrap_or_default();
            let cmd_joined = proc_info
                .cmd()
                .iter()
                .map(|s| s.to_string_lossy())
                .collect::<Vec<_>>()
                .join(" ");

            if (!exe_str.is_empty() && exe_str.to_lowercase().starts_with(&target_norm))
                || (!cwd_str.is_empty() && cwd_str.to_lowercase().starts_with(&target_norm))
                || cmd_joined.to_lowercase().contains(&target_norm)
            {
                results.push(LockedProcessInfo {
                    pid: pid.as_u32(),
                    process_name: proc_info.name().to_string_lossy().to_string(),
                    exe_path: exe_str,
                });
            }
        }
        results
    }

    pub fn kill_process_by_pid(pid: u32) -> Result<(), String> {
        let mut sys = System::new_all();
        sys.refresh_all();
        if let Some(p) = sys.process(sysinfo::Pid::from_u32(pid)) {
            if p.kill() {
                return Ok(());
            }
        }
        Err(format!("Prozess {} konnte nicht beendet werden", pid))
    }

    pub fn list_ads_streams(path: &Path) -> Vec<AdsStreamInfo> {
        let mut streams = Vec::new();
        let zone_path = format!("{}:Zone.Identifier", path.to_string_lossy());
        if let Ok(content) = fs::read_to_string(&zone_path) {
            streams.push(AdsStreamInfo {
                stream_name: ":Zone.Identifier".to_string(),
                size_bytes: content.len() as u64,
                preview_text: content.trim().to_string(),
            });
        }
        streams
    }

    pub fn unblock_zone_identifier(path: &Path) -> Result<bool, String> {
        let zone_path = format!("{}:Zone.Identifier", path.to_string_lossy());
        if Path::new(&zone_path).exists() || fs::metadata(&zone_path).is_ok() {
            fs::remove_file(&zone_path).map_err(|e| e.to_string())?;
            Ok(true)
        } else {
            Ok(false)
        }
    }
}

// ============================================================================
// 12. SMART CLIPBOARD HISTORY & STRUKTURIERTER DATEN-KONVERTER (JSON/YAML/TOML/CSV)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ClipboardHistoryEntry {
    pub text: String,
    pub kind: String,
    pub timestamp: String,
    pub pinned: bool,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ClipboardHistoryService {
    pub items: Vec<ClipboardHistoryEntry>,
}

impl ClipboardHistoryService {
    pub fn load() -> Self {
        let p = AppPaths::clipboard_history_file();
        if let Ok(txt) = fs::read_to_string(&p) {
            if let Ok(s) = serde_json::from_str::<Self>(&txt) {
                return s;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let p = AppPaths::clipboard_history_file();
        if let Ok(txt) = serde_json::to_string_pretty(self) {
            let _ = fs::write(p, txt);
        }
    }

    pub fn push_entry(&mut self, text: String, kind: &str) {
        if text.trim().is_empty() {
            return;
        }
        self.items.retain(|i| i.text != text);
        self.items.insert(
            0,
            ClipboardHistoryEntry {
                text,
                kind: kind.to_string(),
                timestamp: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                pinned: false,
            },
        );
        if self.items.len() > 50 {
            self.items.truncate(50);
        }
        self.save();
    }
}

pub struct DataConverterService;

impl DataConverterService {
    pub fn convert_structured_data(
        input: &str,
        source_format: &str,
        target_format: &str,
    ) -> Result<String, String> {
        let val: serde_json::Value = match source_format.trim().to_ascii_lowercase().as_str() {
            "json" => serde_json::from_str(input).map_err(|e| format!("JSON Parse-Fehler: {}", e))?,
            "yaml" | "yml" => {
                serde_yaml::from_str(input).map_err(|e| format!("YAML Parse-Fehler: {}", e))?
            }
            "toml" => {
                let tv: toml::Value =
                    toml::from_str(input).map_err(|e| format!("TOML Parse-Fehler: {}", e))?;
                serde_json::to_value(tv).map_err(|e| e.to_string())?
            }
            "csv" => Self::csv_to_json_value(input)?,
            _ => {
                // Auto-detect
                if let Ok(v) = serde_json::from_str(input) {
                    v
                } else if let Ok(tv) = toml::from_str::<toml::Value>(input) {
                    serde_json::to_value(tv).map_err(|e| e.to_string())?
                } else if let Ok(yv) = serde_yaml::from_str(input) {
                    yv
                } else {
                    Self::csv_to_json_value(input)?
                }
            }
        };

        match target_format.trim().to_ascii_lowercase().as_str() {
            "json" => serde_json::to_string_pretty(&val).map_err(|e| e.to_string()),
            "json-minified" => serde_json::to_string(&val).map_err(|e| e.to_string()),
            "yaml" | "yml" => serde_yaml::to_string(&val).map_err(|e| e.to_string()),
            "toml" => {
                let wrapped = if val.is_array() {
                    serde_json::json!({ "items": val })
                } else {
                    val
                };
                toml::to_string_pretty(&wrapped).map_err(|e| format!("TOML Export-Fehler: {}", e))
            }
            "csv" => Self::json_value_to_csv(&val),
            _ => serde_json::to_string_pretty(&val).map_err(|e| e.to_string()),
        }
    }

    fn csv_to_json_value(input: &str) -> Result<serde_json::Value, String> {
        let mut rdr = csv::ReaderBuilder::new()
            .has_headers(true)
            .from_reader(input.as_bytes());
        let headers = rdr
            .headers()
            .map_err(|e| format!("CSV Header-Fehler: {}", e))?
            .clone();
        let mut rows = Vec::new();
        for result in rdr.records() {
            let record = result.map_err(|e| format!("CSV Zeilen-Fehler: {}", e))?;
            let mut obj = serde_json::Map::new();
            for (idx, field) in record.iter().enumerate() {
                let key = headers
                    .get(idx)
                    .map(|s| s.to_string())
                    .unwrap_or_else(|| format!("col_{}", idx));
                obj.insert(key, serde_json::Value::String(field.to_string()));
            }
            rows.push(serde_json::Value::Object(obj));
        }
        Ok(serde_json::Value::Array(rows))
    }

    fn json_value_to_csv(val: &serde_json::Value) -> Result<String, String> {
        let arr = if let Some(a) = val.as_array() {
            a.clone()
        } else if let Some(obj) = val.as_object() {
            if let Some(inner) = obj.get("items").and_then(|v| v.as_array()) {
                inner.clone()
            } else {
                vec![val.clone()]
            }
        } else {
            return Err("CSV-Konvertierung erwartet ein Array oder Objekt".to_string());
        };

        if arr.is_empty() {
            return Ok(String::new());
        }

        let mut headers = Vec::new();
        for item in &arr {
            if let Some(obj) = item.as_object() {
                for k in obj.keys() {
                    if !headers.contains(k) {
                        headers.push(k.clone());
                    }
                }
            }
        }

        let mut wtr = csv::Writer::from_writer(Vec::new());
        wtr.write_record(&headers).map_err(|e| e.to_string())?;
        for item in &arr {
            if let Some(obj) = item.as_object() {
                let row: Vec<String> = headers
                    .iter()
                    .map(|h| match obj.get(h) {
                        Some(serde_json::Value::String(s)) => s.clone(),
                        Some(serde_json::Value::Null) | None => String::new(),
                        Some(other) => other.to_string(),
                    })
                    .collect();
                wtr.write_record(&row).map_err(|e| e.to_string())?;
            }
        }
        let bytes = wtr.into_inner().map_err(|e| e.to_string())?;
        Ok(String::from_utf8_lossy(&bytes).to_string())
    }
}

// ============================================================================
// 13. BENUTZERDEFINIERTE TOOLBAR-BUTTONS & EXTERNE TOOLS (User Commands)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserCommandItem {
    pub id: String,
    #[serde(alias = "name", default)]
    pub label: String,
    #[serde(default)]
    pub icon: String,
    #[serde(default)]
    pub program: String,
    #[serde(alias = "command_template", default)]
    pub args_template: String, // Supports %P/{dir}, %T/{other_dir}, %N/{file}, %F/{selected}, %L, %R
    #[serde(default)]
    pub shortcut: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserCommandStore {
    pub commands: Vec<UserCommandItem>,
}

impl Default for UserCommandStore {
    fn default() -> Self {
        Self {
            commands: vec![
                UserCommandItem {
                    id: "vscode".to_string(),
                    label: "VS Code".to_string(),
                    icon: "💻".to_string(),
                    program: "code".to_string(),
                    args_template: "%P".to_string(),
                    shortcut: "Ctrl+Alt+C".to_string(),
                },
                UserCommandItem {
                    id: "os_explorer".to_string(),
                    label: "System-Explorer".to_string(),
                    icon: "📂".to_string(),
                    program: if cfg!(target_os = "windows") {
                        "explorer".to_string()
                    } else if cfg!(target_os = "macos") {
                        "open".to_string()
                    } else {
                        "xdg-open".to_string()
                    },
                    args_template: "%P".to_string(),
                    shortcut: "Ctrl+Alt+E".to_string(),
                },
            ],
        }
    }
}

impl UserCommandStore {
    pub fn load() -> Self {
        let p = AppPaths::user_commands_file();
        if let Ok(txt) = fs::read_to_string(&p) {
            if let Ok(mut s) = serde_json::from_str::<Self>(&txt) {
                s.commands.retain(|c| !c.label.trim().is_empty() && (!c.program.trim().is_empty() || !c.args_template.trim().is_empty()));
                if !s.commands.is_empty() {
                    return s;
                }
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let p = AppPaths::user_commands_file();
        if let Ok(txt) = serde_json::to_string_pretty(self) {
            let _ = fs::write(p, txt);
        }
    }

    pub fn execute_command(
        cmd_item: &UserCommandItem,
        active_path: &str,
        target_path: &str,
        focused_file: &str,
        selected_files: &[String],
        left_path: &str,
        right_path: &str,
    ) -> Result<String, String> {
        let joined_files = selected_files.join(" ");
        let expand = |tpl: &str| -> String {
            tpl.replace("%P", active_path)
                .replace("{dir}", active_path)
                .replace("%T", target_path)
                .replace("{other_dir}", target_path)
                .replace("%N", focused_file)
                .replace("{file}", focused_file)
                .replace("%L", left_path)
                .replace("%R", right_path)
                .replace("%F", &joined_files)
                .replace("{selected}", &joined_files)
        };

        let (prog, expanded_args) = if cmd_item.program.trim().is_empty() {
            let full = expand(&cmd_item.args_template);
            let mut parts = full.trim().splitn(2, char::is_whitespace);
            let p = parts.next().unwrap_or("").to_string();
            let a = parts.next().unwrap_or("").to_string();
            (p, a)
        } else {
            (cmd_item.program.trim().to_string(), expand(&cmd_item.args_template))
        };

        if prog.is_empty() {
            return Err("Kein Programm angegeben.".to_string());
        }

        let mut cmd = Command::new(&prog);
        if !expanded_args.trim().is_empty() {
            cmd.arg(expanded_args.trim());
        }
        cmd.current_dir(active_path);
        cmd.spawn()
            .map_err(|e| format!("Start von '{}' fehlgeschlagen: {}", prog, e))?;
        Ok(format!("{} {}", prog, expanded_args.trim()).trim().to_string())
    }
}

// ============================================================================
// 14. LOKALER SCHNELL-DATEISERVER (Instant HTTP / QR-Code Share)
// ============================================================================

pub struct LocalShareServerState {
    pub running: Arc<AtomicBool>,
    pub url: String,
    pub qr_svg: String,
    pub shared_root: PathBuf,
    pub access_log: Arc<Mutex<Vec<String>>>,
}

impl Default for LocalShareServerState {
    fn default() -> Self {
        Self::new()
    }
}

impl LocalShareServerState {
    pub fn new() -> Self {
        Self {
            running: Arc::new(AtomicBool::new(false)),
            url: String::new(),
            qr_svg: String::new(),
            shared_root: PathBuf::new(),
            access_log: Arc::new(Mutex::new(Vec::new())),
        }
    }

    fn detect_local_ip() -> String {
        if let Ok(sock) = UdpSocket::bind("0.0.0.0:0") {
            if sock.connect("8.8.8.8:80").is_ok() {
                if let Ok(addr) = sock.local_addr() {
                    return addr.ip().to_string();
                }
            }
        }
        "127.0.0.1".to_string()
    }

    pub fn generate_qr_svg(text: &str) -> String {
        let Ok(qr) = QrCode::encode_text(text, QrCodeEcc::Medium) else {
            return String::new();
        };
        let size = qr.size();
        let border = 2;
        let dim = size + border * 2;
        let mut svg = format!(
            r##"<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {0} {0}" width="180" height="180" shape-rendering="crispEdges"><rect width="100%" height="100%" fill="#ffffff"/><path d=""##,
            dim
        );
        for y in 0..size {
            for x in 0..size {
                if qr.get_module(x, y) {
                    svg.push_str(&format!("M{},{}h1v1h-1z ", x + border, y + border));
                }
            }
        }
        svg.push_str(r##"" fill="#0f172a"/></svg>"##);
        svg
    }

    pub fn start(&mut self, dir_path: &Path, port: u16) -> Result<(String, String), String> {
        self.stop();
        let bind_addr = format!("0.0.0.0:{}", port);
        let listener = TcpListener::bind(&bind_addr)
            .or_else(|_| TcpListener::bind("0.0.0.0:0"))
            .map_err(|e| format!("HTTP-Server Bind-Fehler: {}", e))?;
        let actual_port = listener.local_addr().map(|a| a.port()).unwrap_or(port);
        listener.set_nonblocking(true).ok();

        let ip = Self::detect_local_ip();
        let url = format!("http://{}:{}/", ip, actual_port);
        let qr_svg = Self::generate_qr_svg(&url);

        self.running = Arc::new(AtomicBool::new(true));
        self.url = url.clone();
        self.qr_svg = qr_svg.clone();
        self.shared_root = dir_path.to_path_buf();
        let running_clone = self.running.clone();
        let root_clone = self.shared_root.clone();
        let log_clone = self.access_log.clone();

        thread::spawn(move || {
            while running_clone.load(Ordering::Relaxed) {
                if let Ok((mut stream, peer)) = listener.accept() {
                    let _ = stream.set_read_timeout(Some(Duration::from_secs(2)));
                    let mut req_buf = [0u8; 2048];
                    if let Ok(n) = stream.read(&mut req_buf) {
                        let req_str = String::from_utf8_lossy(&req_buf[..n]);
                        let first_line = req_str.lines().next().unwrap_or("");
                        let req_path = first_line
                            .split_whitespace()
                            .nth(1)
                            .unwrap_or("/");
                        if let Ok(mut lg) = log_clone.lock() {
                            lg.insert(0, format!("{} -> {}", peer.ip(), req_path));
                            lg.truncate(50);
                        }
                        Self::handle_http_client(&mut stream, &root_clone, req_path);
                    }
                } else {
                    thread::sleep(Duration::from_millis(60));
                }
            }
        });

        Ok((url, qr_svg))
    }

    pub fn stop(&mut self) {
        self.running.store(false, Ordering::Relaxed);
        self.url.clear();
        self.qr_svg.clear();
    }

    fn handle_http_client(stream: &mut std::net::TcpStream, root: &Path, req_path: &str) {
        let clean = req_path.trim_start_matches('/').replace("..", "");
        let target = if clean.is_empty() {
            root.to_path_buf()
        } else {
            root.join(&clean)
        };

        if target.is_file() {
            if let Ok(bytes) = fs::read(&target) {
                let fname = target
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| "file.bin".to_string());
                let hdr = format!(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {}\r\nContent-Disposition: attachment; filename=\"{}\"\r\n\r\n",
                    bytes.len(),
                    fname
                );
                let _ = stream.write_all(hdr.as_bytes());
                let _ = stream.write_all(&bytes);
                return;
            }
        }

        let mut html = String::from(
            "<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>ModernExplorer Share</title><style>body{font-family:system-ui,sans-serif;background:#0f172a;color:#e2e8f0;padding:18px;}a{color:#38bdf8;text-decoration:none;display:block;padding:10px;border-bottom:1px solid #1e293b;}</style></head><body><h2>📡 ModernExplorer WLAN Share</h2>",
        );
        if let Ok(rd) = fs::read_dir(root) {
            for item in rd.flatten() {
                let p = item.path();
                if p.is_file() {
                    let name = p
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_default();
                    let sz = format_bytes(p.metadata().map(|m| m.len()).unwrap_or(0));
                    html.push_str(&format!(
                        "<a href='/{0}'>📄 {0} <span style='color:#94a3b8;font-size:12px;'>({1})</span></a>",
                        name, sz
                    ));
                }
            }
        }
        html.push_str("</body></html>");
        let hdr = format!(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {}\r\n\r\n",
            html.len()
        );
        let _ = stream.write_all(hdr.as_bytes());
        let _ = stream.write_all(html.as_bytes());
    }
}

// ============================================================================
// 15. ECHTZEIT-DATEISYSTEM-WATCHER (notify Crate)
// ============================================================================

pub struct LiveDirectoryWatcher {
    watcher: Option<RecommendedWatcher>,
    watched_left: String,
    watched_right: String,
    pub dirty_left: Arc<AtomicBool>,
    pub dirty_right: Arc<AtomicBool>,
    left_path_shared: Arc<Mutex<String>>,
    right_path_shared: Arc<Mutex<String>>,
}

impl Default for LiveDirectoryWatcher {
    fn default() -> Self {
        Self::new()
    }
}

impl LiveDirectoryWatcher {
    pub fn new() -> Self {
        let dirty_left = Arc::new(AtomicBool::new(false));
        let dirty_right = Arc::new(AtomicBool::new(false));
        let left_path_shared = Arc::new(Mutex::new(String::new()));
        let right_path_shared = Arc::new(Mutex::new(String::new()));

        let dl = dirty_left.clone();
        let dr = dirty_right.clone();
        let lp = left_path_shared.clone();
        let rp = right_path_shared.clone();

        let watcher = RecommendedWatcher::new(
            move |res: Result<Event, notify::Error>| {
                if let Ok(event) = res {
                    let l_cur = lp.lock().map(|g| g.clone()).unwrap_or_default();
                    let r_cur = rp.lock().map(|g| g.clone()).unwrap_or_default();
                    for p in &event.paths {
                        let parent_str = p
                            .parent()
                            .unwrap_or(p)
                            .to_string_lossy()
                            .to_lowercase();
                        if !l_cur.is_empty() && parent_str == l_cur.to_lowercase() {
                            dl.store(true, Ordering::Relaxed);
                        }
                        if !r_cur.is_empty() && parent_str == r_cur.to_lowercase() {
                            dr.store(true, Ordering::Relaxed);
                        }
                    }
                }
            },
            Config::default(),
        )
        .ok();

        Self {
            watcher,
            watched_left: String::new(),
            watched_right: String::new(),
            dirty_left,
            dirty_right,
            left_path_shared,
            right_path_shared,
        }
    }

    pub fn update_watch(&mut self, side: &str, new_path: &str) {
        let Some(ref mut w) = self.watcher else {
            return;
        };
        let prev = if side == "left" {
            &mut self.watched_left
        } else {
            &mut self.watched_right
        };
        if prev == new_path {
            return;
        }
        if !prev.is_empty() {
            let _ = w.unwatch(Path::new(prev));
        }
        *prev = new_path.to_string();
        if side == "left" {
            if let Ok(mut g) = self.left_path_shared.lock() {
                *g = new_path.to_string();
            }
        } else if let Ok(mut g) = self.right_path_shared.lock() {
            *g = new_path.to_string();
        }
        if Path::new(new_path).is_dir() {
            let _ = w.watch(Path::new(new_path), RecursiveMode::NonRecursive);
        }
    }

    pub fn poll_dirty(&self) -> (bool, bool) {
        let l = self.dirty_left.swap(false, Ordering::Relaxed);
        let r = self.dirty_right.swap(false, Ordering::Relaxed);
        (l, r)
    }
}

// ============================================================================
// 16. SYMLINK- / HARDLINK- / JUNCTION-MANAGER
// ============================================================================

pub struct LinkManagerService;

impl LinkManagerService {
    pub fn read_link_target(path: &Path) -> Option<String> {
        fs::read_link(path)
            .ok()
            .map(|t| t.to_string_lossy().to_string())
    }

    pub fn create_link(
        link_type: &str, // "symlink", "hardlink", "junction"
        target_existing: &Path,
        new_link_path: &Path,
    ) -> Result<String, String> {
        if !target_existing.exists() {
            return Err(format!(
                "Ziel existiert nicht: {}",
                target_existing.display()
            ));
        }
        match link_type.to_ascii_lowercase().as_str() {
            "hardlink" => {
                if target_existing.is_dir() {
                    return Err("Hardlinks können nur für Dateien erstellt werden (für Ordner bitte Junction oder Symlink wählen)".to_string());
                }
                fs::hard_link(target_existing, new_link_path)
                    .map_err(|e| format!("Hardlink Fehler: {}", e))?;
                Ok("Hardlink erfolgreich erstellt".to_string())
            }
            "junction" => {
                #[cfg(target_os = "windows")]
                {
                    use std::os::windows::process::CommandExt;
                    let status = Command::new("cmd")
                        .args([
                            "/C",
                            "mklink",
                            "/J",
                            &new_link_path.to_string_lossy(),
                            &target_existing.to_string_lossy(),
                        ])
                        .creation_flags(0x08000000)
                        .status()
                        .map_err(|e| e.to_string())?;
                    if status.success() {
                        return Ok("Directory Junction erfolgreich erstellt".to_string());
                    }
                    return Err("mklink /J fehlgeschlagen".to_string());
                }
                #[cfg(not(target_os = "windows"))]
                {
                    std::os::unix::fs::symlink(target_existing, new_link_path)
                        .map_err(|e| e.to_string())?;
                    Ok("Symlink erfolgreich erstellt".to_string())
                }
            }
            _ => {
                #[cfg(target_os = "windows")]
                {
                    let res = if target_existing.is_dir() {
                        std::os::windows::fs::symlink_dir(target_existing, new_link_path)
                    } else {
                        std::os::windows::fs::symlink_file(target_existing, new_link_path)
                    };
                    res.map_err(|e| format!("Symlink Fehler (evtl. Entwicklermodus/Admin nötig, alternativ Junction nutzen): {}", e))?;
                    Ok("Symbolischer Link erfolgreich erstellt".to_string())
                }
                #[cfg(not(target_os = "windows"))]
                {
                    std::os::unix::fs::symlink(target_existing, new_link_path)
                        .map_err(|e| e.to_string())?;
                    Ok("Symbolischer Link erfolgreich erstellt".to_string())
                }
            }
        }
    }
}
