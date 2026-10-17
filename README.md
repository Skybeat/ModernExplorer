# ModernExplorer Studio (Rust Edition)

A fast, lightweight, keyboard-centric dual-pane file manager for **Windows**, **Linux**, and **macOS** built natively in **Rust** (`modern-explorer-core` & `modern-explorer-app`) and **Tauri v2**.

---

## Supported Operating Systems, Installers & Portable Releases

Every GitHub Release automatically builds and publishes both **native installers** and **standalone portable packages** for **Windows**, **Linux**, and **macOS** across `x64` and `ARM64` CPU architectures:

| Operating System | Architecture | Native Installer(s) | Portable Package(s) | Runtime / Stack |
| :--- | :--- | :--- | :--- | :--- |
| **Windows 10 / 11** | `x64` | `ModernExplorer-Rust-Setup-win-x64.exe` | `ModernExplorer-Rust-windows-x64-Portable.exe`<br>`ModernExplorer-Rust-windows-x64-Portable.zip` | Rust + Tauri v2 (WebView2) |
| **Windows 10 / 11** | `ARM64` | `ModernExplorer-Rust-Setup-win-arm64.exe` | `ModernExplorer-Rust-windows-arm64-Portable.exe`<br>`ModernExplorer-Rust-windows-arm64-Portable.zip` | Rust + Tauri v2 (WebView2) |
| **Linux** | `x64` | `ModernExplorer-Rust-Setup-linux-x64.deb`<br>`ModernExplorer-Rust-Setup-linux-x64.rpm`<br>`ModernExplorer-Rust-Setup-linux-x64.pkg.tar.zst`<br>`ModernExplorer-Rust-linux-x64.AppImage`<br>`ModernExplorer-Rust-Installer-linux-x64.sh` | `ModernExplorer-Rust-linux-x64-Portable.tar.gz`<br>`ModernExplorer-Rust-linux-x64-Portable.zip` | Rust + Tauri v2 (GTK3 / WebKit2GTK) |
| **Linux** | `ARM64` | `ModernExplorer-Rust-Setup-linux-arm64.deb`<br>`ModernExplorer-Rust-Setup-linux-arm64.rpm`<br>`ModernExplorer-Rust-Setup-linux-arm64.pkg.tar.zst`<br>`ModernExplorer-Rust-linux-arm64.AppImage`<br>`ModernExplorer-Rust-Installer-linux-arm64.sh` | `ModernExplorer-Rust-linux-arm64-Portable.tar.gz`<br>`ModernExplorer-Rust-linux-arm64-Portable.zip` | Rust + Tauri v2 (GTK3 / WebKit2GTK) |
| **macOS** | `x64` (Intel) | `ModernExplorer-Rust-Setup-macos-x64.dmg`<br>`ModernExplorer-Rust-Setup-macos-x64.pkg` | `ModernExplorer-Rust-macos-x64-Portable.zip`<br>`ModernExplorer-Rust-macos-x64-Portable.tar.gz` | Rust + Tauri v2 (WKWebView) |
| **macOS** | `ARM64` (Apple Silicon) | `ModernExplorer-Rust-Setup-macos-arm64.dmg`<br>`ModernExplorer-Rust-Setup-macos-arm64.pkg` | `ModernExplorer-Rust-macos-arm64-Portable.zip`<br>`ModernExplorer-Rust-macos-arm64-Portable.tar.gz` | Rust + Tauri v2 (WKWebView) |

---

## Feature Overview

### 1. Dual-Pane & Multi-Tab File Management
- **Versioned Title Bar**: Displays the current application version directly in the main title bar (`ModernExplorer Studio — Dual-Pane File Manager v1.9.6 (Rust Edition)`).
- **Dual-Pane & Single-Pane Mode (`F12` / `Tab`)**: Instant switching between left and right file panels with a customizable active-panel border color and thickness.
- **Smart Dual-Pane Folder Sync Jump**: Configurable option (`sync_jump_to_other_pane_folder`) that automatically switches to the opposite pane when opening a folder that is already open in a tab of the other pane.
- **Per-Pane Drive Bar (Single-Line or Multi-Line)**: Displays all mounted drives (`Fixed`, `Removable`, `Network`, `CD-ROM`, `RAM`) with live free-space and capacity bars, tooltips, and optional automatic multi-line wrapping (`multiline_drive_bar`).
- **Full Multi-Tab Support (`Ctrl+T`, `Ctrl+W`, `Ctrl+Tab`)**:
  - Open unlimited tabs per pane, duplicate tabs, or move tabs between panes.
  - **Tab States**: Normal, **Pinned** (`📌`), **Locked** (`🔒`, opens navigated folders in a new tab), and **Locked with Directory Changes Allowed** (`🔐`, snaps back to its home folder).
- **8 Explorer View Modes**:
  - **Details** (sortable columns: Name, Size, Modified, Extension, plus inline Git & Size bars), **List**, **Tiles**, **Small Icons**, **Medium Icons**, **Large Icons**, **Extra Large Icons**, and **Content**.
- **Windows Explorer & Total Commander Style Type-Ahead Quick Search**:
  - Typing characters directly in an active file pane immediately focuses, selects, and scrolls to matching folders or files.
  - **Single-Character Cycling**: Repeatedly pressing the same single letter (e.g., `D`, `D`, `D`) cycles through all items starting with that letter.
  - **Multi-Character Prefix & Substring Search**: Typing multiple characters narrows the match (prioritizing prefix matches with automatic substring fallback) and displays a floating quick-search indicator pill (`⚡ Suche: <query> (x/y)`), with `Backspace` to edit the query and `Escape` to clear.
- **Breadcrumb & Editable Address Bar (`Ctrl+L` / `Alt+D`)**: Clickable path segments for instant parent navigation, autocomplete suggestions, or direct path/URI entry.
- **Live Instant Filter (`Ctrl+F`)**: Filter the current directory view in real time with wildcards, substring matching, `ext:` queries, or color tag / note filters (`tag:rot`, `tag:green`, `tag:*`, `note:invoice`).

### 2. Integrated Power Tools (Freely Resizable Windows)
All feature dialogs open as freely resizable, draggable, and maximizable child windows that remember their exact dimensions across sessions (`dialog_sizes` in `settings.json`):

| Feature | Shortcut / Button | Description |
| :--- | :--- | :--- |
| **Multi-Rename Tool (Batch Rename)** | `Ctrl+M` / `✏ Batch Rename` | Rename multiple files and folders at once using placeholders (`[N]` name, `[N1-5]` substring, `[E]` extension, `[C]` counter with custom start/step/digits, `[Y][M][D][h][m][s]` timestamps, `[P]` parent folder, plus image/EXIF placeholders `[Width]`, `[Height]`, `[Resolution]`, `[ExifDate]`, `[Camera]`), Search & Replace, Regular Expressions, case transformations, live conflict detection, and one-click Undo. |
| **Visual Side-by-Side File Diff Viewer** | `Ctrl+Shift+U` / `⚖ Diff` | Compare two files line by line side-by-side with LCS alignment, color highlights (**Added**, **Removed**, **Modified**), hunk navigation (`⬆ Prev` / `⬇ Next`), interactive hunk merging (`→` Left to Right / `←` Right to Left), and direct saving. |
| **Checksum Generator & Verifier** | `Ctrl+Shift+K` / `🔐 Hashes` | Calculate `SHA-256`, `SHA-512`, `MD5`, and `CRC32` for selected files, instantly compare against a pasted clipboard hash, export `.sha256`, `.sha512`, `.md5`, or `.sfv` manifests, and verify existing checksum manifest files. |
| **File Splitter, Joiner & Secure Shredder** | `Ctrl+Shift+X` / `✂ Split/Shred` | Split large files into `.001`, `.002`… parts with an automatic `.sha256` manifest, rejoin `.001` parts seamlessly, or permanently shred sensitive files with **1-Pass**, **3-Pass DoD**, or **7-Pass** cryptographic overwriting. |
| **Color File Tags / Labels & Notes** | `Ctrl+Shift+L` / `🏷 Tags` | Assign persistent color tags (**Red**, **Orange**, **Yellow**, **Green**, **Blue**, **Purple**) and text notes to any file or folder, displayed directly as badges in the file list and filterable via `tag:red`, `tag:rot`, `tag:*`, or `note:...`. |
| **Workspace Profiles (Saved Layouts)** | `Ctrl+Shift+O` / `🗂 Workspaces` | Save and restore complete dual-pane tab layouts (including pinned/locked tabs, view modes, and split ratio) under custom profile names with a single click. |
| **Smart Copy / Path & List Exporter** | `Ctrl+Shift+E` / `📋 Export` | Copy or export selected file lists as **Full Paths**, **File Names**, **Relative Paths**, **Forward-Slash Paths**, **Markdown `file://` Links**, **CSV Table**, **Markdown Table**, **JSON Array**, or **HTML Table**. |
| **Virtual File Basket (Drop-Basket)** | `Ctrl+Shift+B` / `🧺 Basket` | Collect files and folders from arbitrary directories and drives into a virtual staging basket, then execute unified batch actions (**Copy Here**, **Move Here**, **Pack into Archive**, **Batch Rename**, or **Export List**). |
| **EXIF / Media Metadata & Timestamp/Attribute Editor** | `Ctrl+Shift+A` / `🕒 Attr/EXIF` | Inspect binary PNG/JPEG/GIF/BMP/WEBP image dimensions and EXIF camera data (`Width`, `Height`, `DateTaken`, `CameraMake`, `CameraModel`, `ISO`), batch-edit timestamps (`Created`, `Modified`, `Accessed`, `+/- hours` shift, copy EXIF `DateTaken` to file timestamps, `Touch`), and batch-toggle file attributes (`ReadOnly`, `Hidden`, `Archive`, `System`). |
| **Embedded Quick Command Bar (`>_`)** | `Ctrl+Shift+J` / `>_ Console` | Docked PowerShell/CMD/sh command bar at the bottom of the main window executing directly in the active folder with `cd` folder navigation, `Up`/`Down` command history, and a collapsible live output drawer. |
| **Extended Git Panel** | `Ctrl+Shift+G` / `⎇ Git` | Clickable Git branch badge and toolbar panel showing local branches with one-click `git checkout`, modified/untracked working tree status with `git add` staging, direct **Visual Side-by-Side Diff against `HEAD`**, and recent commit history (`git log`). |
| **Directory Compare & Synchronize** | `Ctrl+Shift+S` / `⇄ Sync` | Compare left and right directories recursively by **Size & Timestamp**, **Size Only**, or **SHA-256 Content**. Supports **Bidirectional Merge (newer wins)**, **Left → Right Mirror**, and **Right → Left Mirror** with optional orphan deletion and live progress feedback. |
| **Recursive Deep Search** | `Alt+F7` / `🔍 Deep Search` | High-speed recursive file search by wildcard (`*.rs;*.toml;*.json`), Regular Expression, file size, date range, attributes, and **full-text content search** inside files. Results can be jumped to directly in the active pane. |
| **Duplicate File Finder** | `Ctrl+Shift+D` / `🧹 Duplicates` | 3-stage duplicate scanner (**File Size grouping → 4 KB Partial Hash → Full SHA-256 Hash**) with live progress and wasted disk space calculation, plus one-click selection of all duplicates (preserving the original per group). |
| **Graphical Disk Space Analyzer & Treemap (WinDirStat / WizTree)** | `Ctrl+Shift+W` / `🗺 Treemap` | Interactive **Squarified Treemap** visualizer in a freely resizable analyzer window with **interactive vertical & horizontal drag splitters**, an **integrated `🌳 Explorer Tree ▾` dropdown**, **toggleable `🔥 Top 100 Largest Files` view**, **live Color Mode switcher (`🎨 Categories` vs. `⏳ File Age Heatmap`)**, **Markdown/CSV Storage Report export (`📄 Report`)**, **7 customizable category colors**, **Drill-Down zooming**, and direct deletion via the **Right-Click Context Menu (`🗑 Delete [Del]`)** or **`Delete` (`Entf`) key** with instant in-place Treemap recalculation. |
| **Archive Browser, Packer & Virtual Folder Navigation** | `Alt+F5` (`📦 Archiv`) | Configurable archive double-click behavior (`archive_open_action`): either open archives in the dedicated **Archive Browser** modal or browse inside archives (`.zip`, `.7z`, `.tar.gz`, `.tgz`, `.tar.zst`, `.tzst`, `.tar.xz`, `.txz`, `.rar`, `.jar`, `.nupkg`) directly inside the Explorer panes like normal folders (`archive://`), extract entries, and pack selected files/folders into `.zip`, `.7z`, `.tar.gz`, `.tar.zst`, or `.tar.xz`. |
| **Cloud & Remote Storage (SFTP / FTPS / FTP / WebDAV / S3)** | `Ctrl+Shift+C` / `🌐 Cloud/SFTP` | Connect to remote servers and cloud storage via **SFTP (SSH)**, **FTPS (TLS)**, **FTP**, **WebDAV** (Nextcloud/ownCloud), and **S3-compatible object storage** (AWS S3, MinIO, Cloudflare R2 with native AWS SigV4 signing). |
| **Rule-Based Selection Engine & Presets** | `Num +` / `Num -` / `Num *` | Select or deselect items by wildcard mask, regular expression, minimum/maximum byte size, or maximum file age (days), invert selection (`Num *`), and save reusable filter presets. |
| **Background Transfer Queue & Speed Limiter** | `Ctrl+Shift+Q` / `⚡ Queue` | Enqueue sequential Copy/Move jobs with pause/resume/cancel controls, conflict resolution policies (`Overwrite`, `SkipExisting`, `AutoRename`), and configurable bandwidth throttling (MB/s). |
| **Collapsible Directory Tree Sidebar** | `Alt+F10` / `🌲 Baum` | Toggleable hierarchical directory tree sidebar for rapid navigation across deep folder structures. |
| **Smart Jump (Frecency & Bookmark Launcher)** | `Ctrl+G` / `Alt+Down` / `🚀 Jump` | `zoxide`-style fuzzy launcher ranking visited directories by frequency and recency (frecency). |
| **Synchronized Navigation & Scroll-Lock (`Sync-Lock`)** | `Ctrl+Shift+Y` / `🔗 Sync-Lock` | Locks both panes together so navigating into a subfolder in one pane automatically enters the matching subfolder in the opposite pane and synchronizes vertical scrolling. |
| **Batch Image Converter & Optimizer** | `Ctrl+Shift+I` / `🖼 Bild-Tool` | Batch convert, resize, rotate (`90°`/`180°`/`270°`), and grayscale images across `PNG`, `JPEG`, `WEBP`, `BMP`, and `ICO`. |
| **Integrated PDF Toolkit** | `Ctrl+Alt+P` / `📄 PDF` | Inspect PDF metadata/page counts, merge multiple selected PDF files into a single document, or split a PDF into individual single-page PDFs. |
| **Cryptographic File Vault (`AES-256-GCM`)** | `Ctrl+Shift+V` / `🔒 Tresor` | Encrypt and decrypt sensitive files into authenticated `.vault` containers using **AES-256-GCM** and **PBKDF2-HMAC-SHA256** (100,000 iterations). |
| **NTFS Streams, ACL Permissions & File Locksmith** | `Ctrl+Alt+L` / `🔓 NTFS/Locks` | Inspect NTFS Alternate Data Streams (`ADS`) with one-click `Zone.Identifier` unblocking, view `icacls` ACL permissions, and detect processes locking a file. |
| **Interactive Binary Hex-Editor & Patcher** | `F3` / `F4` (`🛠 Hex-Editor`) | Inspect and edit individual bytes at any hex/decimal offset, search for binary hex patterns or ASCII strings, and patch bytes directly in-place. |
| **Structured Data Converter & Clipboard History** | `Ctrl+Alt+D` / `🔄 Daten/Clip` | Convert and pretty-print structured data between **JSON ↔ YAML ↔ TOML ↔ CSV** and browse/reuse a persistent multi-step clipboard history. |
| **Custom User Commands & Script Bar** | `Ctrl+Alt+U` / `⚡ Skripte` | Create custom one-click toolbar buttons with placeholders (`{file}`, `{dir}`, `{other_dir}`, `{selected}`) to launch external tools or scripts. |
| **Ad-hoc Local HTTP Share Server with QR-Code** | `Ctrl+Alt+S` / `📡 LAN-Share` | Instantly share any folder on your local Wi-Fi/LAN via an embedded HTTP server with an automatically generated SVG QR-Code for mobile devices. |
| **Symlink, Hardlink & Directory Junction Manager** | `Ctrl+Alt+K` / `⛓ Links` | Create Symbolic Links, Hardlinks, or Directory Junctions between panes and inspect link targets (`⛓ → target`) inline in the file list. |
| **Live Filesystem Watcher (`notify`)** | Automatic | Automatically refreshes open panes in the background when external applications create, modify, rename, or delete files. |
| **Branch View (Flat View)** | `Ctrl+B` / `🌿` | Flattens the current directory and all subdirectories into a single unified file list showing relative subpaths. |
| **TreeSize Disk Space Analyzer** | `📊` | Recursively calculates folder sizes in the background and renders proportional visual allocation bars directly inside the `Size` column. |
| **Minimizable & Resizable Right Inspector Dock** | `Ctrl+Q` / `👁 Preview` | Toggleable, resizable (`210px–640px`), and minimizable right dock displaying live previews for the currently selected file (images, Markdown/code/text, CSV/JSON/XML, media, and SHA-256/MD5/CRC32 file metadata) as you navigate with the arrow keys. |
| **Favorites / Bookmarks Bar & Undo** | `Ctrl+D` (`☆`) / `Ctrl+Z` (`↶ Undo`) | Pin frequently used folders to the Favorites bar for one-click navigation and undo recent file operations (rename, batch rename, copy, move, new folder/file) via `Ctrl+Z`. |

### 3. Intelligent Quick Viewer (`F3`), Internal Editor (`F4`) & Binary Hex-Editor
- **Quick Viewer (`F3`) & Hex-Editor**:
  - **Images**: PNG, JPG, GIF, WEBP, BMP, ICO, SVG, TIFF with zoom and pixel dimensions, plus optional hover preview cards in the file list (`enable_image_hover_preview`).
  - **Documents**: PDF and Office/OpenXML package inspection.
  - **Audio & Video**: Built-in media playback.
  - **Structured Data**: Formatted JSON/XML viewer and tabular CSV/TSV preview.
  - **Interactive Binary Hex-Editor & Patcher**: Edit individual bytes, jump to offsets, search hex/ASCII sequences, and apply binary patches in-place.
- **Internal Editor (`F4`)**:
  - Edit text, configuration, and source code files directly in a resizable child window with line/character counters and `Ctrl+S` save support, or configure an external editor in Settings.

### 4. Background Operations Queue & Conflict Resolution
- Background operations for Copy (`F5`), Move (`F6`), Delete (`F8` / `Delete`), and queued transfers (`Ctrl+Shift+Q`) with real-time throughput, bandwidth limiting, pause/resume, and conflict handling (`Overwrite`, `Skip`, `Keep Both / Auto-Rename`).

### 5. Multi-Language Support & Configurable Feature Tooltips
- **14 Built-in Languages**: Automatically detects the OS language (`system`) or lets you switch live without restart between **Deutsch**, **English**, **Español**, **Français**, **Italiano**, **Nederlands**, **Polski**, **Português**, **Русский**, **Türkçe**, **Українська**, **العربية**, **日本語**, and **中文**.
- **Complete Localization Across All Windows**: Main Window, Menus, Toolbars, Column Headers, Right Inspector Dock, Settings, Storage Analyzer (`Treemap`), Quick Viewer (`F3`), Editor (`F4`), Context Menus, and all Power Tool dialogs dynamically update in the selected UI language.
- **Localized Feature Tooltips**: Toolbar buttons, feature buttons, panel controls, and function keys (`F1`–`F12`) provide localized explanations that can be toggled at any time in **Settings → Appearance & Design → Tooltips**.

### 6. Live Customization, Interactive Color Picker & In-Place Self-Updater
- **Appearance, Interactive Color Picker, Treemap Colors & Font Scaling**: Switch live between Dark, Light, and System themes, customize all **10 color settings** (**Active Panel Border**, **Selection Border**, **Marked Item Text**, and all **7 Storage Analyzer Treemap category colors**) using quick presets, `#RRGGBB` hex codes, or the interactive color picker with one-click default reset. Adjust font sizes individually per UI area and per Child Window or via a single Global Font Size setting.
- **Native Self-Replace In-Place Updater (`GitHubUpdateService`)**: Checks GitHub Releases automatically on startup (or manually in the Update dialog), downloads the matching portable release package for the current OS and CPU architecture, replaces the executable directly in-place (with automatic UAC elevation fallback on Windows when installed under `C:\Program Files`), and restarts the application automatically without requiring an external installer wizard.

---

## Keyboard Shortcuts Reference

| Shortcut | Action |
| :--- | :--- |
| `F1` | Show Keyboard Shortcuts Reference |
| `F2` | Inline Rename (single item) or Batch Rename (`Ctrl+M`) |
| `F3` | Open Quick Viewer & Interactive Hex-Editor |
| `F4` | Open Internal Text & Hex Editor |
| `F5` | Copy selected items to opposite pane |
| `F6` | Move selected items to opposite pane |
| `F7` / `Shift+F7` | New File (`F7`) / New Folder (`Shift+F7`) |
| `F8` / `Delete` | Delete selected items |
| `F9` | Open Terminal in current directory |
| `F10` / `Ctrl+Shift+P` | Open Command Palette |
| `Alt+F10` | Toggle Collapsible Directory Tree Sidebar |
| `F12` | Toggle Dual-Pane / Single-Pane layout |
| `Tab` | Switch active pane (Left ↔ Right) |
| `Num +` / `Num -` / `Num *` | Rule-based Select (`Num +`), Deselect (`Num -`), Invert Selection (`Num *`) |
| `Ctrl+G` / `Alt+Down` | Open Smart Jump (Frecency & Bookmark Launcher) |
| `Ctrl+Shift+Y` | Toggle Synchronized Navigation & Scroll-Lock (`Sync-Lock`) |
| `Ctrl+Shift+Q` | Open Background Transfer Queue & Job Manager |
| `Ctrl+Shift+I` | Open Batch Image Converter & Optimizer |
| `Ctrl+Alt+P` | Open Integrated PDF Toolkit (Merge / Split / Info) |
| `Ctrl+Shift+V` | Open Cryptographic File Vault (`AES-256-GCM`) |
| `Ctrl+Alt+L` | Open NTFS Streams, ACL Permissions & File Locksmith |
| `Ctrl+Alt+D` | Open Structured Data Converter (`JSON/YAML/TOML/CSV`) & Clipboard History |
| `Ctrl+Alt+U` | Open Custom User Commands & Script Manager |
| `Ctrl+Alt+S` | Open Ad-hoc Local HTTP Share Server (QR-Code) |
| `Ctrl+Alt+K` | Open Symlink, Hardlink & Directory Junction Manager |
| `A–Z` / `0–9` | Type-Ahead Quick Search (jump to folder/file, repeat single key to cycle matches) |
| `Backspace` | Edit Type-Ahead Quick Search query (or navigate to parent directory when not searching) |
| `Space` / `Insert` | Mark / unmark item (`Insert` also advances selection down) |
| `Ctrl+M` | Open Multi-Rename Tool (Batch Rename) |
| `Ctrl+Shift+U` | Open Visual Side-by-Side File Diff Viewer |
| `Ctrl+Shift+K` | Open Checksum Generator & Verifier (`SHA-256`, `SHA-512`, `MD5`, `CRC32`) |
| `Ctrl+Shift+X` | Open File Splitter, Joiner & Secure Shredder |
| `Ctrl+Shift+L` | Assign Color Tags / Labels & Notes (`tag:rot`, `note:...`) |
| `Ctrl+Shift+O` | Open Workspace Profiles Manager |
| `Ctrl+Shift+E` | Open Smart Copy / Path & Table Exporter |
| `Ctrl+Shift+B` | Open Virtual File Basket (Drop-Basket) |
| `Ctrl+Shift+A` | Open EXIF / Media Metadata & Timestamp / Attribute Editor |
| `Ctrl+Shift+J` | Toggle / Focus Embedded Quick Command Bar (`>_`) |
| `Ctrl+Shift+G` | Open Extended Git Panel (Branches, Status, Diff vs `HEAD`, Commit Log) |
| `Ctrl+Shift+S` | Open Directory Compare & Synchronize |
| `Ctrl+Shift+W` | Open Graphical Disk Space Analyzer & Squarified Treemap |
| `Ctrl+Shift+D` | Open Duplicate File Finder |
| `Ctrl+Shift+C` | Open Cloud & Remote Client (`SFTP`, `FTPS`, `FTP`, `WebDAV`, `S3`) |
| `Alt+F7` | Open Recursive Deep Search |
| `Alt+F5` | Open Archive Browser & Packer (`.zip`, `.7z`, `.tar.gz`, `.tar.zst`, `.tar.xz`, `.rar`) |
| `Ctrl+B` | Toggle Branch View (Flat View) |
| `Ctrl+Q` | Toggle Right Inspector & Preview Dock |
| `Ctrl+D` | Add / Remove current folder in Favorites Bar |
| `Ctrl+Z` | Undo last file operation |

---

## Building from Source & Publishing Releases

### Local Development & Release Build (Rust + Tauri v2)
```powershell
# Run core unit tests
cargo test --manifest-path rust/Cargo.toml

# Build optimized release binary
cargo build --manifest-path rust/Cargo.toml --release
```

### Automated Multi-OS GitHub Releases
Pushing a semantic version tag (`v*`, e.g. `v1.9.6`) or running `scripts/publish-releases.ps1 -Version "1.9.6"` triggers the GitHub Actions workflow (`.github/workflows/release.yml`), which compiles and publishes all Rust portable packages and native installers for Windows (`x64` & `ARM64`), Linux (`x64` & `ARM64`), and macOS (`x64` & `ARM64`).
