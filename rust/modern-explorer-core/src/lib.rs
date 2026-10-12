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
}

