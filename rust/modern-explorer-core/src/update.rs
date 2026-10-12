use regex::Regex;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GitHubAssetInfo {
    pub name: String,
    pub browser_download_url: String,
    pub size: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GitHubReleaseInfo {
    pub tag_name: String,
    pub name: String,
    pub body: String,
    pub html_url: String,
    pub published_at: String,
    pub assets: Vec<GitHubAssetInfo>,
}

pub struct GitHubUpdateService;

impl GitHubUpdateService {
    pub const CURRENT_VERSION: &'static str = "1.9.2";
    pub const RELEASES_URL: &'static str = "https://github.com/Skybeat/ModernExplorer/releases";
    pub const LATEST_API_URL: &'static str =
        "https://api.github.com/repos/Skybeat/ModernExplorer/releases/latest";

    pub fn parse_semver(tag: &str) -> (u32, u32, u32) {
        let cleaned = tag.trim().trim_start_matches(['v', 'V']);
        let core = cleaned.split(['-', '+']).next().unwrap_or(cleaned);
        let mut parts = core.split('.').filter_map(|p| p.parse::<u32>().ok());
        (
            parts.next().unwrap_or(0),
            parts.next().unwrap_or(0),
            parts.next().unwrap_or(0),
        )
    }

    pub fn is_newer_version(remote_tag: &str, current_version: &str) -> bool {
        Self::parse_semver(remote_tag) > Self::parse_semver(current_version)
    }

    pub fn check_for_update() -> Result<Option<GitHubReleaseInfo>, String> {
        let resp = ureq::get(Self::LATEST_API_URL)
            .set("User-Agent", "ModernExplorer-Rust-Updater")
            .set("Accept", "application/vnd.github+json")
            .call()
            .map_err(|e| e.to_string())?;

        let release: GitHubReleaseInfo = resp.into_json().map_err(|e| e.to_string())?;
        if Self::is_newer_version(&release.tag_name, Self::CURRENT_VERSION) {
            Ok(Some(release))
        } else {
            Ok(None)
        }
    }

    /// Formats raw GitHub Markdown release notes into a clean, multi-line readable text
    /// suitable for the multi-line scrollable Release Notes control in the Update & About dialogs.
    pub fn format_release_notes_for_display(raw_body: &str) -> String {
        let trimmed = raw_body.trim();
        if trimmed.is_empty() {
            return String::new();
        }
        let link_re = Regex::new(r"\[([^\]]+)\]\(([^)]+)\)").unwrap();
        let normalized = trimmed.replace("\r\n", "\n").replace('\r', "\n");
        let mut lines_out = Vec::new();
        let mut prev_blank = false;

        for raw_line in normalized.lines() {
            let mut line = raw_line.trim_end().to_string();
            let t = line.trim();
            if t.is_empty() {
                if !prev_blank && !lines_out.is_empty() {
                    lines_out.push(String::new());
                    prev_blank = true;
                }
                continue;
            }
            if t == "---" || t == "***" || t == "___" {
                continue;
            }
            prev_blank = false;

            // Strip Markdown links [label](url) -> label
            line = link_re.replace_all(&line, "$1").to_string();

            let t_clean = line.trim();
            if let Some(h3) = t_clean.strip_prefix("### ") {
                lines_out.push(format!("▸ {}", h3.replace("**", "").replace('`', "")));
            } else if let Some(h2) = t_clean.strip_prefix("## ") {
                lines_out.push(format!("━━ {} ━━", h2.replace("**", "").replace('`', "")));
            } else if let Some(h1) = t_clean.strip_prefix("# ") {
                lines_out.push(format!("━━ {} ━━", h1.replace("**", "").replace('`', "")));
            } else if t_clean.starts_with("- ") || t_clean.starts_with("* ") {
                let indent = line.len() - line.trim_start().len();
                let bullet = if indent >= 2 { "   ◦ " } else { "• " };
                let body = t_clean[2..].replace("**", "").replace('`', "");
                lines_out.push(format!("{}{}", bullet, body));
            } else {
                lines_out.push(t_clean.replace("**", "").replace('`', ""));
            }
        }

        lines_out.join("\n").trim().to_string()
    }

    pub fn open_release_in_browser(url: Option<&str>) {
        let target = url
            .filter(|s| !s.trim().is_empty())
            .unwrap_or(Self::RELEASES_URL);
        let _ = open::that(target);
    }
}

