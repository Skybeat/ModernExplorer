use crate::app_paths::AppPaths;
use crate::filesystem::{format_bytes, FileEntry};
use chrono::{DateTime, Duration, Local};
use crc32fast::Hasher as Crc32Hasher;
use filetime::FileTime;
use md5::Md5;
use rand::RngCore;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256, Sha512};
use std::collections::HashMap;
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::process::Command;

use lopdf::Document;

// ============================================================================
// 1. Visual Side-by-Side File Diff Viewer (FileDiffService)
// ============================================================================

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum DiffLineKind {
    Same,
    Added,
    Removed,
    Modified,
}

#[derive(Debug, Clone)]
pub struct DiffSideBySideRow {
    pub row_index: usize,
    pub left_line_number: Option<usize>,
    pub right_line_number: Option<usize>,
    pub left_text: String,
    pub right_text: String,
    pub kind: DiffLineKind,
    pub hunk_id: Option<usize>,
}

#[derive(Debug, Clone, Default)]
pub struct FileDiffResult {
    pub rows: Vec<DiffSideBySideRow>,
    pub hunk_count: usize,
    pub added_count: usize,
    pub removed_count: usize,
    pub modified_count: usize,
}

pub struct FileDiffService;

impl FileDiffService {
    pub fn compute_sha256_hex(bytes: &[u8]) -> String {
        format!("{:x}", Sha256::digest(bytes))
    }

    pub fn read_file_for_diff(path: &Path) -> Result<String, String> {
        if !path.exists() {
            return Err(format!("Datei existiert nicht: {}", path.display()));
        }
        let bytes = fs::read(path).map_err(|e| format!("Fehler beim Lesen von {}: {}", path.display(), e))?;
        Self::convert_bytes_for_diff(path, &bytes)
    }

    pub fn convert_bytes_for_diff(path: &Path, bytes: &[u8]) -> Result<String, String> {
        let is_pdf = path
            .extension()
            .and_then(|e| e.to_str())
            .map(|e| e.eq_ignore_ascii_case("pdf"))
            .unwrap_or(false)
            || bytes.starts_with(b"%PDF-");

        if is_pdf {
            return Ok(Self::extract_pdf_diff_content(path, bytes));
        }

        Ok(Self::decode_bytes_to_diffable_text(bytes))
    }

    pub fn extract_pdf_diff_content(path: &Path, bytes: &[u8]) -> String {
        let file_name = path.file_name().and_then(|n| n.to_str()).unwrap_or("PDF");
        match Document::load_mem(bytes) {
            Ok(doc) => {
                let pages = doc.get_pages();
                let total_pages = pages.len();
                let mut full_text = String::new();
                let mut extracted_any = false;

                for (page_num, _) in &pages {
                    let page_text = doc.extract_text(&[*page_num]).unwrap_or_default();
                    let trimmed = page_text.trim();
                    if !trimmed.is_empty() {
                        extracted_any = true;
                    }
                    full_text.push_str(&format!("=== [Seite {} / {}] ===\n", page_num, total_pages));
                    if trimmed.is_empty() {
                        full_text.push_str("(Kein extrahierbarer Text auf dieser Seite)\n");
                    } else {
                        for line in page_text.lines() {
                            full_text.push_str(line.trim_end());
                            full_text.push('\n');
                        }
                    }
                    full_text.push('\n');
                }

                if extracted_any {
                    let mut header = format!("[PDF Dokument: {} — {} Seiten, Version {}]\n\n", file_name, total_pages, doc.version);
                    header.push_str(&full_text);
                    return header;
                }

                Self::extract_pdf_structure_and_metadata(&doc, bytes, file_name)
            }
            Err(e) => {
                let mut out = format!("[PDF Ladefehler: {} — Zeige Binär-Hex-Dump]\n\n", e);
                out.push_str(&Self::format_binary_hex_dump(bytes));
                out
            }
        }
    }

    pub fn extract_pdf_structure_and_metadata(doc: &Document, bytes: &[u8], file_name: &str) -> String {
        let mut out = String::new();
        out.push_str(&format!("[PDF Dokument-Struktur & Metadaten (Keine eingebettete Text-Ebene gefunden: {})]\n", file_name));
        out.push_str(&format!("PDF-Version: {}\n", doc.version));
        out.push_str(&format!("Dateigröße: {} Bytes\n", bytes.len()));

        let mut hasher = Sha256::new();
        hasher.update(bytes);
        let sha256_hex = format!("{:x}", hasher.finalize());
        out.push_str(&format!("SHA-256 Prüfsumme: {}\n", sha256_hex));

        let pages = doc.get_pages();
        out.push_str(&format!("Seitenanzahl: {}\n\n", pages.len()));

        out.push_str("=== Seiten-Details ===\n");
        for (page_num, object_id) in &pages {
            out.push_str(&format!("Seite {}: Objekt {:?}", page_num, object_id));
            if let Ok(page_obj) = doc.get_object(*object_id) {
                if let Ok(dict) = page_obj.as_dict() {
                    if let Ok(media_box) = dict.get(b"MediaBox") {
                        out.push_str(&format!(" | MediaBox: {:?}", media_box));
                    }
                    if let Ok(rotate) = dict.get(b"Rotate") {
                        out.push_str(&format!(" | Drehung: {:?}", rotate));
                    }
                    if let Ok(resources) = dict.get(b"Resources") {
                        if let Ok(r_dict) = resources.as_dict() {
                            let keys: Vec<String> = r_dict.iter().map(|(k, _)| String::from_utf8_lossy(k).to_string()).collect();
                            out.push_str(&format!(" | Ressourcen: [{}]", keys.join(", ")));
                        }
                    }
                }
            }
            out.push('\n');
        }

        out.push_str("\n=== Dokument-Informationen (Trailer / Info) ===\n");
        if let Ok(info_ref) = doc.trailer.get(b"Info") {
            if let Ok(ref_id) = info_ref.as_reference() {
                if let Ok(info_dict) = doc.get_object(ref_id).and_then(|o| o.as_dict()) {
                    for (key, val) in info_dict.iter() {
                        let k = String::from_utf8_lossy(key);
                        out.push_str(&format!("{}: {:?}\n", k, val));
                    }
                }
            }
        }

        out.push_str("\n=== Enthaltene PDF-Objekte & Ströme ===\n");
        for (id, obj) in &doc.objects {
            match obj {
                lopdf::Object::Stream(s) => {
                    let type_name = s.dict.get(b"Type").ok().map(|o| format!("{:?}", o)).unwrap_or_else(|| "-".to_string());
                    let subtype = s.dict.get(b"Subtype").ok().map(|o| format!("{:?}", o)).unwrap_or_else(|| "-".to_string());
                    let filter = s.dict.get(b"Filter").ok().map(|o| format!("{:?}", o)).unwrap_or_else(|| "-".to_string());
                    out.push_str(&format!("Stream {:?}: Type={}, Subtype={}, Filter={}, Länge={} Bytes\n", id, type_name, subtype, filter, s.content.len()));
                }
                lopdf::Object::Dictionary(d) => {
                    let type_name = d.get(b"Type").ok().map(|o| format!("{:?}", o)).unwrap_or_else(|| "-".to_string());
                    let subtype = d.get(b"Subtype").ok().map(|o| format!("{:?}", o)).unwrap_or_else(|| "-".to_string());
                    if type_name != "-" || subtype != "-" {
                        out.push_str(&format!("Dict {:?}: Type={}, Subtype={}\n", id, type_name, subtype));
                    }
                }
                _ => {}
            }
        }

        out
    }

    pub fn decode_bytes_to_diffable_text(bytes: &[u8]) -> String {
        if bytes.is_empty() {
            return String::new();
        }

        // Check for UTF-8 BOM
        if bytes.starts_with(&[0xEF, 0xBB, 0xBF]) {
            if let Ok(s) = std::str::from_utf8(&bytes[3..]) {
                return s.to_string();
            }
        }

        // Check for UTF-16 LE BOM
        if bytes.starts_with(&[0xFF, 0xFE]) {
            let u16s: Vec<u16> = bytes[2..]
                .chunks_exact(2)
                .map(|c| u16::from_le_bytes([c[0], c[1]]))
                .collect();
            if let Ok(s) = String::from_utf16(&u16s) {
                return s;
            }
        }

        // Check for UTF-16 BE BOM
        if bytes.starts_with(&[0xFE, 0xFF]) {
            let u16s: Vec<u16> = bytes[2..]
                .chunks_exact(2)
                .map(|c| u16::from_be_bytes([c[0], c[1]]))
                .collect();
            if let Ok(s) = String::from_utf16(&u16s) {
                return s;
            }
        }

        // Try standard UTF-8
        if let Ok(s) = std::str::from_utf8(bytes) {
            return s.to_string();
        }

        // Check if mostly text or binary
        let sample = &bytes[..bytes.len().min(4096)];
        let null_count = sample.iter().filter(|&&b| b == 0).count();
        if null_count > 0 {
            // Binary file: format as hex dump lines
            return Self::format_binary_hex_dump(bytes);
        }

        // Fallback: UTF-8 lossy (handles Windows-1252 / Latin-1 nicely)
        String::from_utf8_lossy(bytes).into_owned()
    }

    pub fn format_binary_hex_dump(bytes: &[u8]) -> String {
        let mut out = String::new();
        let max_len = bytes.len().min(512 * 1024);
        for (i, chunk) in bytes[..max_len].chunks(16).enumerate() {
            let offset = i * 16;
            let mut hex_part = String::new();
            let mut ascii_part = String::new();
            for (j, &b) in chunk.iter().enumerate() {
                if j == 8 {
                    hex_part.push(' ');
                }
                hex_part.push_str(&format!("{:02x} ", b));
                if (32..=126).contains(&b) {
                    ascii_part.push(b as char);
                } else {
                    ascii_part.push('.');
                }
            }
            out.push_str(&format!("{:08x}: {:<49} |{}|\n", offset, hex_part.trim_end(), ascii_part));
        }
        if bytes.len() > max_len {
            out.push_str(&format!("... [Gekürzt: {} Bytes weitere Binärdaten] ...\n", bytes.len() - max_len));
        }
        out
    }

    pub fn compare_files(
        left_path: &Path,
        right_path: &Path,
        ignore_whitespace: bool,
    ) -> Result<FileDiffResult, String> {
        let left_bytes = fs::read(left_path).map_err(|e| format!("Fehler beim Lesen der linken Datei ({}): {}", left_path.display(), e))?;
        let right_bytes = fs::read(right_path).map_err(|e| format!("Fehler beim Lesen der rechten Datei ({}): {}", right_path.display(), e))?;

        let mut left_text = Self::convert_bytes_for_diff(left_path, &left_bytes)?;
        let mut right_text = Self::convert_bytes_for_diff(right_path, &right_bytes)?;

        if left_text == right_text && left_bytes != right_bytes {
            let left_hash = format!("{:x}", Sha256::digest(&left_bytes));
            let right_hash = format!("{:x}", Sha256::digest(&right_bytes));
            left_text.push_str(&format!("\n\n=== [Binäre Datei-Eigenschaften (Extrahierter Text ist identisch)] ===\nDateigröße: {} Bytes\nSHA-256: {}\n", left_bytes.len(), left_hash));
            right_text.push_str(&format!("\n\n=== [Binäre Datei-Eigenschaften (Extrahierter Text ist identisch)] ===\nDateigröße: {} Bytes\nSHA-256: {}\n", right_bytes.len(), right_hash));
        }

        Ok(Self::compare_text(&left_text, &right_text, ignore_whitespace))
    }

    pub fn compare_text(
        left_text: &str,
        right_text: &str,
        ignore_whitespace: bool,
    ) -> FileDiffResult {
        let left_lines: Vec<&str> = left_text.lines().collect();
        let right_lines: Vec<&str> = right_text.lines().collect();
        let n = left_lines.len();
        let m = right_lines.len();

        let eq = |a: &str, b: &str| -> bool {
            if ignore_whitespace {
                a.split_whitespace().collect::<Vec<_>>() == b.split_whitespace().collect::<Vec<_>>()
            } else {
                a == b
            }
        };

        // LCS DP table
        let mut dp = vec![vec![0usize; m + 1]; n + 1];
        for i in (0..n).rev() {
            for j in (0..m).rev() {
                if eq(left_lines[i], right_lines[j]) {
                    dp[i][j] = 1 + dp[i + 1][j + 1];
                } else {
                    dp[i][j] = dp[i + 1][j].max(dp[i][j + 1]);
                }
            }
        }

        let mut i = 0;
        let mut j = 0;
        let mut raw_rows: Vec<(Option<usize>, Option<usize>, String, String, DiffLineKind)> =
            Vec::new();

        while i < n || j < m {
            if i < n && j < m && eq(left_lines[i], right_lines[j]) {
                raw_rows.push((
                    Some(i + 1),
                    Some(j + 1),
                    left_lines[i].to_string(),
                    right_lines[j].to_string(),
                    DiffLineKind::Same,
                ));
                i += 1;
                j += 1;
            } else if i < n && (j == m || dp[i + 1][j] >= dp[i][j + 1]) {
                raw_rows.push((
                    Some(i + 1),
                    None,
                    left_lines[i].to_string(),
                    String::new(),
                    DiffLineKind::Removed,
                ));
                i += 1;
            } else {
                raw_rows.push((
                    None,
                    Some(j + 1),
                    String::new(),
                    right_lines[j].to_string(),
                    DiffLineKind::Added,
                ));
                j += 1;
            }
        }

        // Collapse adjacent Removed + Added pairs into Modified rows
        let mut merged_rows = Vec::new();
        let mut k = 0;
        while k < raw_rows.len() {
            if k + 1 < raw_rows.len()
                && raw_rows[k].4 == DiffLineKind::Removed
                && raw_rows[k + 1].4 == DiffLineKind::Added
            {
                merged_rows.push((
                    raw_rows[k].0,
                    raw_rows[k + 1].1,
                    raw_rows[k].2.clone(),
                    raw_rows[k + 1].3.clone(),
                    DiffLineKind::Modified,
                ));
                k += 2;
            } else {
                merged_rows.push(raw_rows[k].clone());
                k += 1;
            }
        }

        let mut rows = Vec::with_capacity(merged_rows.len());
        let mut current_hunk = 0usize;
        let mut in_hunk = false;
        let mut added_count = 0;
        let mut removed_count = 0;
        let mut modified_count = 0;

        for (idx, (ll, rl, lt, rt, kind)) in merged_rows.into_iter().enumerate() {
            let hunk_id = if kind == DiffLineKind::Same {
                in_hunk = false;
                None
            } else {
                if !in_hunk {
                    current_hunk += 1;
                    in_hunk = true;
                }
                match kind {
                    DiffLineKind::Added => added_count += 1,
                    DiffLineKind::Removed => removed_count += 1,
                    DiffLineKind::Modified => modified_count += 1,
                    DiffLineKind::Same => {}
                }
                Some(current_hunk)
            };

            rows.push(DiffSideBySideRow {
                row_index: idx,
                left_line_number: ll,
                right_line_number: rl,
                left_text: lt,
                right_text: rt,
                kind,
                hunk_id,
            });
        }

        FileDiffResult {
            rows,
            hunk_count: current_hunk,
            added_count,
            removed_count,
            modified_count,
        }
    }

    pub fn merge_hunk_left_to_right(diff: &mut FileDiffResult, hunk_id: usize) {
        for row in &mut diff.rows {
            if row.hunk_id == Some(hunk_id) {
                match row.kind {
                    DiffLineKind::Removed | DiffLineKind::Modified => {
                        row.right_text = row.left_text.clone();
                        row.right_line_number = row.left_line_number;
                        row.kind = DiffLineKind::Same;
                        row.hunk_id = None;
                    }
                    DiffLineKind::Added => {
                        row.right_text.clear();
                        row.right_line_number = None;
                        row.kind = DiffLineKind::Same;
                        row.hunk_id = None;
                    }
                    DiffLineKind::Same => {}
                }
            }
        }
    }

    pub fn merge_hunk_right_to_left(diff: &mut FileDiffResult, hunk_id: usize) {
        for row in &mut diff.rows {
            if row.hunk_id == Some(hunk_id) {
                match row.kind {
                    DiffLineKind::Added | DiffLineKind::Modified => {
                        row.left_text = row.right_text.clone();
                        row.left_line_number = row.right_line_number;
                        row.kind = DiffLineKind::Same;
                        row.hunk_id = None;
                    }
                    DiffLineKind::Removed => {
                        row.left_text.clear();
                        row.left_line_number = None;
                        row.kind = DiffLineKind::Same;
                        row.hunk_id = None;
                    }
                    DiffLineKind::Same => {}
                }
            }
        }
    }

    pub fn reconstruct_left_text(diff: &FileDiffResult) -> String {
        diff.rows
            .iter()
            .filter(|r| r.left_line_number.is_some())
            .map(|r| r.left_text.as_str())
            .collect::<Vec<_>>()
            .join("\n")
    }

    pub fn reconstruct_right_text(diff: &FileDiffResult) -> String {
        diff.rows
            .iter()
            .filter(|r| r.right_line_number.is_some())
            .map(|r| r.right_text.as_str())
            .collect::<Vec<_>>()
            .join("\n")
    }
}

// ============================================================================
// 2. Checksum Generator & Manifest Verifier (ChecksumBatchService)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileChecksumEntry {
    pub file_name: String,
    pub full_path: String,
    pub size_bytes: u64,
    pub sha256: String,
    pub sha512: String,
    pub md5: String,
    pub crc32: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ChecksumManifestFormat {
    Sha256,
    Sha512,
    Md5,
    Sfv,
}

#[derive(Debug, Clone)]
pub struct ManifestVerificationItem {
    pub file_name: String,
    pub expected_hash: String,
    pub actual_hash: String,
    pub is_match: bool,
    pub file_exists: bool,
}

pub struct ChecksumBatchService;

impl ChecksumBatchService {
    pub fn compute_checksums(paths: &[String]) -> Vec<FileChecksumEntry> {
        let mut results = Vec::new();
        for p in paths {
            let path = Path::new(p);
            if !path.is_file() {
                continue;
            }
            if let Ok(mut f) = File::open(path) {
                let mut sha256 = Sha256::new();
                let mut sha512 = Sha512::new();
                let mut md5 = Md5::new();
                let mut crc32 = Crc32Hasher::new();
                let mut buf = [0u8; 65536];
                let mut total = 0u64;
                while let Ok(n) = f.read(&mut buf) {
                    if n == 0 {
                        break;
                    }
                    total += n as u64;
                    sha256.update(&buf[..n]);
                    sha512.update(&buf[..n]);
                    md5.update(&buf[..n]);
                    crc32.update(&buf[..n]);
                }
                let name = path
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| p.clone());
                results.push(FileChecksumEntry {
                    file_name: name,
                    full_path: p.clone(),
                    size_bytes: total,
                    sha256: format!("{:x}", sha256.finalize()),
                    sha512: format!("{:x}", sha512.finalize()),
                    md5: format!("{:x}", md5.finalize()),
                    crc32: format!("{:08x}", crc32.finalize()),
                });
            }
        }
        results
    }

    pub fn format_manifest(
        entries: &[FileChecksumEntry],
        format: ChecksumManifestFormat,
    ) -> String {
        let mut out = String::new();
        for e in entries {
            match format {
                ChecksumManifestFormat::Sha256 => {
                    out.push_str(&format!("{} *{}\n", e.sha256, e.file_name));
                }
                ChecksumManifestFormat::Sha512 => {
                    out.push_str(&format!("{} *{}\n", e.sha512, e.file_name));
                }
                ChecksumManifestFormat::Md5 => {
                    out.push_str(&format!("{} *{}\n", e.md5, e.file_name));
                }
                ChecksumManifestFormat::Sfv => {
                    out.push_str(&format!("{} {}\n", e.file_name, e.crc32.to_uppercase()));
                }
            }
        }
        out
    }

    pub fn verify_manifest(manifest_path: &Path) -> Result<Vec<ManifestVerificationItem>, String> {
        let content = fs::read_to_string(manifest_path).map_err(|e| e.to_string())?;
        let base_dir = manifest_path.parent().unwrap_or_else(|| Path::new("."));
        let ext = manifest_path
            .extension()
            .map(|e| e.to_string_lossy().to_lowercase())
            .unwrap_or_default();

        let mut items = Vec::new();
        for raw_line in content.lines() {
            let line = raw_line.trim();
            if line.is_empty() || line.starts_with(';') || line.starts_with('#') {
                continue;
            }
            if ext == "sfv" {
                if let Some((fname, hash)) = line.rsplit_once(' ') {
                    let fname = fname.trim();
                    let expected = hash.trim().to_lowercase();
                    let target = base_dir.join(fname);
                    let exists = target.is_file();
                    let actual = if exists {
                        Self::compute_checksums(&[target.to_string_lossy().to_string()])
                            .first()
                            .map(|c| c.crc32.to_lowercase())
                            .unwrap_or_default()
                    } else {
                        String::new()
                    };
                    items.push(ManifestVerificationItem {
                        file_name: fname.to_string(),
                        is_match: exists && actual == expected,
                        expected_hash: expected,
                        actual_hash: actual,
                        file_exists: exists,
                    });
                }
            } else if let Some((hash, rest)) = line.split_once(' ') {
                let expected = hash.trim().to_lowercase();
                let fname = rest.trim().trim_start_matches('*');
                let target = base_dir.join(fname);
                let exists = target.is_file();
                let actual = if exists {
                    if let Some(c) =
                        Self::compute_checksums(&[target.to_string_lossy().to_string()]).first()
                    {
                        match expected.len() {
                            32 => c.md5.to_lowercase(),
                            128 => c.sha512.to_lowercase(),
                            _ => c.sha256.to_lowercase(),
                        }
                    } else {
                        String::new()
                    }
                } else {
                    String::new()
                };
                items.push(ManifestVerificationItem {
                    file_name: fname.to_string(),
                    is_match: exists && actual == expected,
                    expected_hash: expected,
                    actual_hash: actual,
                    file_exists: exists,
                });
            }
        }
        Ok(items)
    }
}

// ============================================================================
// 3. File Splitter, Joiner (.001) & Secure Shredder (FileSplitAndShredService)
// ============================================================================

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ShredMode {
    OnePassRandom,
    ThreePassDoD,
    SevenPass,
}

pub struct FileSplitAndShredService;

impl FileSplitAndShredService {
    pub fn split_file(
        source_file: &Path,
        output_dir: &Path,
        part_size_bytes: u64,
    ) -> Result<Vec<PathBuf>, String> {
        if part_size_bytes < 64 {
            return Err("Part size must be at least 64 bytes.".to_string());
        }
        fs::create_dir_all(output_dir).map_err(|e| e.to_string())?;
        let base_name = source_file
            .file_name()
            .ok_or_else(|| "Invalid source file".to_string())?
            .to_string_lossy()
            .to_string();

        let mut src = File::open(source_file).map_err(|e| e.to_string())?;
        let mut part_index = 1usize;
        let mut created_parts = Vec::new();
        let mut buf = vec![0u8; 65536];

        loop {
            let mut remaining = part_size_bytes;
            let part_name = format!("{}.{:03}", base_name, part_index);
            let part_path = output_dir.join(&part_name);
            let mut wrote_any = false;
            let mut out_file: Option<File> = None;

            while remaining > 0 {
                let to_read = (remaining as usize).min(buf.len());
                let n = src.read(&mut buf[..to_read]).map_err(|e| e.to_string())?;
                if n == 0 {
                    break;
                }
                if out_file.is_none() {
                    out_file = Some(File::create(&part_path).map_err(|e| e.to_string())?);
                }
                out_file
                    .as_mut()
                    .unwrap()
                    .write_all(&buf[..n])
                    .map_err(|e| e.to_string())?;
                wrote_any = true;
                remaining -= n as u64;
            }

            if !wrote_any {
                break;
            }
            created_parts.push(part_path);
            part_index += 1;
        }

        // Write companion .sha256 manifest
        let part_strings: Vec<String> = created_parts
            .iter()
            .map(|p| p.to_string_lossy().to_string())
            .collect();
        let checksums = ChecksumBatchService::compute_checksums(&part_strings);
        let manifest_text =
            ChecksumBatchService::format_manifest(&checksums, ChecksumManifestFormat::Sha256);
        let manifest_path = output_dir.join(format!("{}.sha256", base_name));
        let _ = fs::write(manifest_path, manifest_text);

        Ok(created_parts)
    }

    pub fn join_parts(first_part_path: &Path, destination_file: &Path) -> Result<u64, String> {
        let dir = first_part_path.parent().unwrap_or_else(|| Path::new("."));
        let first_name = first_part_path
            .file_name()
            .ok_or_else(|| "Invalid part path".to_string())?
            .to_string_lossy()
            .to_string();
        let base_name = first_name
            .strip_suffix(".001")
            .unwrap_or(&first_name)
            .to_string();

        let mut out = File::create(destination_file).map_err(|e| e.to_string())?;
        let mut total_written = 0u64;
        let mut buf = [0u8; 65536];

        for idx in 1..=999 {
            let candidate = dir.join(format!("{}.{:03}", base_name, idx));
            if !candidate.is_file() {
                if idx == 1 {
                    return Err("First part (.001) not found.".to_string());
                }
                break;
            }
            let mut part_file = File::open(&candidate).map_err(|e| e.to_string())?;
            loop {
                let n = part_file.read(&mut buf).map_err(|e| e.to_string())?;
                if n == 0 {
                    break;
                }
                out.write_all(&buf[..n]).map_err(|e| e.to_string())?;
                total_written += n as u64;
            }
        }
        Ok(total_written)
    }

    pub fn shred_file(file_path: &Path, mode: ShredMode) -> Result<(), String> {
        if !file_path.is_file() {
            return Err("Target file does not exist.".to_string());
        }
        let len = fs::metadata(file_path).map_err(|e| e.to_string())?.len();
        let passes: &[Option<u8>] = match mode {
            ShredMode::OnePassRandom => &[None],
            ShredMode::ThreePassDoD => &[Some(0x00), Some(0xFF), None],
            ShredMode::SevenPass => &[
                Some(0x00),
                Some(0xFF),
                None,
                Some(0xAA),
                Some(0x55),
                Some(0x00),
                None,
            ],
        };

        {
            let mut f = OpenOptions::new()
                .write(true)
                .open(file_path)
                .map_err(|e| e.to_string())?;
            let mut buf = vec![0u8; 65536];
            let mut rng = rand::thread_rng();

            for pass in passes {
                f.seek(SeekFrom::Start(0)).map_err(|e| e.to_string())?;
                let mut remaining = len;
                while remaining > 0 {
                    let chunk = (remaining as usize).min(buf.len());
                    match pass {
                        Some(byte) => buf[..chunk].fill(*byte),
                        None => rng.fill_bytes(&mut buf[..chunk]),
                    }
                    f.write_all(&buf[..chunk]).map_err(|e| e.to_string())?;
                    remaining -= chunk as u64;
                }
                f.sync_all().map_err(|e| e.to_string())?;
            }
            f.set_len(0).map_err(|e| e.to_string())?;
        }

        // Rename to anonymous name before deletion
        let parent = file_path.parent().unwrap_or_else(|| Path::new("."));
        let scrubbed_path = parent.join("00000000.tmp");
        if fs::rename(file_path, &scrubbed_path).is_ok() {
            fs::remove_file(&scrubbed_path).map_err(|e| e.to_string())
        } else {
            fs::remove_file(file_path).map_err(|e| e.to_string())
        }
    }
}

// ============================================================================
// 4. Color File Tags / Labels & Notes (FileTagService)
// ============================================================================

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum FileTagColor {
    None,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
}

impl FileTagColor {
    pub fn hex_color(self) -> &'static str {
        match self {
            Self::None => "#64748B",
            Self::Red => "#EF4444",
            Self::Orange => "#F97316",
            Self::Yellow => "#EAB308",
            Self::Green => "#10B981",
            Self::Blue => "#3B82F6",
            Self::Purple => "#A855F7",
        }
    }

    pub fn display_label(self) -> &'static str {
        match self {
            Self::None => "Kein Tag",
            Self::Red => "🔴 Rot (Red)",
            Self::Orange => "🟠 Orange",
            Self::Yellow => "🟡 Gelb (Yellow)",
            Self::Green => "🟢 Grün (Green)",
            Self::Blue => "🔵 Blau (Blue)",
            Self::Purple => "🟣 Violett (Purple)",
        }
    }

    pub fn matches_query_token(self, token: &str) -> bool {
        let t = token.trim().to_ascii_lowercase();
        if t == "*" || t == "any" {
            return self != Self::None;
        }
        match self {
            Self::Red => t == "red" || t == "rot",
            Self::Orange => t == "orange",
            Self::Yellow => t == "yellow" || t == "gelb",
            Self::Green => t == "green" || t == "gruen" || t == "grün",
            Self::Blue => t == "blue" || t == "blau",
            Self::Purple => t == "purple" || t == "violett" || t == "lila",
            Self::None => t == "none" || t == "kein",
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileTagEntry {
    pub path: String,
    pub color: FileTagColor,
    pub note: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct FileTagService {
    entries: HashMap<String, FileTagEntry>,
}

impl FileTagService {
    pub fn load() -> Self {
        let path = AppPaths::file_tags_file();
        if let Ok(text) = fs::read_to_string(&path) {
            if let Ok(svc) = serde_json::from_str::<FileTagService>(&text) {
                return svc;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let path = AppPaths::file_tags_file();
        if let Ok(json) = serde_json::to_string_pretty(self) {
            let _ = fs::write(path, json);
        }
    }

    fn norm_key(path: &str) -> String {
        path.replace('/', "\\").to_lowercase()
    }

    pub fn set_tag_and_note(&mut self, path: &str, color: FileTagColor, note: &str) {
        let key = Self::norm_key(path);
        if color == FileTagColor::None && note.trim().is_empty() {
            self.entries.remove(&key);
        } else {
            self.entries.insert(
                key,
                FileTagEntry {
                    path: path.to_string(),
                    color,
                    note: note.trim().to_string(),
                },
            );
        }
        self.save();
    }

    pub fn get(&self, path: &str) -> Option<&FileTagEntry> {
        self.entries.get(&Self::norm_key(path))
    }

    pub fn matches_tag_query(&self, path: &str, query: &str) -> bool {
        if let Some(entry) = self.get(path) {
            entry.color.matches_query_token(query)
        } else {
            false
        }
    }

    pub fn matches_note_query(&self, path: &str, query: &str) -> bool {
        if let Some(entry) = self.get(path) {
            if query.trim() == "*" {
                !entry.note.is_empty()
            } else {
                entry.note.to_lowercase().contains(&query.to_lowercase())
            }
        } else {
            false
        }
    }

    pub fn all_tagged(&self) -> Vec<&FileTagEntry> {
        self.entries.values().collect()
    }
}

// ============================================================================
// 5. Workspace Profiles (WorkspaceProfileService)
// ============================================================================

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WorkspaceTabSnapshot {
    pub path: String,
    pub is_pinned: bool,
    pub is_locked: bool,
    pub view_mode: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WorkspaceProfile {
    pub name: String,
    pub left_tabs: Vec<WorkspaceTabSnapshot>,
    pub right_tabs: Vec<WorkspaceTabSnapshot>,
    pub is_dual_pane: bool,
    pub split_ratio: f32,
    pub created_at: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct WorkspaceProfileService {
    pub profiles: Vec<WorkspaceProfile>,
}

impl WorkspaceProfileService {
    pub fn load() -> Self {
        let path = AppPaths::workspaces_file();
        if let Ok(text) = fs::read_to_string(&path) {
            if let Ok(svc) = serde_json::from_str::<WorkspaceProfileService>(&text) {
                return svc;
            }
        }
        Self::default()
    }

    pub fn save(&self) {
        let path = AppPaths::workspaces_file();
        if let Ok(json) = serde_json::to_string_pretty(self) {
            let _ = fs::write(path, json);
        }
    }

    pub fn upsert_profile(&mut self, profile: WorkspaceProfile) {
        self.profiles
            .retain(|p| !p.name.eq_ignore_ascii_case(&profile.name));
        self.profiles.push(profile);
        self.save();
    }

    pub fn delete_profile(&mut self, name: &str) {
        self.profiles
            .retain(|p| !p.name.eq_ignore_ascii_case(name));
        self.save();
    }
}

// ============================================================================
// 6. Smart Copy / Path & List Exporter (SmartExportService)
// ============================================================================

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SmartExportFormat {
    FullPaths,
    FileNamesOnly,
    RelativePaths,
    ForwardSlashPaths,
    MarkdownFileLinks,
    CsvTable,
    MarkdownTable,
    JsonArray,
    HtmlTable,
}

impl SmartExportFormat {
    pub fn all() -> &'static [(SmartExportFormat, &'static str)] {
        &[
            (Self::FullPaths, "Vollständige Pfade (Full Paths)"),
            (Self::FileNamesOnly, "Nur Dateinamen (File Names Only)"),
            (Self::RelativePaths, "Relative Pfade (Relative Paths)"),
            (Self::ForwardSlashPaths, "Forward-Slash-Pfade (C:/path/file)"),
            (Self::MarkdownFileLinks, "Markdown file://-Links"),
            (Self::CsvTable, "CSV-Tabelle (.csv)"),
            (Self::MarkdownTable, "Markdown-Tabelle (.md)"),
            (Self::JsonArray, "JSON-Array (.json)"),
            (Self::HtmlTable, "HTML-Tabelle (.html)"),
        ]
    }
}

pub struct SmartExportService;

impl SmartExportService {
    pub fn format_entries(
        entries: &[FileEntry],
        format: SmartExportFormat,
        base_dir: Option<&Path>,
    ) -> String {
        match format {
            SmartExportFormat::FullPaths => entries
                .iter()
                .map(|e| e.full_path.as_str())
                .collect::<Vec<_>>()
                .join("\n"),
            SmartExportFormat::FileNamesOnly => entries
                .iter()
                .map(|e| e.name.as_str())
                .collect::<Vec<_>>()
                .join("\n"),
            SmartExportFormat::RelativePaths => entries
                .iter()
                .map(|e| {
                    if let Some(base) = base_dir {
                        Path::new(&e.full_path)
                            .strip_prefix(base)
                            .map(|p| p.to_string_lossy().to_string())
                            .unwrap_or_else(|_| e.name.clone())
                    } else {
                        e.name.clone()
                    }
                })
                .collect::<Vec<_>>()
                .join("\n"),
            SmartExportFormat::ForwardSlashPaths => entries
                .iter()
                .map(|e| e.full_path.replace('\\', "/"))
                .collect::<Vec<_>>()
                .join("\n"),
            SmartExportFormat::MarkdownFileLinks => entries
                .iter()
                .map(|e| {
                    let slash = e.full_path.replace('\\', "/");
                    format!("[{}](file:///{})", e.name, slash.trim_start_matches('/'))
                })
                .collect::<Vec<_>>()
                .join("\n"),
            SmartExportFormat::CsvTable => {
                let mut out = String::from("Name,Extension,SizeBytes,FormattedSize,Modified,FullPath\n");
                for e in entries {
                    out.push_str(&format!(
                        "\"{}\",\"{}\",{},\"{}\",\"{}\",\"{}\"\n",
                        e.name.replace('"', "\"\""),
                        e.extension,
                        e.size_bytes,
                        e.display_size(),
                        e.modified.format("%Y-%m-%d %H:%M:%S"),
                        e.full_path.replace('"', "\"\"")
                    ));
                }
                out
            }
            SmartExportFormat::MarkdownTable => {
                let mut out = String::from(
                    "| Name | Type | Size | Modified | Path |\n| :--- | :--- | ---: | :--- | :--- |\n",
                );
                for e in entries {
                    out.push_str(&format!(
                        "| {} | {} | {} | {} | `{}` |\n",
                        e.name,
                        if e.is_directory { "<DIR>" } else { &e.extension },
                        e.display_size(),
                        e.modified.format("%Y-%m-%d %H:%M"),
                        e.full_path
                    ));
                }
                out
            }
            SmartExportFormat::JsonArray => {
                serde_json::to_string_pretty(entries).unwrap_or_else(|_| "[]".to_string())
            }
            SmartExportFormat::HtmlTable => {
                let mut out = String::from(
                    "<table>\n  <thead><tr><th>Name</th><th>Size</th><th>Modified</th><th>Path</th></tr></thead>\n  <tbody>\n",
                );
                for e in entries {
                    out.push_str(&format!(
                        "    <tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td></tr>\n",
                        e.name,
                        e.display_size(),
                        e.modified.format("%Y-%m-%d %H:%M"),
                        e.full_path
                    ));
                }
                out.push_str("  </tbody>\n</table>");
                out
            }
        }
    }
}

// ============================================================================
// 7. Virtual File Basket / Drop-Basket (FileBasketService)
// ============================================================================

#[derive(Debug, Clone, Default)]
pub struct FileBasketService {
    items: Vec<FileEntry>,
}

impl FileBasketService {
    pub fn add_paths(&mut self, paths: &[String]) -> usize {
        let mut added = 0;
        for p in paths {
            if self
                .items
                .iter()
                .any(|existing| existing.full_path.eq_ignore_ascii_case(p))
            {
                continue;
            }
            if let Some(entry) = FileEntry::try_from_path(p) {
                self.items.push(entry);
                added += 1;
            }
        }
        added
    }

    pub fn remove_by_path(&mut self, path: &str) -> bool {
        let before = self.items.len();
        self.items
            .retain(|i| !i.full_path.eq_ignore_ascii_case(path));
        self.items.len() < before
    }

    pub fn clear(&mut self) {
        self.items.clear();
    }

    pub fn items(&self) -> &[FileEntry] {
        &self.items
    }

    pub fn count(&self) -> usize {
        self.items.len()
    }

    pub fn total_file_bytes(&self) -> u64 {
        self.items.iter().map(|i| i.size_bytes).sum()
    }

    pub fn summary_label(&self) -> String {
        format!(
            "{} Elemente ({})",
            self.items.len(),
            format_bytes(self.total_file_bytes())
        )
    }
}

// ============================================================================
// 8. EXIF / Media Metadata & Timestamp / Attribute Editor (MetadataAndAttributeService)
// ============================================================================

#[derive(Debug, Clone, Default)]
pub struct MediaMetadataInfo {
    pub width: Option<u32>,
    pub height: Option<u32>,
    pub resolution_text: String,
    pub date_taken: Option<String>,
    pub camera_make: Option<String>,
    pub camera_model: Option<String>,
    pub iso: Option<u32>,
    pub format_name: String,
}

#[derive(Debug, Clone, Default)]
pub struct BatchAttributeTimestampOptions {
    pub shift_timestamps: bool,
    pub shift_hours: i64,
    pub touch_to_now: bool,
    pub set_readonly: Option<bool>,
}

pub struct MetadataAndAttributeService;

impl MetadataAndAttributeService {
    pub fn inspect_media_metadata(path: impl AsRef<Path>) -> MediaMetadataInfo {
        let p = path.as_ref();
        let mut info = MediaMetadataInfo::default();
        let Ok(mut f) = File::open(p) else {
            return info;
        };
        let mut header = [0u8; 4096];
        let Ok(n) = f.read(&mut header) else {
            return info;
        };
        let buf = &header[..n];

        // PNG: 89 50 4E 47 0D 0A 1A 0A + IHDR at offset 12..24
        if n >= 24 && &buf[0..8] == b"\x89PNG\r\n\x1a\n" {
            let w = u32::from_be_bytes([buf[16], buf[17], buf[18], buf[19]]);
            let h = u32::from_be_bytes([buf[20], buf[21], buf[22], buf[23]]);
            info.width = Some(w);
            info.height = Some(h);
            info.resolution_text = format!("{}x{}", w, h);
            info.format_name = "PNG".to_string();
        }
        // GIF: GIF87a or GIF89a
        else if n >= 10 && (&buf[0..6] == b"GIF89a" || &buf[0..6] == b"GIF87a") {
            let w = u16::from_le_bytes([buf[6], buf[7]]) as u32;
            let h = u16::from_le_bytes([buf[8], buf[9]]) as u32;
            info.width = Some(w);
            info.height = Some(h);
            info.resolution_text = format!("{}x{}", w, h);
            info.format_name = "GIF".to_string();
        }
        // BMP: 'BM' + width/height at offset 18..26
        else if n >= 26 && &buf[0..2] == b"BM" {
            let w = i32::from_le_bytes([buf[18], buf[19], buf[20], buf[21]]).unsigned_abs();
            let h = i32::from_le_bytes([buf[22], buf[23], buf[24], buf[25]]).unsigned_abs();
            info.width = Some(w);
            info.height = Some(h);
            info.resolution_text = format!("{}x{}", w, h);
            info.format_name = "BMP".to_string();
        }
        // JPEG: FF D8
        else if n >= 4 && buf[0] == 0xFF && buf[1] == 0xD8 {
            info.format_name = "JPEG".to_string();
            let mut pos = 2usize;
            while pos + 9 < n {
                if buf[pos] != 0xFF {
                    pos += 1;
                    continue;
                }
                let marker = buf[pos + 1];
                let seg_len = u16::from_be_bytes([buf[pos + 2], buf[pos + 3]]) as usize;
                if (0xC0..=0xC3).contains(&marker) && pos + 9 <= n {
                    let h = u16::from_be_bytes([buf[pos + 5], buf[pos + 6]]) as u32;
                    let w = u16::from_be_bytes([buf[pos + 7], buf[pos + 8]]) as u32;
                    info.width = Some(w);
                    info.height = Some(h);
                    info.resolution_text = format!("{}x{}", w, h);
                    break;
                }
                if seg_len < 2 {
                    break;
                }
                pos += 2 + seg_len;
            }
        }

        info
    }

    pub fn apply_batch_changes(
        paths: &[String],
        options: &BatchAttributeTimestampOptions,
    ) -> Result<usize, String> {
        let mut count = 0usize;
        for p in paths {
            let path = Path::new(p);
            if !path.exists() {
                continue;
            }
            if options.touch_to_now {
                let now = FileTime::now();
                filetime::set_file_times(path, now, now).map_err(|e| e.to_string())?;
            } else if options.shift_timestamps && options.shift_hours != 0 {
                if let Ok(meta) = fs::metadata(path) {
                    if let Ok(mtime) = meta.modified() {
                        let dt: DateTime<Local> = DateTime::from(mtime);
                        let shifted = dt + Duration::hours(options.shift_hours);
                        let ft = FileTime::from_unix_time(shifted.timestamp(), 0);
                        filetime::set_file_mtime(path, ft).map_err(|e| e.to_string())?;
                    }
                }
            }
            if let Some(ro) = options.set_readonly {
                if let Ok(meta) = fs::metadata(path) {
                    let mut perms = meta.permissions();
                    perms.set_readonly(ro);
                    let _ = fs::set_permissions(path, perms);
                }
            }
            count += 1;
        }
        Ok(count)
    }
}

// ============================================================================
// 9. Embedded Quick Command Runner (QuickCommandRunnerService)
// ============================================================================

#[derive(Debug, Clone)]
pub struct QuickCommandResult {
    pub command_line: String,
    pub exit_code: i32,
    pub output_text: String,
    pub navigate_to_directory: Option<String>,
}

pub struct QuickCommandRunnerService {
    pub history: Vec<String>,
}

impl Default for QuickCommandRunnerService {
    fn default() -> Self {
        Self::new()
    }
}

pub fn hidden_command(program: &str) -> Command {
    let mut cmd = Command::new(program);
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd
}

impl QuickCommandRunnerService {
    pub fn new() -> Self {
        Self {
            history: Vec::new(),
        }
    }

    pub fn execute(&mut self, command_line: &str, working_dir: &Path) -> QuickCommandResult {
        let trimmed = command_line.trim();
        if !trimmed.is_empty() {
            self.history.push(trimmed.to_string());
        }

        // Check built-in `cd <target>` navigation
        if trimmed.eq_ignore_ascii_case("cd") {
            return QuickCommandResult {
                command_line: trimmed.to_string(),
                exit_code: 0,
                output_text: working_dir.to_string_lossy().to_string(),
                navigate_to_directory: None,
            };
        }
        if let Some(rest) = trimmed.strip_prefix("cd ").or_else(|| trimmed.strip_prefix("CD ")) {
            let target_str = rest.trim().trim_matches('"').trim_matches('\'');
            let candidate = working_dir.join(target_str);
            let resolved = fs::canonicalize(&candidate).unwrap_or(candidate);
            if resolved.is_dir() {
                let clean = resolved
                    .to_string_lossy()
                    .trim_start_matches(r"\\?\")
                    .to_string();
                return QuickCommandResult {
                    command_line: trimmed.to_string(),
                    exit_code: 0,
                    output_text: format!("Navigated to {}", clean),
                    navigate_to_directory: Some(clean),
                };
            } else {
                return QuickCommandResult {
                    command_line: trimmed.to_string(),
                    exit_code: 1,
                    output_text: format!("Directory not found: {}", target_str),
                    navigate_to_directory: None,
                };
            }
        }

        #[cfg(target_os = "windows")]
        let output = hidden_command("cmd")
            .args(["/C", trimmed])
            .current_dir(working_dir)
            .output();

        #[cfg(not(target_os = "windows"))]
        let output = hidden_command("sh")
            .args(["-c", trimmed])
            .current_dir(working_dir)
            .output();

        match output {
            Ok(out) => {
                let stdout = String::from_utf8_lossy(&out.stdout);
                let stderr = String::from_utf8_lossy(&out.stderr);
                let combined = if stderr.trim().is_empty() {
                    stdout.to_string()
                } else {
                    format!("{}\n{}", stdout, stderr)
                };
                QuickCommandResult {
                    command_line: trimmed.to_string(),
                    exit_code: out.status.code().unwrap_or(0),
                    output_text: combined.trim().to_string(),
                    navigate_to_directory: None,
                }
            }
            Err(e) => QuickCommandResult {
                command_line: trimmed.to_string(),
                exit_code: -1,
                output_text: format!("Failed to execute command: {}", e),
                navigate_to_directory: None,
            },
        }
    }
}

// ============================================================================
// 10. Extended Git Integration Panel (GitIntegrationService)
// ============================================================================

#[derive(Debug, Clone)]
pub struct GitFileStatusEntry {
    pub status_code: String,
    pub relative_path: String,
    pub full_path: String,
}

#[derive(Debug, Clone)]
pub struct GitCommitEntry {
    pub short_hash: String,
    pub author: String,
    pub date: String,
    pub subject: String,
}

#[derive(Debug, Clone, Default)]
pub struct GitRepoSnapshot {
    pub is_repo: bool,
    pub repo_root: String,
    pub current_branch: String,
    pub branches: Vec<String>,
    pub changed_files: Vec<GitFileStatusEntry>,
    pub recent_commits: Vec<GitCommitEntry>,
}

pub struct GitIntegrationService;

impl GitIntegrationService {
    /// Ultra-fast pure-Rust check for `.git/HEAD` without spawning any child process.
    pub fn get_quick_badge(dir: &Path) -> Option<String> {
        let mut cur: Option<&Path> = Some(dir);
        while let Some(p) = cur {
            let dot_git = p.join(".git");
            if dot_git.is_dir() {
                let head_file = dot_git.join("HEAD");
                if let Ok(head_raw) = fs::read_to_string(&head_file) {
                    let trimmed = head_raw.trim();
                    let branch = if let Some(rest) = trimmed.strip_prefix("ref:") {
                        let r = rest.trim();
                        r.strip_prefix("refs/heads/")
                            .unwrap_or_else(|| r.rsplit('/').next().unwrap_or(r))
                            .to_string()
                    } else if trimmed.len() >= 7 {
                        trimmed[..7].to_string()
                    } else {
                        "HEAD".to_string()
                    };
                    return Some(format!("⎇ {}", branch));
                }
            } else if dot_git.is_file() {
                // Worktree / submodule .git file pointing to gitdir
                if let Ok(gitfile_raw) = fs::read_to_string(&dot_git) {
                    if let Some(gitdir) = gitfile_raw.trim().strip_prefix("gitdir:") {
                        let gd_path = p.join(gitdir.trim());
                        if let Ok(head_raw) = fs::read_to_string(gd_path.join("HEAD")) {
                            let trimmed = head_raw.trim();
                            let branch = if let Some(rest) = trimmed.strip_prefix("ref:") {
                                let r = rest.trim();
                                r.strip_prefix("refs/heads/")
                                    .unwrap_or_else(|| r.rsplit('/').next().unwrap_or(r))
                                    .to_string()
                            } else if trimmed.len() >= 7 {
                                trimmed[..7].to_string()
                            } else {
                                "HEAD".to_string()
                            };
                            return Some(format!("⎇ {}", branch));
                        }
                    }
                }
            }
            cur = p.parent();
        }
        None
    }

    pub fn inspect_repository(dir: &Path) -> GitRepoSnapshot {
        let Ok(root_out) = hidden_command("git")
            .args(["rev-parse", "--show-toplevel"])
            .current_dir(dir)
            .output()
        else {
            return GitRepoSnapshot::default();
        };
        if !root_out.status.success() {
            return GitRepoSnapshot::default();
        }
        let repo_root = String::from_utf8_lossy(&root_out.stdout).trim().to_string();
        let root_path = Path::new(&repo_root);

        let current_branch = hidden_command("git")
            .args(["rev-parse", "--abbrev-ref", "HEAD"])
            .current_dir(root_path)
            .output()
            .ok()
            .map(|o| String::from_utf8_lossy(&o.stdout).trim().to_string())
            .unwrap_or_else(|| "HEAD".to_string());

        let branches = hidden_command("git")
            .args(["branch", "--format=%(refname:short)"])
            .current_dir(root_path)
            .output()
            .ok()
            .map(|o| {
                String::from_utf8_lossy(&o.stdout)
                    .lines()
                    .map(|l| l.trim().to_string())
                    .filter(|l| !l.is_empty())
                    .collect()
            })
            .unwrap_or_default();

        let changed_files = hidden_command("git")
            .args(["status", "--porcelain"])
            .current_dir(root_path)
            .output()
            .ok()
            .map(|o| {
                String::from_utf8_lossy(&o.stdout)
                    .lines()
                    .filter(|l| l.len() > 3)
                    .map(|line| {
                        let code = line[..2].trim().to_string();
                        let rel = line[3..].trim().trim_matches('"').to_string();
                        let full = root_path.join(&rel).to_string_lossy().to_string();
                        GitFileStatusEntry {
                            status_code: code,
                            relative_path: rel,
                            full_path: full,
                        }
                    })
                    .collect()
            })
            .unwrap_or_default();

        let recent_commits = hidden_command("git")
            .args(["log", "-n", "20", "--pretty=format:%h|%an|%ad|%s", "--date=short"])
            .current_dir(root_path)
            .output()
            .ok()
            .map(|o| {
                String::from_utf8_lossy(&o.stdout)
                    .lines()
                    .filter_map(|line| {
                        let parts: Vec<&str> = line.splitn(4, '|').collect();
                        if parts.len() == 4 {
                            Some(GitCommitEntry {
                                short_hash: parts[0].to_string(),
                                author: parts[1].to_string(),
                                date: parts[2].to_string(),
                                subject: parts[3].to_string(),
                            })
                        } else {
                            None
                        }
                    })
                    .collect()
            })
            .unwrap_or_default();

        GitRepoSnapshot {
            is_repo: true,
            repo_root,
            current_branch,
            branches,
            changed_files,
            recent_commits,
        }
    }

    pub fn checkout_branch(repo_root: &Path, branch_name: &str) -> Result<String, String> {
        let out = hidden_command("git")
            .args(["checkout", branch_name])
            .current_dir(repo_root)
            .output()
            .map_err(|e| e.to_string())?;
        if out.status.success() {
            Ok(format!("Switched to branch '{}'", branch_name))
        } else {
            Err(String::from_utf8_lossy(&out.stderr).trim().to_string())
        }
    }

    pub fn stage_file(repo_root: &Path, relative_path: &str) -> Result<(), String> {
        let out = hidden_command("git")
            .args(["add", "--", relative_path])
            .current_dir(repo_root)
            .output()
            .map_err(|e| e.to_string())?;
        if out.status.success() {
            Ok(())
        } else {
            Err(String::from_utf8_lossy(&out.stderr).trim().to_string())
        }
    }

    pub fn unstage_file(repo_root: &Path, relative_path: &str) -> Result<(), String> {
        let out = hidden_command("git")
            .args(["reset", "HEAD", "--", relative_path])
            .current_dir(repo_root)
            .output()
            .map_err(|e| e.to_string())?;
        if out.status.success() {
            Ok(())
        } else {
            Err(String::from_utf8_lossy(&out.stderr).trim().to_string())
        }
    }

    pub fn commit(repo_root: &Path, message: &str) -> Result<String, String> {
        let out = hidden_command("git")
            .args(["commit", "-m", message])
            .current_dir(repo_root)
            .output()
            .map_err(|e| e.to_string())?;
        if out.status.success() {
            Ok(String::from_utf8_lossy(&out.stdout).trim().to_string())
        } else {
            Err(String::from_utf8_lossy(&out.stderr).trim().to_string())
        }
    }

    pub fn get_head_file_content(repo_root: &Path, relative_path: &str) -> Option<String> {
        let spec = format!("HEAD:{}", relative_path.replace('\\', "/"));
        let out = hidden_command("git")
            .args(["show", &spec])
            .current_dir(repo_root)
            .output()
            .ok()?;
        if out.status.success() {
            Some(String::from_utf8_lossy(&out.stdout).to_string())
        } else {
            None
        }
    }
}

