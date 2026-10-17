pub mod app_paths;
pub mod archive;
pub mod features;
pub mod filesystem;
pub mod localization;
pub mod operations;
pub mod remote;
pub mod session;
pub mod settings;
pub mod treemap;
pub mod update;
pub mod viewer;

#[cfg(test)]
mod tests {
    use crate::features::*;
    use crate::filesystem::*;
    use crate::operations::*;
    use crate::settings::*;
    use crate::treemap::*;
    use crate::update::*;
    use std::fs;
    use tempfile::TempDir;

    #[test]
    fn file_diff_service_computes_lcs_and_merges_hunks() {
        let left = "line1\nline2_old\nline3";
        let right = "line1\nline2_new\nline3\nline4_added";
        let mut diff = FileDiffService::compare_text(left, right, false);
        assert!(diff.hunk_count >= 2);
        assert!(diff.modified_count >= 1);
        assert!(diff.added_count >= 1);

        FileDiffService::merge_hunk_left_to_right(&mut diff, 1);
        let merged_right = FileDiffService::reconstruct_right_text(&diff);
        assert!(merged_right.contains("line2_old"));
    }

    #[test]
    fn checksum_batch_service_computes_hashes_and_verifies_manifest() {
        let dir = TempDir::new().unwrap();
        let file_a = dir.path().join("alpha.txt");
        fs::write(&file_a, b"ModernExplorerRust").unwrap();

        let entries =
            ChecksumBatchService::compute_checksums(&[file_a.to_string_lossy().to_string()]);
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].sha256.len(), 64);
        assert_eq!(entries[0].sha512.len(), 128);
        assert_eq!(entries[0].md5.len(), 32);
        assert_eq!(entries[0].crc32.len(), 8);

        let manifest_path = dir.path().join("checks.sha256");
        let manifest =
            ChecksumBatchService::format_manifest(&entries, ChecksumManifestFormat::Sha256);
        fs::write(&manifest_path, manifest).unwrap();

        let verified = ChecksumBatchService::verify_manifest(&manifest_path).unwrap();
        assert_eq!(verified.len(), 1);
        assert!(verified[0].is_match);
    }

    #[test]
    fn file_split_join_and_shred_service_roundtrips() {
        let dir = TempDir::new().unwrap();
        let src = dir.path().join("payload.bin");
        let payload: Vec<u8> = (0..1024).map(|i| (i % 251) as u8).collect();
        fs::write(&src, &payload).unwrap();

        let split_dir = dir.path().join("parts");
        let parts = FileSplitAndShredService::split_file(&src, &split_dir, 300).unwrap();
        assert_eq!(parts.len(), 4);

        let rejoined = dir.path().join("rejoined.bin");
        let bytes = FileSplitAndShredService::join_parts(&parts[0], &rejoined).unwrap();
        assert_eq!(bytes, 1024);
        assert_eq!(fs::read(&rejoined).unwrap(), payload);

        FileSplitAndShredService::shred_file(&rejoined, ShredMode::ThreePassDoD).unwrap();
        assert!(!rejoined.exists());
    }

    #[test]
    fn metadata_inspector_and_batch_rename_support_png_dimensions() {
        let dir = TempDir::new().unwrap();
        let png_path = dir.path().join("photo.png");
        let png_bytes: [u8; 24] = [
            0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, b'I', b'H',
            b'D', b'R', 0x00, 0x00, 0x01, 0x40, // 320
            0x00, 0x00, 0x00, 0xF0, // 240
        ];
        fs::write(&png_path, png_bytes).unwrap();

        let info = MetadataAndAttributeService::inspect_media_metadata(&png_path);
        assert_eq!(info.width, Some(320));
        assert_eq!(info.height, Some(240));
        assert_eq!(info.resolution_text, "320x240");

        let fe = FileEntry::try_from_path(&png_path).unwrap();
        let mut opts = BatchRenameOptions::default();
        opts.name_pattern = "img_[Resolution]_[C]".to_string();
        let preview = BatchRenameService::generate_preview(&[fe], &opts);
        assert_eq!(preview[0].new_name, "img_320x240_001.png");
    }

    #[test]
    fn duplicate_finder_and_treemap_scan_work() {
        let dir = TempDir::new().unwrap();
        let f1 = dir.path().join("dup1.dat");
        let f2 = dir.path().join("dup2.dat");
        let data = vec![42u8; 8192];
        fs::write(&f1, &data).unwrap();
        fs::write(&f2, &data).unwrap();

        let dup_res = DuplicateFinderService::scan_directory(dir.path(), 1);
        assert_eq!(dup_res.groups.len(), 1);
        assert_eq!(dup_res.total_wasted_bytes, 8192);

        // Test with progress callback
        let mut progress_events = Vec::new();
        let dup_res2 = DuplicateFinderService::scan_directory_with_progress(
            dir.path(),
            1,
            |p| progress_events.push(p.clone()),
            None,
        );
        assert_eq!(dup_res2.groups.len(), 1);
        assert!(!progress_events.is_empty());
        assert!(progress_events.iter().any(|p| p.stage == 1));
        assert!(progress_events.iter().any(|p| p.percentage >= 99.0));

        // Test cancellation
        let cancel = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(true));
        let dup_res_cancelled = DuplicateFinderService::scan_directory_with_progress(
            dir.path(),
            1,
            |_| {},
            Some(cancel),
        );
        assert_eq!(dup_res_cancelled.groups.len(), 0);

        let tree = StorageTreemapService::scan_directory_tree(dir.path(), 4);
        assert_eq!(tree.size_bytes, 16384);
        let top = StorageTreemapService::get_top_largest_files(&tree, 10);
        assert_eq!(top.len(), 2);
        let report = StorageTreemapService::export_storage_report(&tree, true);
        assert!(report.contains("dup1.dat"));
    }

    #[test]
    fn github_update_service_formats_multiline_release_notes() {
        let raw = "## What's New in Modern Explorer v1.8.1\n- **Feature 1**: Added [Rust Edition](file:///rust/Cargo.toml)\n  - Sub item\n### Bugfixes\n* Scrollable multi-line release notes";
        let formatted = GitHubUpdateService::format_release_notes_for_display(raw);
        let lines: Vec<&str> = formatted.lines().collect();
        assert!(lines.len() >= 5);
        assert!(formatted.contains("━━ What's New in Modern Explorer v1.8.1 ━━"));
        assert!(formatted.contains("• Feature 1: Added Rust Edition"));
        assert!(formatted.contains("◦ Sub item"));
        assert!(formatted.contains("▸ Bugfixes"));
        assert!(!formatted.contains("file:///"));
        assert!(GitHubUpdateService::is_newer_version("v1.9.3", "1.9.2"));
        assert!(!GitHubUpdateService::is_newer_version("v1.9.2", "1.9.2"));

        let dummy_rel = GitHubReleaseInfo {
            tag_name: "v1.9.3".to_string(),
            name: "Modern Explorer Studio v1.9.3 (Rust Edition)".to_string(),
            body: String::new(),
            html_url: GitHubUpdateService::RELEASES_URL.to_string(),
            published_at: String::new(),
            assets: vec![
                GitHubAssetInfo {
                    name: "ModernExplorer-Rust-windows-x64-Portable.exe".to_string(),
                    browser_download_url: "https://example.com/portable.exe".to_string(),
                    size: 5000,
                },
                GitHubAssetInfo {
                    name: "ModernExplorer-Rust-Setup-win-x64.exe".to_string(),
                    browser_download_url: "https://example.com/setup.exe".to_string(),
                    size: 4000,
                },
            ],
        };
        let best = GitHubUpdateService::select_best_rust_asset(&dummy_rel);
        assert!(best.is_some());
        assert_eq!(
            best.unwrap().name,
            "ModernExplorer-Rust-windows-x64-Portable.exe"
        );
    }

    #[test]
    fn github_update_service_selects_portable_and_unpacks_zip() {
        let dir = TempDir::new().unwrap();
        let zip_path = dir.path().join("archive.zip");

        {
            let file = fs::File::create(&zip_path).unwrap();
            let mut zip = zip::ZipWriter::new(file);
            let options = zip::write::SimpleFileOptions::default();
            zip.start_file("ModernExplorer-Rust.exe", options).unwrap();
            use std::io::Write;
            zip.write_all(b"MZ_DUMMY_EXECUTABLE_CONTENT").unwrap();
            zip.finish().unwrap();
        }

        let out_dir = dir.path().join("out");
        fs::create_dir_all(&out_dir).unwrap();
        let extracted = GitHubUpdateService::unpack_binary_if_archive(&zip_path, &out_dir).unwrap();
        assert!(extracted.exists());
        let content = fs::read(&extracted).unwrap();
        assert_eq!(content, b"MZ_DUMMY_EXECUTABLE_CONTENT");
    }

    #[test]
    fn color_settings_support_all_10_targets_and_reset() {
        let mut settings = AppSettings::default();
        settings.set_color_hex(ColorSettingTarget::TreemapVideoMedia, "#123456".to_string());
        assert_eq!(
            settings.get_color_hex(ColorSettingTarget::TreemapVideoMedia),
            "#123456"
        );
        settings.reset_color_to_default(ColorSettingTarget::TreemapVideoMedia);
        assert_eq!(
            settings.get_color_hex(ColorSettingTarget::TreemapVideoMedia),
            ColorSettingTarget::TreemapVideoMedia.default_hex()
        );
    }

    #[test]
    fn archive_sync_export_basket_and_quick_runner_work() {
        use crate::archive::{ArchiveFormat, ArchiveService};
        let dir = TempDir::new().unwrap();
        let left_dir = dir.path().join("left");
        let right_dir = dir.path().join("right");
        fs::create_dir_all(&left_dir).unwrap();
        fs::create_dir_all(&right_dir).unwrap();

        let sample = left_dir.join("note.txt");
        fs::write(&sample, "Hello Rust Sync & Archive").unwrap();

        // Directory Sync
        let sync_items = DirectorySyncService::compare_directories(
            &left_dir,
            &right_dir,
            SyncComparisonMode::SizeAndTimestamp,
            SyncDirectionMode::LeftToRight,
            false,
        );
        let synced = DirectorySyncService::execute_sync(&left_dir, &right_dir, &sync_items).unwrap();
        assert_eq!(synced, 1);
        assert!(right_dir.join("note.txt").exists());

        // Archive Pack & List & Extract
        let zip_path = dir.path().join("bundle.zip");
        let packed = ArchiveService::pack_archive(
            &[sample.to_string_lossy().to_string()],
            &zip_path,
            ArchiveFormat::Zip,
        )
        .unwrap();
        assert_eq!(packed, 1);
        let entries = ArchiveService::list_entries(&zip_path).unwrap();
        assert_eq!(entries.len(), 1);

        let extract_dir = dir.path().join("extracted");
        ArchiveService::extract_all(&zip_path, &extract_dir).unwrap();
        assert!(extract_dir.join("note.txt").exists());

        // Smart Export & Basket
        let fe = FileEntry::try_from_path(&sample).unwrap();
        let md_links = SmartExportService::format_entries(
            &[fe],
            SmartExportFormat::MarkdownFileLinks,
            Some(&left_dir),
        );
        assert!(md_links.contains("[note.txt](file:///"));

        let mut basket = FileBasketService::default();
        assert_eq!(basket.add_paths(&[sample.to_string_lossy().to_string()]), 1);
        assert_eq!(basket.count(), 1);

        // QuickCommandRunner cd navigation
        let mut runner = QuickCommandRunnerService::new();
        let cd_res = runner.execute("cd left", dir.path());
        assert_eq!(cd_res.exit_code, 0);
        assert!(cd_res.navigate_to_directory.is_some());
    }

    #[test]
    fn virtual_archive_and_sync_jump_settings_work() {
        use crate::archive::{ArchiveFormat, ArchiveService};
        use crate::settings::AppSettings;

        // 1. Settings defaults and serialization
        let def = AppSettings::default();
        assert_eq!(def.archive_open_action, "archive_browser");
        assert!(def.sync_jump_to_other_pane_folder);

        let json_str = serde_json::to_string(&def).unwrap();
        let parsed: AppSettings = serde_json::from_str(&json_str).unwrap();
        assert_eq!(parsed.archive_open_action, "archive_browser");
        assert!(parsed.sync_jump_to_other_pane_folder);

        let from_empty: AppSettings = serde_json::from_str("{}").unwrap();
        assert_eq!(from_empty.archive_open_action, "archive_browser");
        assert!(from_empty.sync_jump_to_other_pane_folder);

        // 2. Virtual archive path helpers
        let root_vp = ArchiveService::build_virtual_archive_path("C:/data/test.zip", "");
        assert_eq!(root_vp, "archive://C:/data/test.zip");
        assert!(ArchiveService::is_virtual_archive_path(&root_vp));

        let sub_vp = ArchiveService::build_virtual_archive_path("C:/data/test.zip", "nested/sub");
        assert_eq!(sub_vp, "archive://C:/data/test.zip|nested/sub");

        let (arch, inner) = ArchiveService::parse_virtual_archive_path(&sub_vp).unwrap();
        assert_eq!(arch, "C:/data/test.zip");
        assert_eq!(inner, "nested/sub");

        let p1 = ArchiveService::get_parent_path_for_virtual_archive(&sub_vp);
        assert_eq!(p1, "archive://C:/data/test.zip|nested");

        let p2 = ArchiveService::get_parent_path_for_virtual_archive(&p1);
        assert_eq!(p2, "archive://C:/data/test.zip");

        let p3 = ArchiveService::get_parent_path_for_virtual_archive(&p2);
        assert_eq!(p3.replace('\\', "/"), "C:/data");

        // 3. Virtual archive listing and entry extraction
        let dir = TempDir::new().unwrap();
        let src_dir = dir.path().join("content");
        let sub_dir = src_dir.join("folder");
        fs::create_dir_all(&sub_dir).unwrap();
        fs::write(src_dir.join("hello.txt"), "Hello Virtual Archive!").unwrap();
        fs::write(sub_dir.join("inner.rs"), "fn main() {}").unwrap();

        let zip_path = dir.path().join("sample.zip");
        let packed = ArchiveService::pack_archive(
            &[
                src_dir.join("hello.txt").to_string_lossy().to_string(),
                sub_dir.to_string_lossy().to_string(),
            ],
            &zip_path,
            ArchiveFormat::Zip,
        )
        .unwrap();
        assert!(packed >= 2);

        let root_virt = ArchiveService::build_virtual_archive_path(&zip_path.to_string_lossy(), "");
        let root_entries = ArchiveService::list_virtual_archive_entries(&root_virt).unwrap();
        assert!(root_entries.iter().any(|e| e.name == "hello.txt" && !e.is_directory));
        assert!(root_entries.iter().any(|e| e.name == "folder" && e.is_directory));

        let sub_virt = ArchiveService::build_virtual_archive_path(&zip_path.to_string_lossy(), "folder");
        let sub_entries = ArchiveService::list_virtual_archive_entries(&sub_virt).unwrap();
        assert!(sub_entries.iter().any(|e| e.name == "inner.rs" && !e.is_directory));

        let extracted = ArchiveService::extract_single_entry_to_temp(&zip_path.to_string_lossy(), "hello.txt").unwrap();
        assert!(extracted.is_file());
        let text = fs::read_to_string(&extracted).unwrap();
        assert_eq!(text, "Hello Virtual Archive!");
    }
}

