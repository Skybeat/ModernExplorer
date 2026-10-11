# Modern Explorer

A fast, keyboard-centric dual-pane file manager for **Windows**, **Linux**, and **macOS** built with **.NET 9**, featuring a native **WinUI 3 (Windows App SDK)** experience on Windows and a cross-platform **Avalonia UI** desktop build for **Linux**, **macOS**, and **Windows**.

---

## Supported Operating Systems, Installers & Portable Releases

Every GitHub Release automatically builds and publishes both **100% self-contained native installers** and **portable `.zip` packages** (bundling all required **.NET 9**, **Windows App SDK**, **Microsoft Visual C++ CRT**, and **Avalonia** runtimes with bootstrapper prompts disabled so no external runtime downloads are ever needed) for all supported platforms and CPU architectures:

| Operating System | Architecture | Native Installer(s) | Portable Package | UI Stack |
| :--- | :--- | :--- | :--- | :--- |
| **Windows 10 / 11** | `x64` | `ModernExplorer-Setup-win-x64.exe` | `ModernExplorer-win-x64.zip` | WinUI 3 (Windows App SDK) |
| **Windows 10 / 11** | `ARM64` | `ModernExplorer-Setup-win-arm64.exe` | `ModernExplorer-win-arm64.zip` | WinUI 3 (Windows App SDK) |
| **Windows 10 / 11** | `x86` (32-bit) | `ModernExplorer-Setup-win-x86.exe` | `ModernExplorer-win-x86.zip` | Avalonia UI Desktop |
| **Linux** | `x64` | `ModernExplorer-Setup-linux-x64.deb`<br>`ModernExplorer-Installer-linux-x64.sh` | `ModernExplorer-linux-x64.zip` | Avalonia UI Desktop |
| **Linux** | `ARM64` | `ModernExplorer-Setup-linux-arm64.deb`<br>`ModernExplorer-Installer-linux-arm64.sh` | `ModernExplorer-linux-arm64.zip` | Avalonia UI Desktop |
| **macOS** | `x64` (Intel) | `ModernExplorer-Setup-osx-x64.dmg`<br>`ModernExplorer-Setup-osx-x64.pkg` | `ModernExplorer-osx-x64.zip` | Avalonia UI Desktop |
| **macOS** | `ARM64` (Apple Silicon) | `ModernExplorer-Setup-osx-arm64.dmg`<br>`ModernExplorer-Setup-osx-arm64.pkg` | `ModernExplorer-osx-arm64.zip` | Avalonia UI Desktop |

---

## Feature Overview

### 1. Dual-Pane & Multi-Tab File Management
- **Versioned Title Bar**: Displays the current application version directly in the main title bar (`ModernExplorer v1.8.1`).
- **Dual-Pane & Single-Pane Mode (`F10` / `Tab`)**: Instant switching between left and right file panels with a customizable active-panel border color and thickness.
- **Per-Pane Drive Bar**: Displays all mounted drives (`Fixed`, `Removable`, `Network`, `CD-ROM`, `RAM`) with live free-space and capacity bars plus tooltips.
- **Full Multi-Tab Support (`Ctrl+T`, `Ctrl+W`, `Ctrl+Tab`)**:
  - Open unlimited tabs per pane, duplicate tabs, tear out tabs into new windows, or move tabs between panes.
  - **Tab States**: Normal, **Pinned** (`📌`), **Locked** (`🔒`, blocks navigation), and **Locked with Directory Changes Allowed** (`🔐`, snaps back to its home folder).
- **8 Explorer View Modes**:
  - **Details** (sortable columns: Name, Size, Modified, Extension, plus inline Git & Size bars), **List**, **Tiles**, **Small Icons**, **Medium Icons**, **Large Icons**, **Extra Large Icons**, and **Content**.
- **Breadcrumb & Editable Address Bar (`Ctrl+L` / `Alt+D`)**: Clickable path segments for instant parent navigation or direct path/URI entry.
- **Live Instant Filter (`Ctrl+F`)**: Filter the current directory view in real time with wildcards, substring matching, `ext:` queries, or color tag / note filters (`tag:rot`, `tag:green`, `tag:*`, `note:invoice`).

### 2. Integrated Power Tools (Freely Resizable Windows)
All feature dialogs open as large, freely resizable and maximizable child windows that remember their dimensions across sessions:

| Feature | Shortcut / Button | Description |
| :--- | :--- | :--- |
| **Multi-Rename Tool (Batch Rename)** | `Ctrl+M` / `✏ Batch Rename` | Rename multiple files and folders at once using placeholders (`[N]` name, `[N1-5]` substring, `[E]` extension, `[C]` counter with custom start/step/digits, `[Y][M][D][h][m][s]` timestamps, `[P]` parent folder, plus image/EXIF placeholders `[Width]`, `[Height]`, `[Resolution]`, `[ExifDate]`, `[Camera]`), Search & Replace, Regular Expressions, case transformations, live conflict detection, and one-click Undo. |
| **Visual Side-by-Side File Diff Viewer** | `Ctrl+Shift+U` / `⚖ Diff` | Compare two files line by line side-by-side with LCS alignment, color highlights (**Added**, **Removed**, **Modified**), hunk navigation (`⬆ Prev` / `⬇ Next`), interactive hunk merging (`→` Left to Right / `←` Right to Left), and direct saving. |
| **Checksum Generator & Verifier** | `Ctrl+Shift+K` / `🔐 Hashes` | Calculate `SHA-256`, `SHA-512`, `MD5`, and `CRC32` for selected files, instantly compare against a pasted clipboard hash, export `.sha256`, `.sha512`, `.md5`, or `.sfv` manifests, and verify existing checksum manifest files. |
| **File Splitter, Joiner & Secure Shredder** | `✂ Split/Shred` | Split large files into `.001`, `.002`… parts with an automatic `.sha256` manifest, rejoin `.001` parts seamlessly, or permanently shred sensitive files with **1-Pass**, **3-Pass DoD**, or **7-Pass** cryptographic overwriting. |
| **Color File Tags / Labels & Notes** | `Ctrl+Shift+L` / `🏷 Tags` | Assign persistent color tags (**Red**, **Orange**, **Yellow**, **Green**, **Blue**, **Purple**) and text notes to any file or folder, displayed directly as badges in the file list and filterable via `tag:red`, `tag:rot`, `tag:*`, or `note:...`. |
| **Workspace Profiles (Saved Layouts)** | `Ctrl+Shift+O` / `🗂 Workspaces` | Save and restore complete dual-pane tab layouts (including pinned/locked tabs, view modes, and split ratio) under custom profile names with a single click. |
| **Smart Copy / Path & List Exporter** | `Ctrl+Shift+E` / `📋 Export` | Copy or export selected file lists as **Full Paths**, **File Names**, **Relative Paths**, **Forward-Slash Paths**, **Markdown `file://` Links**, **CSV Table**, **Markdown Table**, **JSON Array**, or **HTML Table**. |
| **Virtual File Basket (Drop-Basket)** | `Ctrl+Shift+B` / `🧺 Basket` | Collect files and folders from arbitrary directories and drives into a virtual staging basket, then execute unified batch actions (**Copy Here**, **Move Here**, **Pack into Archive**, **Batch Rename**, or **Export List**). |
| **EXIF / Media Metadata & Timestamp/Attribute Editor** | `Ctrl+Shift+A` / `🕒 Attr/EXIF` | Inspect binary PNG/JPEG/GIF/BMP/WEBP image dimensions and EXIF camera data (`Width`, `Height`, `DateTaken`, `CameraMake`, `CameraModel`, `ISO`), batch-edit timestamps (`Created`, `Modified`, `Accessed`, `+/- hours` shift, copy EXIF `DateTaken` to file timestamps, `Touch`), and batch-toggle file attributes (`ReadOnly`, `Hidden`, `Archive`, `System`). |
| **Embedded Quick Command Bar (`>_`)** | `Ctrl+Shift+J` / `>_ Console` | Docked PowerShell/CMD/sh command bar at the bottom of the main window executing directly in the active folder with `cd` folder navigation, `Up`/`Down` command history, and a collapsible live output drawer. |
| **Extended Git Panel** | `Ctrl+Shift+G` / `⎇ Git` | Clickable Git branch badge and toolbar panel showing local branches with one-click `git checkout`, modified/untracked working tree status with `git add` staging, direct **Visual Side-by-Side Diff against `HEAD`**, and recent commit history (`git log`). |
| **Directory Compare & Synchronize** | `Ctrl+Shift+S` / `⇄ Sync` | Compare left and right directories recursively by **Size & Timestamp**, **Size Only**, or **SHA-256 Content**. Supports **Bidirectional Merge (newer wins)**, **Left → Right Mirror**, and **Right → Left Mirror** with optional orphan deletion and live progress feedback. |
| **Recursive Deep Search** | `Alt+F7` / `🔍 Deep Search` | High-speed recursive file search by wildcard (`*.cs;*.json`), Regular Expression, file size, date range, attributes, and **full-text content search** inside files. Results can be opened directly as a virtual tab or jumped to in the active pane. |
| **Duplicate File Finder** | `🧹 Duplicates` | 3-stage duplicate scanner (**File Size grouping → 4 KB Partial Hash → Full SHA-256 Hash**) with a **real-time ProgressBar, percentage indicator, and live file counter**. Calculates wasted disk space and offers one-click selection of all duplicates (preserving the original per group). |
| **Graphical Disk Space Analyzer & Treemap (WinDirStat / WizTree)** | `Ctrl+Shift+W` / `🗺 Treemap` | Interactive **Squarified Treemap** visualizer in a freely resizable analyzer window with **interactive vertical & horizontal drag splitters**, an **integrated `🌳 Explorer Tree ▾` dropdown**, **toggleable `🔥 Top 100 Largest Files` view**, **live Color Mode switcher (`🎨 Categories` vs. `⏳ File Age Heatmap`)**, **Markdown/CSV Storage Report export (`📄 Report`)**, **7 customizable category colors**, **Drill-Down zooming**, **Multi-Selection via `Ctrl+Click`**, and direct deletion via the **Right-Click Context Menu (`🗑 Delete [Del]`)** or **`Delete` (`Entf`) key** with instant in-place Treemap recalculation. |
| **Archive Browser & Packer** | `Alt+F5` (`📦 Pack`) / `Alt+F9` (Extract) | Browse inside archives (`.zip`, `.7z`, `.rar`, `.tar`, `.gz`, `.bz2`, `.xz`, `.tgz`, `.nupkg`, `.jar`) transparently like normal folders (`archive://`), extract individual or all entries, and pack selected files/folders into `.zip`, `.tar.gz`, or `.7z` archives. |
| **Built-in FTP / FTPS / SFTP Client** | `Ctrl+Shift+C` / `🌐 FTP/SFTP` | Connect directly to remote servers via **SFTP (SSH)**, **FTPS (Explicit TLS)**, or **FTP** as virtual paths (`sftp://`, `ftps://`, `ftp://`). Includes a Connection Manager with **Windows DPAPI password encryption**, SSH private key authentication, and seamless upload/download/rename/delete/mkdir via `F5`/`F6`/`F7`/`F8`. |
| **Branch View (Flat View)** | `Ctrl+B` / `🌿` | Flattens the current directory and all subdirectories into a single unified file list showing relative subpaths. |
| **TreeSize Disk Space Analyzer** | `📊` | Recursively calculates folder sizes in the background and renders proportional visual allocation bars directly inside the `Size` column. |
| **Docked Quick Preview Pane** | `Ctrl+Q` / `👁 Preview` | Toggleable third column displaying live previews for the currently selected file (images, Markdown/code/text, CSV/JSON/XML, media, and SHA-256/MD5/CRC32 file metadata) as you navigate with the arrow keys. |
| **Favorites / Bookmarks Bar & Undo** | `Ctrl+D` (`☆`) / `Ctrl+Z` (`↶ Undo`) | Pin frequently used folders to the Favorites bar for one-click navigation and undo recent file operations (rename, batch rename, copy, move, new folder/file) via `Ctrl+Z`. |

### 3. Intelligent Quick Viewer (`F3`) & Internal Editor (`F4`)
- **Quick Viewer (`F3`)**:
  - **Images**: PNG, JPG, GIF, WEBP, BMP, ICO, SVG, TIFF with zoom and pixel dimensions.
  - **Documents**: Native PDF page rendering and Office/OpenXML package inspection.
  - **Audio & Video**: Built-in hardware-accelerated playback via `MediaPlayerElement`.
  - **Structured Data**: Formatted JSON/XML viewer and tabular CSV/TSV preview.
  - **Log Viewer**: Filter by log level (`ERROR`, `WARN`, `INFO`) and live `tail -f` file monitoring.
  - **Hex & Checksums**: Binary hex dump and instant SHA-256 checksum inspector.
- **Internal Editor (`F4`)**:
  - Edit text, configuration, and source code files directly in a resizable child window with line/character counters and `Ctrl+S` save support, or configure an external editor in Settings.

### 4. Background Operations Queue & Conflict Resolution
- Non-blocking background queue for Copy (`F5`), Move (`F6`), and Delete (`F8` / `Shift+Delete`) with real-time throughput (`MB/s`), item progress, pause/cancel support, and interactive conflict resolution (`Overwrite`, `Skip`, `Keep Both / Auto-Rename`, `Apply to All`).

### 5. Multi-Language Support & Configurable Feature Tooltips
- **10 Built-in Languages**: Automatically detects the OS language (`system`) or lets you switch live between **Deutsch**, **English**, **Español**, **Français**, **Italiano**, **Nederlands**, **Polski**, **Português**, **日本語**, and **中文**.
- **Complete Localization Across All Windows**: Main Window, Settings (including Treemap colors, Color Pickers & Child Window font size cards), Storage Analyzer (`Treemap`), Quick Viewer (`F3`), Editor (`F4`), and all Power Tool child windows dynamically update in the selected UI language.
- **Localized Feature Tooltips**: Every toolbar button, feature button, panel control, and function key (`F1`–`F12`) provides a concise, localized explanation of the feature in the active UI language.
- **Tooltip Toggle in Settings**: Tooltips can be enabled or disabled at any time under **Settings → Appearance & Design → Tooltips**.

### 6. Live Customization, Interactive Color Picker & Automatic Self-Updater
- **Appearance, Interactive Color Picker (`🎨 Farbpicker`), Treemap Colors & Font Scaling**: Switch live between Dark, Light, and System themes, customize all **10 color settings** (**Active Panel Border**, **Selection Border**, **Marked Item Text**, and all **7 Storage Analyzer Treemap category colors**) using either **quick presets**, **`#RRGGBB` hex codes**, or an **interactive WinUI 3 `ColorPicker` flyout (`🎨 Farbpicker`) with color spectrum, brightness slider, RGB/HSV channels, live preview, and one-click default reset** on every single color setting. Adjust font sizes either **individually per UI area and per Child Window** or via a **single simplified Global Font Size checkbox**.
- **Elevated Self-Updater (`ModernExplorer.Updater`)**: Checks GitHub Releases on startup (or manually in the About window), downloads the matching architecture installer/package, closes Modern Explorer, launches a dedicated graphical progress window (`ProgressBar` + live status) with automatic UAC elevation support for per-machine (`Program Files`) installations, and automatically brings the restarted application window to the foreground.

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
| `F7` / `Shift+F7` | New File from Template (`F7`) / New Folder (`Shift+F7`) |
| `F8` / `Delete` | Move to Recycle Bin (`Shift+Delete` for permanent delete) |
| `F9` | Open Terminal in current directory |
| `F10` | Open Command Palette (`Ctrl+Shift+P`) |
| `F11` | Toggle Fullscreen |
| `F12` | Toggle Dual-Pane / Single-Pane layout (`Shift+F12` Swap Panels) |
| `Tab` | Switch active pane (Left ↔ Right) |
| `Space` / `Insert` | Mark / unmark file and advance selection |
| `Ctrl+M` | Open Multi-Rename Tool (Batch Rename) |
| `Ctrl+Shift+U` | Open Visual Side-by-Side File Diff Viewer |
| `Ctrl+Shift+K` | Open Checksum Generator & Verifier (`SHA-256`, `SHA-512`, `MD5`, `CRC32`) |
| `Ctrl+Shift+L` | Assign Color Tags / Labels & Notes (`tag:rot`, `note:...`) |
| `Ctrl+Shift+O` | Open Workspace Profiles Manager |
| `Ctrl+Shift+E` | Open Smart Copy / Path & Table Exporter |
| `Ctrl+Shift+B` | Open Virtual File Basket (Drop-Basket) |
| `Ctrl+Shift+A` | Open EXIF / Media Metadata & Timestamp / Attribute Editor |
| `Ctrl+Shift+J` | Toggle / Focus Embedded Quick Command Bar (`>_`) |
| `Ctrl+Shift+G` | Open Extended Git Panel (Branches, Status, Diff vs `HEAD`, Commit Log) |
| `Ctrl+Shift+S` | Open Directory Compare & Synchronize |
| `Ctrl+Shift+W` | Open Graphical Disk Space Analyzer & Squarified Treemap (WinDirStat) |
| `Alt+F7` / `Ctrl+Shift+F` | Open Recursive Deep Search |
| `Alt+F5` / `Alt+F9` | Pack into Archive (`.zip`, `.tar.gz`, `.7z`) / Extract Archive |
| `Ctrl+B` | Toggle Branch View (Flat View) |
| `Ctrl+Q` | Toggle Docked Quick Preview Pane |
| `Ctrl+D` | Add / Remove current folder in Favorites Bar |
| `Ctrl+Z` | Undo last file operation |

---

## Building & Publishing Releases

Pushing a version tag (`v*`, e.g. `v1.8.0`) or running `scripts/publish-releases.ps1 -Version "1.8.0"` builds and publishes all self-contained portable archives and native installers (`.exe`, `.deb`, `.sh`, `.dmg`, `.pkg`) and attaches them to the GitHub Release.
