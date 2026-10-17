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
| **Archive Browser, Packer & Virtual Folder Navigation** | `Alt+F5` (`📦 Pack`) | Configurable archive double-click behavior (`archive_open_action`): either open archives in the dedicated **Archive Browser** modal or browse inside archives (`.zip`, `.tar.gz`, `.tgz`, `.jar`, `.nupkg`) directly inside the Explorer panes like normal folders (`archive://`), extract individual or all entries, and pack selected files/folders into `.zip` or `.tar.gz` archives. |
| **Built-in FTP / FTPS / SFTP Client** | `Ctrl+Shift+C` / `🌐 FTP/SFTP` | Connect to remote servers via **SFTP (SSH)**, **FTPS (Explicit TLS)**, or **FTP** with a built-in Connection Manager, saved profiles, and remote directory browsing/transfers. |
| **Branch View (Flat View)** | `Ctrl+B` / `🌿` | Flattens the current directory and all subdirectories into a single unified file list showing relative subpaths. |
| **TreeSize Disk Space Analyzer** | `📊` | Recursively calculates folder sizes in the background and renders proportional visual allocation bars directly inside the `Size` column. |
| **Minimizable & Resizable Right Inspector Dock** | `Ctrl+Q` / `👁 Preview` | Toggleable, resizable (`210px–640px`), and minimizable right dock displaying live previews for the currently selected file (images, Markdown/code/text, CSV/JSON/XML, media, and SHA-256/MD5/CRC32 file metadata) as you navigate with the arrow keys. |
| **Favorites / Bookmarks Bar & Undo** | `Ctrl+D` (`☆`) / `Ctrl+Z` (`↶ Undo`) | Pin frequently used folders to the Favorites bar for one-click navigation and undo recent file operations (rename, batch rename, copy, move, new folder/file) via `Ctrl+Z`. |

### 3. Intelligent Quick Viewer (`F3`) & Internal Editor (`F4`)
- **Quick Viewer (`F3`)**:
  - **Images**: PNG, JPG, GIF, WEBP, BMP, ICO, SVG, TIFF with zoom and pixel dimensions, plus optional hover preview cards in the file list (`enable_image_hover_preview`).
  - **Documents**: PDF and Office/OpenXML package inspection.
  - **Audio & Video**: Built-in media playback.
  - **Structured Data**: Formatted JSON/XML viewer and tabular CSV/TSV preview.
  - **Log Viewer**: Filter by log level (`ERROR`, `WARN`, `INFO`) and live log inspection.
  - **Hex & Checksums**: Binary hex dump and instant SHA-256 checksum inspector.
- **Internal Editor (`F4`)**:
  - Edit text, configuration, and source code files directly in a resizable child window with line/character counters and `Ctrl+S` save support, or configure an external editor in Settings.

### 4. Background Operations Queue & Conflict Resolution
- Background operations for Copy (`F5`), Move (`F6`), and Delete (`F8` / `Delete`) with real-time throughput, item progress, and conflict handling (`Overwrite`, `Skip`, `Keep Both / Auto-Rename`).

### 5. Multi-Language Support & Configurable Feature Tooltips
- **14 Built-in Languages**: Automatically detects the OS language (`system`) or lets you switch live without restart between **Deutsch**, **English**, **Español**, **Français**, **Italiano**, **Nederlands**, **Polski**, **Português**, **Русский**, **Türkçe**, **Українська**, **العربية**, **日本語**, and **中文**.
- **Complete Localization Across All Windows**: Main Window, Menus, Toolbars, Column Headers, Right Inspector Dock, Settings, Storage Analyzer (`Treemap`), Quick Viewer (`F3`), Editor (`F4`), Context Menus, and all 20 Power Tool dialogs dynamically update in the selected UI language.
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
| `F3` | Open Quick Viewer |
| `F4` | Open Internal Text Editor |
| `F5` | Copy selected items to opposite pane |
| `F6` | Move selected items to opposite pane |
| `F7` / `Shift+F7` | New Folder (`F7`) / New File (`Shift+F7`) |
| `F8` / `Delete` | Delete selected items |
| `F9` | Open Terminal in current directory |
| `F10` / `Ctrl+Shift+P` | Open Command Palette |
| `F12` | Toggle Dual-Pane / Single-Pane layout |
| `Tab` | Switch active pane (Left ↔ Right) |
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
| `Ctrl+Shift+C` | Open Built-in FTP / FTPS / SFTP Client |
| `Alt+F7` | Open Recursive Deep Search |
| `Alt+F5` | Open Archive Browser & Packer (`.zip`, `.tar.gz`) |
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
