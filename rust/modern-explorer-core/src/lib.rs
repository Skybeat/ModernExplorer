pub mod app_paths;
pub mod archive;
pub mod cloud_backup;
pub mod features;
pub mod filesystem;
pub mod localization;
pub mod operations;
pub mod power_tools_v2;
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
    use crate::power_tools_v2::*;
    use crate::remote::*;
    use crate::settings::*;
    use crate::treemap::*;
    use crate::update::*;
    use crate::viewer::*;
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
    fn file_diff_service_handles_pdf_and_binary_comparisons() {
        let dir = TempDir::new().unwrap();
        let bin_a = dir.path().join("a.bin");
        let bin_b = dir.path().join("b.bin");
        fs::write(&bin_a, b"\x00\x01\x02\x03hello").unwrap();
        fs::write(&bin_b, b"\x00\x01\x02\x04hello").unwrap();

        let diff_bin = FileDiffService::compare_files(&bin_a, &bin_b, false).unwrap();
        assert!(diff_bin.rows.len() > 0);
        assert!(diff_bin.modified_count > 0 || diff_bin.hunk_count > 0);

        let mut doc = lopdf::Document::with_version("1.5");
        let pages_id = doc.new_object_id();
        let mut page_dict = lopdf::Dictionary::new();
        page_dict.set("Type", "Page");
        page_dict.set("Parent", pages_id);
        let page_id = doc.add_object(lopdf::Object::Dictionary(page_dict));

        let mut pages_dict = lopdf::Dictionary::new();
        pages_dict.set("Type", "Pages");
        pages_dict.set("Kids", vec![page_id.into()]);
        pages_dict.set("Count", 1);
        doc.objects.insert(pages_id, lopdf::Object::Dictionary(pages_dict));

        let mut catalog_dict = lopdf::Dictionary::new();
        catalog_dict.set("Type", "Catalog");
        catalog_dict.set("Pages", pages_id);
        let catalog_id = doc.add_object(lopdf::Object::Dictionary(catalog_dict));
        doc.trailer.set("Root", catalog_id);

        let mut pdf_bytes = Vec::new();
        doc.save_to(&mut pdf_bytes).unwrap();

        let pdf_path = dir.path().join("doc.pdf");
        fs::write(&pdf_path, &pdf_bytes).unwrap();
        let extracted = FileDiffService::read_file_for_diff(&pdf_path).unwrap();
        assert!(extracted.contains("PDF"));
        assert!(extracted.contains("1.5"));
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

        let rects_files_only = StorageTreemapService::build_squarified_treemap_layout(&tree, 800.0, 600.0, 2, 100, 4.0, false);
        assert_eq!(rects_files_only.len(), 2);
        assert!(!rects_files_only[0].is_group_container);
        assert!(!rects_files_only[1].is_group_container);

        let rects_with_folders = StorageTreemapService::build_squarified_treemap_layout(&tree, 800.0, 600.0, 2, 100, 4.0, true);
        assert!(!rects_with_folders.is_empty());
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
    fn replace_binary_in_place_works_in_writable_directory() {
        let dir = TempDir::new().unwrap();
        let target_exe = dir.path().join("ModernExplorer-Rust.exe");
        let new_binary = dir.path().join("new_binary.exe");
        let update_dir = dir.path().join("update_staging");
        fs::create_dir_all(&update_dir).unwrap();

        fs::write(&target_exe, b"OLD_BINARY_V1").unwrap();
        fs::write(&new_binary, b"NEW_BINARY_V2").unwrap();

        let directly_replaced = GitHubUpdateService::replace_binary_in_place(
            &new_binary,
            &target_exe,
            &update_dir,
        ).unwrap();

        assert!(directly_replaced);
        assert_eq!(fs::read(&target_exe).unwrap(), b"NEW_BINARY_V2");
        let old_backup = target_exe.with_extension("old.exe");
        assert!(old_backup.exists());
        assert_eq!(fs::read(&old_backup).unwrap(), b"OLD_BINARY_V1");
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

    #[test]
    fn extended_archive_formats_7z_zst_xz_roundtrip() {
        use crate::archive::{ArchiveFormat, ArchiveService};
        let dir = TempDir::new().unwrap();
        let src_file = dir.path().join("readme.txt");
        fs::write(&src_file, "Hello 7z, zst, xz archives in Rust!").unwrap();
        let src_list = vec![src_file.to_string_lossy().to_string()];

        for (ext, fmt) in [
            ("bundle.7z", ArchiveFormat::SevenZ),
            ("bundle.tar.zst", ArchiveFormat::TarZst),
            ("bundle.tar.xz", ArchiveFormat::TarXz),
        ] {
            let arch_path = dir.path().join(ext);
            let packed = ArchiveService::pack_archive(&src_list, &arch_path, fmt).unwrap();
            assert_eq!(packed, 1);
            assert!(ArchiveService::is_supported_archive(&arch_path));

            let listed = ArchiveService::list_entries(&arch_path).unwrap();
            assert_eq!(listed.len(), 1);
            assert_eq!(listed[0].name, "readme.txt");

            let out_dir = dir.path().join(format!("out_{}", ext));
            ArchiveService::extract_all(&arch_path, &out_dir).unwrap();
            assert_eq!(
                fs::read_to_string(out_dir.join("readme.txt")).unwrap(),
                "Hello 7z, zst, xz archives in Rust!"
            );
        }
    }

    #[test]
    fn vault_encryption_hex_editor_data_converter_and_image_converter_work() {
        let dir = TempDir::new().unwrap();

        // 1. AES-256-GCM File Vault
        let plain_path = dir.path().join("secret.txt");
        let menc_path = dir.path().join("secret.txt.menc");
        let dec_path = dir.path().join("secret_restored.txt");
        fs::write(&plain_path, "Top Secret Payload 2026").unwrap();

        FileVaultService::encrypt_file(&plain_path, &menc_path, "CorrectHorseBatteryStaple", None)
            .unwrap();
        assert!(menc_path.exists());
        assert!(FileVaultService::decrypt_file(&menc_path, &dec_path, "WrongPass", None).is_err());
        FileVaultService::decrypt_file(&menc_path, &dec_path, "CorrectHorseBatteryStaple", None)
            .unwrap();
        assert_eq!(
            fs::read_to_string(&dec_path).unwrap(),
            "Top Secret Payload 2026"
        );

        // 2. Hex Editor Read, Search & Patch
        let bin_path = dir.path().join("sample.bin");
        fs::write(&bin_path, b"ABCD1234EFGH").unwrap();
        let hits = FileContentService::search_binary(&bin_path, "31 32 33 34", true, 10).unwrap();
        assert_eq!(hits, vec![4]);
        FileContentService::apply_hex_patches(&bin_path, &[(4, b'9'), (5, b'9')]).unwrap();
        let page = FileContentService::read_hex_editor_page(&bin_path, 0, 32).unwrap();
        assert_eq!(&page.bytes, b"ABCD9934EFGH");

        // 3. Structured Data Converter (JSON <-> YAML <-> TOML <-> CSV)
        let json_in = r#"[{"name":"alpha","port":"8080"},{"name":"beta","port":"9090"}]"#;
        let yaml_out =
            DataConverterService::convert_structured_data(json_in, "json", "yaml").unwrap();
        assert!(yaml_out.contains("alpha"));
        let csv_out =
            DataConverterService::convert_structured_data(&yaml_out, "yaml", "csv").unwrap();
        assert!(csv_out.contains("name,port"));
        let back_json =
            DataConverterService::convert_structured_data(&csv_out, "csv", "json").unwrap();
        assert!(back_json.contains("9090"));

        // 4. Image Converter & Optimizer
        let img_in = dir.path().join("src.png");
        let mut img_buf = image::RgbaImage::new(40, 20);
        for px in img_buf.pixels_mut() {
            *px = image::Rgba([20, 140, 240, 255]);
        }
        img_buf.save(&img_in).unwrap();

        let opts = ImageConvertOptions {
            target_format: "webp".to_string(),
            scale_percent: Some(50),
            max_width: None,
            max_height: None,
            rotate_degrees: 90,
            flip_horizontal: false,
            flip_vertical: false,
            grayscale: false,
            watermark_banner: true,
            output_dir: dir.path().to_string_lossy().to_string(),
            filename_suffix: "_opt".to_string(),
        };
        let conv = ImageConverterService::convert_batch(
            &[img_in.to_string_lossy().to_string()],
            &opts,
        );
        assert_eq!(conv.len(), 1);
        assert!(conv[0].ok);
        assert_eq!(conv[0].dimensions, "10x20");

        // 5. QR SVG, Selection Engine, Frecency & WebDAV/S3 XML parsers
        let qr_svg = LocalShareServerState::generate_qr_svg("http://192.168.1.10:8787/");
        assert!(qr_svg.starts_with("<svg"));

        let mut frec = FrecencyStore::default();
        frec.entries.push(FrecencyEntry {
            path: "I:\\AI_Projects\\ModernExplorer\\rust".to_string(),
            visit_count: 10,
            last_visited_unix: 1_900_000_000,
        });
        let jump_hits = frec.query_smart_jump("mod rust", 5);
        assert_eq!(jump_hits.len(), 1);

        let s3_xml = "<ListBucketResult><CommonPrefixes><Prefix>backups/</Prefix></CommonPrefixes><Contents><Key>report.pdf</Key><Size>4096</Size><LastModified>2026-10-17</LastModified></Contents></ListBucketResult>";
        let s3_items = RemoteFileSystemService::parse_s3_list_xml(s3_xml);
        assert_eq!(s3_items.len(), 2);
    }
}


