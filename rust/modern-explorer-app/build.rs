use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

fn find_rc_exe() -> Option<PathBuf> {
    if Command::new("rc.exe").arg("/?").output().is_ok() {
        return Some(PathBuf::from("rc.exe"));
    }

    let kits_root = Path::new(r"C:\Program Files (x86)\Windows Kits\10\bin");
    if let Ok(entries) = fs::read_dir(kits_root) {
        let mut versions: Vec<PathBuf> = entries
            .flatten()
            .map(|e| e.path())
            .filter(|p| p.is_dir())
            .collect();
        versions.sort();
        versions.reverse();
        for ver_dir in versions {
            let candidate = ver_dir.join("x64").join("rc.exe");
            if candidate.is_file() {
                return Some(candidate);
            }
        }
    }

    None
}

fn main() {
    println!("cargo:rerun-if-changed=assets/AppIcon.ico");
    println!("cargo:rerun-if-changed=assets/AppIcon.png");
    println!("cargo:rerun-if-changed=assets/AppIcon64.rgba");

    let target_os = env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    if target_os == "windows" {
        let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
        let ico_path = manifest_dir.join("assets").join("AppIcon.ico");
        if ico_path.is_file() {
            if let Some(rc_exe) = find_rc_exe() {
                let out_dir = PathBuf::from(env::var("OUT_DIR").unwrap());
                let rc_file = out_dir.join("app_icon.rc");
                let res_file = out_dir.join("app_icon.res");

                let escaped_ico = ico_path.to_string_lossy().replace('\\', "\\\\");
                let rc_content = format!("1 ICON \"{}\"\r\n", escaped_ico);
                if fs::write(&rc_file, rc_content).is_ok() {
                    let status = Command::new(rc_exe)
                        .args([
                            "/nologo",
                            "/fo",
                            &res_file.to_string_lossy(),
                            &rc_file.to_string_lossy(),
                        ])
                        .status();
                    if let Ok(s) = status {
                        if s.success() && res_file.is_file() {
                            println!("cargo:rustc-link-arg-bins={}", res_file.display());
                        }
                    }
                }
            }
        }
    }
}

