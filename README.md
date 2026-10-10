# Modern Explorer

A fast, keyboard-centric dual-pane file manager built with **.NET 9**, featuring a native **WinUI 3 (Windows App SDK)** experience on Windows and a cross-platform **Avalonia UI** desktop build for **Linux**, **macOS**, and **Windows**.

## Supported Operating Systems & Releases

Every GitHub Release provides ready-to-run binary packages for all supported platforms and architectures:

| Operating System | Architecture | Package Name | UI Stack |
| :--- | :--- | :--- | :--- |
| **Windows 10 / 11** | `x64` | `ModernExplorer-win-x64.zip` | WinUI 3 (Windows App SDK) |
| **Windows 10 / 11** | `ARM64` | `ModernExplorer-win-arm64.zip` | WinUI 3 (Windows App SDK) |
| **Windows 10 / 11** | `x86` (32-bit) | `ModernExplorer-win-x86.zip` | Avalonia UI Desktop |
| **Linux** | `x64` | `ModernExplorer-linux-x64.zip` | Avalonia UI Desktop |
| **Linux** | `ARM64` | `ModernExplorer-linux-arm64.zip` | Avalonia UI Desktop |
| **macOS** | `x64` (Intel) | `ModernExplorer-osx-x64.zip` | Avalonia UI Desktop |
| **macOS** | `ARM64` (Apple Silicon) | `ModernExplorer-osx-arm64.zip` | Avalonia UI Desktop |

## Highlights

- **Dual-Pane & Single-Pane Explorer**: Instant switching via `Tab` and `F10`, customizable active pane border, per-pane drive bar with live free-space indicators, and full multi-tab support (including pinned and locked tabs).
- **Keyboard-First Commander Workflow**: Classic function-key bar (`F2` Rename, `F3` Quick Viewer, `F4` Internal Editor, `F5` Copy, `F6` Move, `F7` New File from Template, `Shift+F7` New Folder, `F8` Delete, `F9` Terminal, `F10` Dual/Single Pane, `Ctrl+Shift+P` Command Palette).
- **Intelligent Quick Viewer (`F3`) & Editor (`F4`)**:
  - Instant image preview (PNG, JPG, GIF, WEBP, BMP, ICO, SVG) with zoom and metadata
  - PDF document rendering
  - Audio & video playback (`MediaPlayerElement`)
  - Archive content browser (`.zip`, `.nupkg`, `.jar`, `.docx`, `.xlsx`, `.pptx`)
  - Structured CSV/TSV table viewer and formatted JSON/XML viewer
  - Log viewer with level filtering (`ERROR`, `WARN`, `INFO`) and live `tail -f` mode
  - Binary hex dump and SHA-256 checksum inspector
- **Multi-Language Support**: Automatically detects the OS system language (`system` default) or lets you manually select from 10 languages (**Deutsch**, **English**, **Español**, **Français**, **Italiano**, **Nederlands**, **Polski**, **Português**, **日本語**, **中文**) live in Settings.
- **Automatic GitHub Release Updates**: Checks for new releases on startup, automatically selects the matching OS/architecture asset (`win-x64`, `win-arm64`, `win-x86`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`), and offers one-click download and installation.
- **Rich Customization**: Dark, Light, and System themes, customizable selection/focus/marked-item colors, configurable font sizes and row heights, image hover previews, and session persistence.

## Building & Publishing Releases

```powershell
# Run unit tests
dotnet test

# Build & publish all OS release packages (Windows, Linux, macOS) to GitHub Releases
pwsh -File scripts/publish-releases.ps1 -Version 1.0.0
```
