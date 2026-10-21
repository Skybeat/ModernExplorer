#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod api;

use api::BackendState;
use modern_explorer_core::{settings::SettingsStore, update::GitHubUpdateService};
use serde_json::Value;
use std::sync::{Arc, Mutex};
use tauri::{Manager, PhysicalPosition, PhysicalSize, Position, Size};

fn percent_decode(input: &str) -> String {
    let mut bytes = Vec::with_capacity(input.len());
    let raw = input.as_bytes();
    let mut i = 0;
    while i < raw.len() {
        if raw[i] == b'%' && i + 2 < raw.len() {
            if let Ok(hex_str) = std::str::from_utf8(&raw[i + 1..i + 3]) {
                if let Ok(b) = u8::from_str_radix(hex_str, 16) {
                    bytes.push(b);
                    i += 3;
                    continue;
                }
            }
        } else if raw[i] == b'+' {
            bytes.push(b' ');
            i += 1;
            continue;
        }
        bytes.push(raw[i]);
        i += 1;
    }
    String::from_utf8_lossy(&bytes).to_string()
}

fn guess_mime_type(path: &str) -> &'static str {
    let lower = path.to_ascii_lowercase();
    if lower.ends_with(".png") || lower.ends_with(".apng") {
        "image/png"
    } else if lower.ends_with(".jpg") || lower.ends_with(".jpeg") || lower.ends_with(".jpe") || lower.ends_with(".jfif") {
        "image/jpeg"
    } else if lower.ends_with(".gif") {
        "image/gif"
    } else if lower.ends_with(".webp") {
        "image/webp"
    } else if lower.ends_with(".avif") {
        "image/avif"
    } else if lower.ends_with(".svg") {
        "image/svg+xml"
    } else if lower.ends_with(".bmp") {
        "image/bmp"
    } else if lower.ends_with(".ico") {
        "image/x-icon"
    } else if lower.ends_with(".tif") || lower.ends_with(".tiff") {
        "image/tiff"
    } else {
        "application/octet-stream"
    }
}

#[tauri::command]
fn rpc(
    action: String,
    params: Value,
    state: tauri::State<Arc<Mutex<BackendState>>>,
) -> Result<Value, String> {
    if let Ok(mut guard) = state.lock() {
        Ok(guard.handle_rpc(&action, &params))
    } else {
        Err("Backend state lock poisoned".to_string())
    }
}

fn main() {
    #[cfg(target_os = "linux")]
    {
        if std::env::var_os("GDK_BACKEND").is_none() {
            std::env::set_var("GDK_BACKEND", "x11");
        }
    }

    GitHubUpdateService::cleanup_old_versions();

    let state = Arc::new(Mutex::new(BackendState::new()));
    let (win_w, win_h, win_x, win_y, win_max) = {
        let g = state.lock().unwrap();
        (
            g.settings.main_window_width.clamp(980.0, 3840.0),
            g.settings.main_window_height.clamp(620.0, 2160.0),
            g.settings.main_window_x,
            g.settings.main_window_y,
            g.settings.main_window_maximized,
        )
    };

    let state_events = state.clone();

    tauri::Builder::default()
        .manage(state.clone())
        .invoke_handler(tauri::generate_handler![rpc])
        .register_uri_scheme_protocol("file-preview", |_app, request| {
            // Handle CORS preflight
            if request.method() == http::Method::OPTIONS {
                return http::Response::builder()
                    .status(204)
                    .header("Access-Control-Allow-Origin", "*")
                    .header("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS")
                    .header("Access-Control-Allow-Headers", "*")
                    .body(Vec::new())
                    .unwrap();
            }

            let mut file_path = String::new();

            // Try 1: Query string (?path=...)
            if let Some(query) = request.uri().query() {
                for pair in query.split('&') {
                    if let Some(val) = pair.strip_prefix("path=") {
                        file_path = percent_decode(val);
                        break;
                    }
                }
            }

            // Try 2: Substring "path=" in full URI
            if file_path.is_empty() {
                let uri_str = request.uri().to_string();
                if let Some(idx) = uri_str.find("path=") {
                    let raw = &uri_str[idx + 5..];
                    let end = raw.find('&').unwrap_or(raw.len());
                    file_path = percent_decode(&raw[..end]);
                }
            }

            // Try 3: Direct path component (/path=... or /C:/...)
            if file_path.is_empty() {
                let p = request.uri().path();
                if let Some(val) = p.strip_prefix("/?path=") {
                    file_path = percent_decode(val);
                } else if let Some(val) = p.strip_prefix("/path=") {
                    file_path = percent_decode(val);
                } else if p.len() > 1 && !p.starts_with("/favicon.ico") {
                    let raw = if p.len() > 3 && p.chars().nth(2) == Some(':') {
                        &p[1..]
                    } else {
                        p
                    };
                    file_path = percent_decode(raw);
                }
            }

            // Normalize Windows drive letter if there is a leading slash/backslash
            if (file_path.starts_with('/') || file_path.starts_with('\\'))
                && file_path.len() > 2
                && file_path.chars().nth(2) == Some(':')
            {
                file_path = file_path[1..].to_string();
            }

            if !file_path.is_empty() {
                let actual_path = if modern_explorer_core::archive::ArchiveService::is_virtual_archive_path(&file_path) {
                    if let Some((arch_file, inner_path)) = modern_explorer_core::archive::ArchiveService::parse_virtual_archive_path(&file_path) {
                        modern_explorer_core::archive::ArchiveService::extract_single_entry_to_temp(&arch_file, &inner_path).ok()
                    } else {
                        None
                    }
                } else {
                    Some(std::path::PathBuf::from(&file_path))
                };

                if let Some(p) = actual_path {
                    if let Ok(bytes) = std::fs::read(&p) {
                        let mime = guess_mime_type(&p.to_string_lossy());
                        return http::Response::builder()
                            .status(200)
                            .header("Content-Type", mime)
                            .header("Cache-Control", "max-age=60")
                            .header("Access-Control-Allow-Origin", "*")
                            .header("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS")
                            .body(bytes)
                            .unwrap();
                    }
                }
            }
            http::Response::builder()
                .status(404)
                .header("Access-Control-Allow-Origin", "*")
                .body(Vec::new())
                .unwrap()
        })
        .setup(move |app| {
            if let Some(main_window) = app.get_webview_window("main") {
                let title = format!(
                    "ModernExplorer Studio — Dual-Pane File Manager v{} (Rust Edition)",
                    GitHubUpdateService::CURRENT_VERSION
                );
                let _ = main_window.set_title(&title);

                if let (Some(x), Some(y)) = (win_x, win_y) {
                    if x > -10000 && y > -10000 {
                        let _ = main_window.set_position(Position::Physical(PhysicalPosition::new(x, y)));
                    }
                }
                let _ = main_window.set_size(Size::Physical(PhysicalSize::new(win_w as u32, win_h as u32)));

                if win_max {
                    let _ = main_window.maximize();
                }
            }
            Ok(())
        })
        .on_window_event(move |window, event| {
            if window.label() == "main" {
                match event {
                    tauri::WindowEvent::Resized(size) => {
                        if let Ok(is_max) = window.is_maximized() {
                            if !is_max && size.width >= 980 && size.height >= 620 {
                                if let Ok(mut g) = state_events.lock() {
                                    g.settings.main_window_width = size.width as f32;
                                    g.settings.main_window_height = size.height as f32;
                                    g.settings.main_window_maximized = false;
                                }
                            } else if is_max {
                                if let Ok(mut g) = state_events.lock() {
                                    g.settings.main_window_maximized = true;
                                }
                            }
                        }
                    }
                    tauri::WindowEvent::Moved(pos) => {
                        if let Ok(is_max) = window.is_maximized() {
                            if !is_max && pos.x > -10000 && pos.y > -10000 {
                                if let Ok(mut g) = state_events.lock() {
                                    g.settings.main_window_x = Some(pos.x);
                                    g.settings.main_window_y = Some(pos.y);
                                }
                            }
                        }
                    }
                    tauri::WindowEvent::Destroyed => {
                        if let Ok(g) = state_events.lock() {
                            SettingsStore::save(&g.settings);
                        }
                    }
                    _ => {}
                }
            }
        })
        .run(tauri::generate_context!())
        .expect("Error running ModernExplorer Tauri application");
}
