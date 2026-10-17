use serde::{Deserialize, Serialize};
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum QuickViewMode {
    TextAndCode,
    HexDump,
    StructuredData,
    LogViewer,
    MediaAndMetadata,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HexEditorPageDto {
    pub total_size: u64,
    pub offset: u64,
    pub bytes: Vec<u8>,
}

pub struct FileContentService;

impl FileContentService {
    pub fn read_text_preview(path: &Path, max_bytes: usize) -> Result<String, String> {
        let mut f = File::open(path).map_err(|e| e.to_string())?;
        let mut buf = vec![0u8; max_bytes.max(1024)];
        let n = f.read(&mut buf).map_err(|e| e.to_string())?;
        Ok(String::from_utf8_lossy(&buf[..n]).to_string())
    }

    pub fn save_text(path: &Path, content: &str) -> Result<(), String> {
        fs::write(path, content).map_err(|e| e.to_string())
    }

    pub fn read_hex_editor_page(
        path: &Path,
        offset: u64,
        length: usize,
    ) -> Result<HexEditorPageDto, String> {
        let mut f = File::open(path).map_err(|e| e.to_string())?;
        let total_size = f.metadata().map_err(|e| e.to_string())?.len();
        let clamped_len = length.clamp(16, 16384);
        let safe_offset = offset.min(total_size);
        f.seek(SeekFrom::Start(safe_offset))
            .map_err(|e| e.to_string())?;
        let mut buf = vec![0u8; clamped_len];
        let n = f.read(&mut buf).map_err(|e| e.to_string())?;
        buf.truncate(n);
        Ok(HexEditorPageDto {
            total_size,
            offset: safe_offset,
            bytes: buf,
        })
    }

    pub fn apply_hex_patches(path: &Path, patches: &[(u64, u8)]) -> Result<usize, String> {
        let mut f = OpenOptions::new()
            .read(true)
            .write(true)
            .open(path)
            .map_err(|e| e.to_string())?;
        let mut count = 0usize;
        for &(offset, val) in patches {
            f.seek(SeekFrom::Start(offset)).map_err(|e| e.to_string())?;
            f.write_all(&[val]).map_err(|e| e.to_string())?;
            count += 1;
        }
        f.flush().map_err(|e| e.to_string())?;
        Ok(count)
    }

    pub fn search_binary(
        path: &Path,
        query: &str,
        is_hex: bool,
        max_matches: usize,
    ) -> Result<Vec<u64>, String> {
        let needle: Vec<u8> = if is_hex {
            let cleaned: String = query
                .chars()
                .filter(|c| c.is_ascii_hexdigit())
                .collect();
            if cleaned.is_empty() || cleaned.len() % 2 != 0 {
                return Err("Ungültige Hex-Sequenz (gerade Anzahl Hex-Zeichen erforderlich)".to_string());
            }
            (0..cleaned.len())
                .step_by(2)
                .filter_map(|i| u8::from_str_radix(&cleaned[i..i + 2], 16).ok())
                .collect()
        } else {
            query.as_bytes().to_vec()
        };

        if needle.is_empty() {
            return Ok(Vec::new());
        }

        let data = fs::read(path).map_err(|e| e.to_string())?;
        let mut matches = Vec::new();
        if data.len() >= needle.len() {
            for i in 0..=(data.len() - needle.len()) {
                if &data[i..i + needle.len()] == needle.as_slice() {
                    matches.push(i as u64);
                    if matches.len() >= max_matches.max(1) {
                        break;
                    }
                }
            }
        }
        Ok(matches)
    }

    pub fn format_hex_dump(path: &Path, max_bytes: usize) -> Result<String, String> {
        let mut f = File::open(path).map_err(|e| e.to_string())?;
        let mut buf = vec![0u8; max_bytes.clamp(64, 65536)];
        let n = f.read(&mut buf).map_err(|e| e.to_string())?;
        let data = &buf[..n];

        let mut out = String::new();
        for (row_idx, chunk) in data.chunks(16).enumerate() {
            let offset = row_idx * 16;
            out.push_str(&format!("{:08X}  ", offset));
            for i in 0..16 {
                if i < chunk.len() {
                    out.push_str(&format!("{:02X} ", chunk[i]));
                } else {
                    out.push_str("   ");
                }
                if i == 7 {
                    out.push(' ');
                }
            }
            out.push_str(" |");
            for &b in chunk {
                let ch = if (0x20..=0x7E).contains(&b) {
                    b as char
                } else {
                    '.'
                };
                out.push(ch);
            }
            out.push_str("|\n");
        }
        Ok(out)
    }

    pub fn filter_log_lines(content: &str, level_filter: &str, search_query: &str) -> Vec<String> {
        let lvl = level_filter.trim().to_ascii_uppercase();
        let q = search_query.trim().to_lowercase();
        content
            .lines()
            .filter(|line| {
                let level_ok = if lvl.is_empty() || lvl == "ALL" {
                    true
                } else {
                    line.to_ascii_uppercase().contains(&lvl)
                };
                let query_ok = if q.is_empty() {
                    true
                } else {
                    line.to_lowercase().contains(&q)
                };
                level_ok && query_ok
            })
            .map(|s| s.to_string())
            .collect()
    }

    pub fn parse_csv_rows(content: &str, max_rows: usize) -> Vec<Vec<String>> {
        let sep = if content.lines().next().map(|l| l.contains('\t')).unwrap_or(false) {
            '\t'
        } else if content.lines().next().map(|l| l.contains(';')).unwrap_or(false) {
            ';'
        } else {
            ','
        };
        content
            .lines()
            .take(max_rows.max(1))
            .map(|l| {
                l.split(sep)
                    .map(|c| c.trim().trim_matches('"').to_string())
                    .collect()
            })
            .collect()
    }
}
