#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod api;

use api::BackendState;
use modern_explorer_core::{app_paths::AppPaths, settings::SettingsStore};
use serde::Deserialize;
use serde_json::{json, Value};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};
#[cfg(target_os = "windows")]
use tao::platform::windows::WindowBuilderExtWindows;
use tao::{
    dpi::{LogicalSize, PhysicalPosition},
    event::{Event, WindowEvent},
    event_loop::{ControlFlow, EventLoop},
    window::{Icon, WindowBuilder},
};
use tiny_http::{Header, Method, Response, Server};
use wry::{WebContext, WebViewBuilder};

const INDEX_HTML: &str = include_str!("ui.html");
const APP_ICON_ICO: &[u8] = include_bytes!("../assets/AppIcon.ico");
const APP_ICON_PNG: &[u8] = include_bytes!("../assets/AppIcon.png");
const APP_ICON_RGBA64: &[u8] = include_bytes!("../assets/AppIcon64.rgba");

#[derive(Deserialize)]
struct RpcRequest {
    action: String,
    #[serde(default)]
    params: Value,
}

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
    if lower.ends_with(".png") {
        "image/png"
    } else if lower.ends_with(".jpg") || lower.ends_with(".jpeg") || lower.ends_with(".jfif") {
        "image/jpeg"
    } else if lower.ends_with(".gif") {
        "image/gif"
    } else if lower.ends_with(".webp") {
        "image/webp"
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

fn start_local_rpc_server(state: Arc<Mutex<BackendState>>) -> u16 {
    let server = Server::http("127.0.0.1:0").expect("Failed to start embedded HTTP RPC server");
    let port = server
        .server_addr()
        .to_ip()
        .map(|addr| addr.port())
        .expect("Failed to obtain local port");

    thread::spawn(move || {
        for mut request in server.incoming_requests() {
            let method = request.method().clone();
            let url = request.url().to_string();

            if method == Method::Get && (url == "/" || url.starts_with("/index.html") || url.starts_with("/?")) {
                let header = Header::from_bytes(&b"Content-Type"[..], &b"text/html; charset=utf-8"[..]).unwrap();
                let cache = Header::from_bytes(&b"Cache-Control"[..], &b"no-store"[..]).unwrap();
                let response = Response::from_string(INDEX_HTML)
                    .with_header(header)
                    .with_header(cache);
                let _ = request.respond(response);
                continue;
            }

            if method == Method::Get && (url == "/app-icon.png" || url.starts_with("/app-icon.png?")) {
                let header = Header::from_bytes(&b"Content-Type"[..], &b"image/png"[..]).unwrap();
                let cache = Header::from_bytes(&b"Cache-Control"[..], &b"max-age=3600"[..]).unwrap();
                let response = Response::from_data(APP_ICON_PNG.to_vec())
                    .with_header(header)
                    .with_header(cache);
                let _ = request.respond(response);
                continue;
            }

            if method == Method::Get && (url == "/favicon.ico" || url.starts_with("/favicon.ico?")) {
                let header = Header::from_bytes(&b"Content-Type"[..], &b"image/x-icon"[..]).unwrap();
                let cache = Header::from_bytes(&b"Cache-Control"[..], &b"max-age=3600"[..]).unwrap();
                let response = Response::from_data(APP_ICON_ICO.to_vec())
                    .with_header(header)
                    .with_header(cache);
                let _ = request.respond(response);
                continue;
            }

            if method == Method::Get && url.starts_with("/file?") {
                let query = &url["/file?".len()..];
                let mut file_path = String::new();
                for pair in query.split('&') {
                    if let Some(val) = pair.strip_prefix("path=") {
                        file_path = percent_decode(val);
                        break;
                    }
                }
                if !file_path.is_empty() {
                    if let Ok(bytes) = std::fs::read(&file_path) {
                        let mime = guess_mime_type(&file_path);
                        let header = Header::from_bytes(&b"Content-Type"[..], mime.as_bytes()).unwrap();
                        let cache = Header::from_bytes(&b"Cache-Control"[..], &b"max-age=60"[..]).unwrap();
                        let response = Response::from_data(bytes)
                            .with_header(header)
                            .with_header(cache);
                        let _ = request.respond(response);
                        continue;
                    }
                }
                let _ = request.respond(Response::from_string("File not found").with_status_code(404));
                continue;
            }

            if method == Method::Post && url.starts_with("/api") {
                let mut body = String::new();
                let _ = request.as_reader().read_to_string(&mut body);

                let reply = match serde_json::from_str::<RpcRequest>(&body) {
                    Ok(rpc) => {
                        if let Ok(mut guard) = state.lock() {
                            guard.handle_rpc(&rpc.action, &rpc.params)
                        } else {
                            json!({ "ok": false, "error": "Backend state lock poisoned" })
                        }
                    }
                    Err(e) => json!({ "ok": false, "error": format!("Invalid RPC JSON: {e}") }),
                };

                let json_str = serde_json::to_string(&reply).unwrap_or_else(|_| r#"{"ok":false,"error":"Serialization error"}"#.to_string());
                let header = Header::from_bytes(&b"Content-Type"[..], &b"application/json; charset=utf-8"[..]).unwrap();
                let cache = Header::from_bytes(&b"Cache-Control"[..], &b"no-store"[..]).unwrap();
                let response = Response::from_string(json_str)
                    .with_header(header)
                    .with_header(cache);
                let _ = request.respond(response);
                continue;
            }

            let response = Response::from_string("Not Found").with_status_code(404);
            let _ = request.respond(response);
        }
    });

    port
}

fn main() {
    #[cfg(target_os = "linux")]
    {
        if std::env::var_os("GDK_BACKEND").is_none() {
            std::env::set_var("GDK_BACKEND", "x11");
        }
    }

    let state = Arc::new(Mutex::new(BackendState::new()));
    let (win_w, win_h, win_x, win_y, win_max) = {
        let g = state.lock().unwrap();
        (
            g.settings.main_window_width.clamp(980.0, 3840.0) as f64,
            g.settings.main_window_height.clamp(620.0, 2160.0) as f64,
            g.settings.main_window_x,
            g.settings.main_window_y,
            g.settings.main_window_maximized,
        )
    };
    let port = start_local_rpc_server(state.clone());
    let app_url = format!("http://127.0.0.1:{port}/");

    let app_icon = Icon::from_rgba(APP_ICON_RGBA64.to_vec(), 64, 64).ok();

    let event_loop = EventLoop::new();
    let mut wb = WindowBuilder::new()
        .with_title("ModernExplorer Studio — Dual-Pane File Manager v1.9.3 (Rust Edition)")
        .with_inner_size(LogicalSize::new(win_w, win_h))
        .with_min_inner_size(LogicalSize::new(980.0, 620.0))
        .with_window_icon(app_icon.clone());

    #[cfg(target_os = "windows")]
    {
        wb = wb.with_taskbar_icon(app_icon);
    }

    let window = wb
        .build(&event_loop)
        .expect("Failed to create native window");

    if let (Some(x), Some(y)) = (win_x, win_y) {
        if x > -10000 && y > -10000 {
            let pos = PhysicalPosition::new(x, y);
            window.set_outer_position(pos);
            window.set_inner_size(LogicalSize::new(win_w, win_h));
            window.set_outer_position(pos);
        }
    }

    let mut saved_normal_pos = window
        .outer_position()
        .unwrap_or_else(|_| PhysicalPosition::new(40, 40));
    let mut saved_normal_size = window.inner_size();
    let mut was_maximized = false;
    let mut pending_restore = false;
    let mut restore_guard_until: Option<Instant> = None;

    if win_max {
        window.set_maximized(true);
        was_maximized = true;
    }

    let webview_data_dir = AppPaths::app_data_dir().join("WebView2");
    let mut web_context = WebContext::new(Some(webview_data_dir));

    let _webview = WebViewBuilder::with_web_context(&mut web_context)
        .with_url(&app_url)
        .with_devtools(true)
        .build(&window)
        .expect("Failed to initialize WebView2");

    event_loop.run(move |event, _, control_flow| {
        *control_flow = ControlFlow::Wait;

        match event {
            Event::WindowEvent {
                event: WindowEvent::Moved(pos),
                ..
            } => {
                let is_max = window.is_maximized();
                let is_min = window.is_minimized();
                let in_guard = restore_guard_until
                    .map(|until| Instant::now() < until)
                    .unwrap_or(false);

                if is_max {
                    was_maximized = true;
                    if let Ok(mut guard) = state.lock() {
                        guard.settings.main_window_maximized = true;
                    }
                } else if !is_min {
                    if was_maximized {
                        was_maximized = false;
                        pending_restore = true;
                        restore_guard_until = Some(Instant::now() + Duration::from_millis(250));
                        window.set_outer_position(saved_normal_pos);
                        window.set_inner_size(saved_normal_size);
                        window.set_outer_position(saved_normal_pos);
                        if let Ok(mut guard) = state.lock() {
                            guard.settings.main_window_maximized = false;
                        }
                    } else if in_guard {
                        if pos != saved_normal_pos {
                            window.set_outer_position(saved_normal_pos);
                        }
                    } else if pos.x > -10000 && pos.y > -10000 {
                        saved_normal_pos = pos;
                        let cur_size = window.inner_size();
                        if cur_size.width >= 600 && cur_size.height >= 400 {
                            saved_normal_size = cur_size;
                        }
                        if let Ok(mut guard) = state.lock() {
                            guard.settings.main_window_x = Some(pos.x);
                            guard.settings.main_window_y = Some(pos.y);
                            guard.settings.main_window_maximized = false;
                        }
                    }
                }
            }
            Event::WindowEvent {
                event: WindowEvent::Resized(physical_size),
                ..
            } => {
                let is_max = window.is_maximized();
                let is_min = window.is_minimized();
                let in_guard = restore_guard_until
                    .map(|until| Instant::now() < until)
                    .unwrap_or(false);

                if is_max {
                    was_maximized = true;
                    if let Ok(mut guard) = state.lock() {
                        guard.settings.main_window_maximized = true;
                    }
                } else if !is_min {
                    if was_maximized {
                        was_maximized = false;
                        pending_restore = true;
                        restore_guard_until = Some(Instant::now() + Duration::from_millis(250));
                        window.set_outer_position(saved_normal_pos);
                        window.set_inner_size(saved_normal_size);
                        window.set_outer_position(saved_normal_pos);
                        if let Ok(mut guard) = state.lock() {
                            guard.settings.main_window_maximized = false;
                        }
                    } else if in_guard {
                        if physical_size != saved_normal_size {
                            window.set_inner_size(saved_normal_size);
                        }
                        if window.outer_position().ok() != Some(saved_normal_pos) {
                            window.set_outer_position(saved_normal_pos);
                        }
                    } else {
                        let scale = window.scale_factor();
                        let logical = physical_size.to_logical::<f64>(scale);
                        if logical.width >= 600.0 && logical.height >= 400.0 {
                            saved_normal_size = physical_size;
                            if let Ok(pos) = window.outer_position() {
                                if pos.x > -10000 && pos.y > -10000 {
                                    saved_normal_pos = pos;
                                }
                            }
                            if let Ok(mut guard) = state.lock() {
                                guard.settings.main_window_width = logical.width as f32;
                                guard.settings.main_window_height = logical.height as f32;
                                guard.settings.main_window_x = Some(saved_normal_pos.x);
                                guard.settings.main_window_y = Some(saved_normal_pos.y);
                                guard.settings.main_window_maximized = false;
                            }
                        }
                    }
                }
            }
            Event::MainEventsCleared => {
                if pending_restore {
                    pending_restore = false;
                    window.set_outer_position(saved_normal_pos);
                    window.set_inner_size(saved_normal_size);
                    window.set_outer_position(saved_normal_pos);
                }
            }
            Event::WindowEvent {
                event: WindowEvent::CloseRequested,
                ..
            } => {
                if let Ok(guard) = state.lock() {
                    SettingsStore::save(&guard.settings);
                }
                *control_flow = ControlFlow::Exit;
            }
            _ => {}
        }
    });
}
