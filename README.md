# Modern Explorer

A fast, keyboard-centric dual-pane file manager for Windows 10/11 built with **.NET 9** and **WinUI 3 (Windows App SDK)**.

## Highlights

- **Dual-Pane & Single-Pane Explorer**: Instant switching via `Tab` and `F10`, customizable active pane border, per-pane drive bar with live free-space indicators, and full multi-tab support (including pinned and locked tabs).
- **Keyboard-First Commander Workflow**: Classic function-key bar (`F2` Rename, `F3` Quick Viewer, `F4` Internal Editor, `F5` Copy, `F6` Move, `F7` New File from Template, `Shift+F7` New Folder, `F8` Delete, `F9` Compare, `F10` Dual/Single Pane, `Ctrl+Shift+P` Command Palette).
- **Intelligent Quick Viewer (`F3`) & Editor (`F4`)**:
  - Instant image preview (PNG, JPG, GIF, WEBP, BMP, ICO, SVG) with zoom and metadata
  - PDF document rendering
  - Audio & video playback (`MediaPlayerElement`)
  - Archive content browser (`.zip`, `.nupkg`, `.jar`, `.docx`, `.xlsx`, `.pptx`)
  - Structured CSV/TSV table viewer and formatted JSON/XML viewer
  - Log viewer with level filtering (`ERROR`, `WARN`, `INFO`) and live `tail -f` mode
  - Binary hex dump and SHA-256 checksum inspector
- **Multi-Language Support**: Automatically detects the Windows system language (`system` default) or lets you manually select from 10 languages (**Deutsch**, **English**, **Español**, **Français**, **Italiano**, **Nederlands**, **Polski**, **Português**, **日本語**, **中文**) live in Settings.
- **Automatic GitHub Release Updates**: Checks for new releases on startup and offers one-click download and installation.
- **Rich Customization**: Dark, Light, and System themes, customizable selection/focus/marked-item colors, configurable font sizes and row heights, image hover previews, and session persistence.

## Building & Running

### Prerequisites
- Windows 10 (version 1809 / build 17763 or newer) or Windows 11
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Build & Test
```powershell
dotnet test
dotnet build src/ModernExplorer.App/ModernExplorer.App.csproj -p:Platform=x64
```

