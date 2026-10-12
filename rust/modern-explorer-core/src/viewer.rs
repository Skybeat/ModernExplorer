use std::fs::{self, File};
use std::io::Read;
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum QuickViewMode {
    TextAndCode,
    HexDump,
    StructuredData,
    LogViewer,
    MediaAndMetadata,
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
            .map(|l| l.split(sep).map(|c| c.trim().trim_matches('"').to_string()).collect())
            .collect()
    }
}

