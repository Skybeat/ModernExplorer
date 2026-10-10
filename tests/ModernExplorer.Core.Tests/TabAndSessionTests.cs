using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.ViewModels;

namespace ModernExplorer.Core.Tests;

public sealed class TabAndSessionTests : IDisposable
{
    private readonly string _tempDir;

    public TabAndSessionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ModernExplorer_Tabs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task LockedTab_OpensSubfolderInNewTab_InsteadOfChangingOwnPath()
    {
        var subDir = Path.Combine(_tempDir, "Sub");
        Directory.CreateDirectory(subDir);

        using var hub = new WatcherHub();
        using var panel = new PanelViewModel(PanelSide.Left, hub, new ImmediateDispatcher());

        var tab1 = await panel.CreateTabAsync(_tempDir, activate: true);
        tab1.IsLocked = true;

        await tab1.NavigateAsync(subDir);

        Assert.Equal(2, panel.Tabs.Count);
        Assert.Equal(_tempDir, tab1.CurrentPath);
        Assert.Equal(subDir, panel.ActiveTab?.CurrentPath);
    }

    [Fact]
    public async Task PinnedTab_CannotBeClosedByStandardClose_AndStaysInPinnedZone()
    {
        using var hub = new WatcherHub();
        using var panel = new PanelViewModel(PanelSide.Left, hub, new ImmediateDispatcher());

        var t1 = await panel.CreateTabAsync(_tempDir);
        var t2 = await panel.CreateTabAsync(_tempDir);
        panel.ActiveTab = t2;
        panel.TogglePinActiveTab();

        Assert.True(t2.IsPinned);
        Assert.Equal(0, panel.Tabs.IndexOf(t2)); // Pinned zone is on the left

        bool closed = await panel.CloseTabAsync(t2, forceIfPinned: false);
        Assert.False(closed);
        Assert.Equal(2, panel.Tabs.Count);
    }

    [Fact]
    public void SessionStore_SavesAtomicallyAndLoadsStateRoundtrip()
    {
        var file = Path.Combine(_tempDir, "session.json");
        using var store = new SessionStore(file);

        var state = new SessionState
        {
            Windows =
            [
                new WindowState
                {
                    Layout = new LayoutState { Dual = true, SplitRatio = 0.4, ActivePanel = PanelSide.Right },
                    Left = new PanelState { ActiveIndex = 0, Tabs = [new TabState { Path = _tempDir, Pinned = true }] },
                }
            ]
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Windows);
        Assert.Equal(PanelSide.Right, loaded.Windows[0].Layout.ActivePanel);
        Assert.True(loaded.Windows[0].Left.Tabs[0].Pinned);
    }

    [Fact]
    public async Task PanelViewModel_LoadsDrives_AndNavigatesCurrentTabOnDriveClick()
    {
        using var hub = new WatcherHub();
        using var panel = new PanelViewModel(PanelSide.Left, hub, new ImmediateDispatcher());

        Assert.NotEmpty(panel.Drives);
        await panel.CreateTabAsync(_tempDir);

        var targetDrive = panel.Drives[0].RootPath;
        await panel.NavigateToDriveAsync(targetDrive);

        Assert.Equal(targetDrive, panel.ActiveTab?.CurrentPath);
        Assert.True(panel.Drives[0].IsSelected);
    }

    [Fact]
    public void ShellIconExtractor_ExtractsSystemIconsForFoldersFilesAndDrives()
    {
        var folderIcon = ShellIconExtractor.GetStandardFolderIcon();
        Assert.NotNull(folderIcon);
        Assert.True(folderIcon!.Width > 0 && folderIcon.Height > 0);
        Assert.Equal(folderIcon.Width * folderIcon.Height * 4, folderIcon.BgraPixels.Length);
        Assert.Contains(folderIcon.BgraPixels, b => b != 0);

        var txtIcon = ShellIconExtractor.GetExtensionIcon(".txt");
        Assert.NotNull(txtIcon);
        Assert.Contains(txtIcon!.BgraPixels, b => b != 0);
    }

    [Fact]
    public void SettingsStore_DefaultsToSaveSessionTrueAndDarkTheme_AndPersistsChanges()
    {
        var file = Path.Combine(_tempDir, "settings.json");
        var store = new ModernExplorer.Core.Settings.SettingsStore(file);
        var vm = new SettingsViewModel(store);

        Assert.True(vm.SaveSession);
        Assert.Equal(ModernExplorer.Core.Settings.AppTheme.Dark, vm.Theme);
        Assert.Equal(13.0, vm.FileListFontSize);

        vm.SetTheme(ModernExplorer.Core.Settings.AppTheme.Light);
        vm.FileListFontSize = 16.5;
        vm.TabFontSize = 14.0;
        vm.DriveBarFontSize = 13.5;
        vm.AddressBarFontSize = 15.0;
        vm.BreadcrumbFontSize = 12.5;
        vm.StatusBarFontSize = 13.0;
        vm.FunctionBarFontSize = 13.0;
        vm.SaveSession = false;

        var reloaded = new SettingsViewModel(store);
        Assert.Equal(ModernExplorer.Core.Settings.AppTheme.Light, reloaded.Theme);
        Assert.Equal(16.5, reloaded.FileListFontSize);
        Assert.Equal(14.0, reloaded.TabFontSize);
        Assert.Equal(13.5, reloaded.DriveBarFontSize);
        Assert.Equal(15.0, reloaded.AddressBarFontSize);
        Assert.Equal(12.5, reloaded.BreadcrumbFontSize);
        Assert.Equal(13.0, reloaded.StatusBarFontSize);
        Assert.Equal(13.0, reloaded.FunctionBarFontSize);
        Assert.False(reloaded.SaveSession);

        reloaded.ResetFontSizesToDefaults();
        Assert.Equal(13.0, reloaded.FileListFontSize);
        Assert.Equal(12.0, reloaded.TabFontSize);
    }

    [Fact]
    public async Task MainViewModel_SavesTabsAndPathsOnClose_AndRestoresThemOnNextStart()
    {
        var dirA = Directory.CreateDirectory(Path.Combine(_tempDir, "Alpha")).FullName;
        var dirB = Directory.CreateDirectory(Path.Combine(_tempDir, "Beta")).FullName;
        var dirC = Directory.CreateDirectory(Path.Combine(_tempDir, "Gamma")).FullName;

        var sessionFile = Path.Combine(_tempDir, "session.json");
        var settingsFile = Path.Combine(_tempDir, "settings.json");

        // 1. First application run: open tabs, change paths, and close application (DisposeAsync)
        {
            var kb = ModernExplorer.Core.Input.KeyBindingStore.LoadDefaultsWithUserOverride(null);
            var watcher = new WatcherHub();
            var ui = new NullShellUi();
            var queue = new ModernExplorer.Core.Operations.FileOperationQueue(ui);
            var templates = new TemplateStore(_tempDir);
            var creator = new FileCreationService(templates);
            var sessionStore = new SessionStore(sessionFile);
            var settingsStore = new ModernExplorer.Core.Settings.SettingsStore(settingsFile);

            var vm1 = new MainViewModel(kb, watcher, queue, creator, sessionStore, new ImmediateDispatcher(), ui, settingsStore);
            await vm1.InitializeAsync();

            await vm1.Left.ActiveTab!.NavigateAsync(dirA);
            await vm1.Left.CreateTabAsync(dirB, activate: true);
            await vm1.Right.ActiveTab!.NavigateAsync(dirC);

            // Simulate TwoWay ListView behavior when clearing tabs during Dispose
            vm1.Left.Tabs.CollectionChanged += (_, _) =>
            {
                if (vm1.Left.Tabs.Count == 0) vm1.Left.ActiveTab = null;
            };
            vm1.Right.Tabs.CollectionChanged += (_, _) =>
            {
                if (vm1.Right.Tabs.Count == 0) vm1.Right.ActiveTab = null;
            };

            await vm1.DisposeAsync();
        }

        // 2. Second application run: session must restore all previously open tabs and paths
        {
            var kb = ModernExplorer.Core.Input.KeyBindingStore.LoadDefaultsWithUserOverride(null);
            var watcher = new WatcherHub();
            var ui = new NullShellUi();
            var queue = new ModernExplorer.Core.Operations.FileOperationQueue(ui);
            var templates = new TemplateStore(_tempDir);
            var creator = new FileCreationService(templates);
            var sessionStore = new SessionStore(sessionFile);
            var settingsStore = new ModernExplorer.Core.Settings.SettingsStore(settingsFile);

            await using var vm2 = new MainViewModel(kb, watcher, queue, creator, sessionStore, new ImmediateDispatcher(), ui, settingsStore);
            await vm2.InitializeAsync();

            Assert.Equal(2, vm2.Left.Tabs.Count);
            Assert.Equal(dirA, vm2.Left.Tabs[0].CurrentPath);
            Assert.Equal(dirB, vm2.Left.Tabs[1].CurrentPath);
            Assert.Equal(dirB, vm2.Left.ActiveTab?.CurrentPath);
            Assert.Single(vm2.Right.Tabs);
            Assert.Equal(dirC, vm2.Right.ActiveTab?.CurrentPath);
        }
    }

    [Fact]
    public async Task TabViewModel_FocusedItem_SynchronizesIsFocused_AndPreservesSelectionOnTransientNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, "one.txt"), "1");
        File.WriteAllText(Path.Combine(_tempDir, "two.png"), "2");
        File.WriteAllText(Path.Combine(_tempDir, "three.jpg"), "3");

        using var hub = new WatcherHub();
        using var panel = new PanelViewModel(PanelSide.Left, hub, new ImmediateDispatcher());
        var tab = await panel.CreateTabAsync(_tempDir, activate: true);

        Assert.Equal(4, tab.Items.Count); // '..' + 3 files
        Assert.Same(tab.Items[0], tab.FocusedItem);
        Assert.True(tab.Items[0].IsFocused);
        Assert.False(tab.Items[1].IsFocused);
        Assert.False(tab.Items[2].IsFocused);

        // Mouse click selection on item 2
        tab.FocusedItem = tab.Items[2];
        Assert.False(tab.Items[0].IsFocused);
        Assert.False(tab.Items[1].IsFocused);
        Assert.True(tab.Items[2].IsFocused);

        // Transient null assignment (e.g., when switching panels via Tab) must not lose FocusedItem
        tab.FocusedItem = null;
        Assert.Same(tab.Items[2], tab.FocusedItem);
        Assert.True(tab.Items[2].IsFocused);

        // Relative navigation from the last selected item (Index 2 -> Up -> Index 1)
        tab.MoveFocus(-1);
        Assert.Same(tab.Items[1], tab.FocusedItem);
        Assert.True(tab.Items[1].IsFocused);
        Assert.False(tab.Items[2].IsFocused);
    }

    [Fact]
    public void SettingsViewModel_PersistsSelectionBorderColor_AndImageHoverPreview()
    {
        var file = Path.Combine(_tempDir, "settings_border_hover.json");
        var store = new ModernExplorer.Core.Settings.SettingsStore(file);
        var vm = new SettingsViewModel(store);

        Assert.Equal("#FFFFFF", vm.SelectionBorderColor);
        Assert.True(vm.EnableImageHoverPreview);

        vm.SetSelectionBorderColor("#00E5FF");
        vm.EnableImageHoverPreview = false;

        var reloaded = new SettingsViewModel(store);
        Assert.Equal("#00E5FF", reloaded.SelectionBorderColor);
        Assert.False(reloaded.EnableImageHoverPreview);

        // Invalid color value preserves the last valid color; NormalizeHexColor falls back to default
        reloaded.SetSelectionBorderColor("not-a-color");
        Assert.Equal("#00E5FF", reloaded.SelectionBorderColor);
        Assert.Equal("#FFFFFF", ModernExplorer.Core.Settings.AppSettings.NormalizeHexColor("not-a-color"));
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".JPG", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".gif", true)]
    [InlineData(".webp", true)]
    [InlineData(".bmp", true)]
    [InlineData(".ico", true)]
    [InlineData(".txt", false)]
    [InlineData(".exe", false)]
    [InlineData("", false)]
    public void FileItemViewModel_IsSupportedImageExtension_DetectsImageFormats(string ext, bool expected)
    {
        Assert.Equal(expected, FileItemViewModel.IsSupportedImageExtension(ext));
    }

    [Fact]
    public void SettingsViewModel_PersistsWindowBounds_ActivePanelBorder_MarkedItemColor_AndPreviewSize()
    {
        var file = Path.Combine(_tempDir, "settings_windows_colors.json");
        var store = new ModernExplorer.Core.Settings.SettingsStore(file);
        var vm = new SettingsViewModel(store);

        Assert.Equal("#0078D4", vm.ActivePanelBorderColor);
        Assert.Equal("#38BDF8", vm.MarkedItemTextColor);
        Assert.Equal(320.0, vm.ImageHoverPreviewSize);
        Assert.Equal(1480, vm.MainWindowWidth);
        Assert.Equal(900, vm.MainWindowHeight);
        Assert.False(vm.MainWindowIsMaximized);
        Assert.Equal(1040, vm.SettingsWindowWidth);
        Assert.Equal(760, vm.SettingsWindowHeight);
        Assert.Equal(860, vm.ChildWindowWidth);
        Assert.Equal(640, vm.ChildWindowHeight);

        vm.SetActivePanelBorderColor("#10B981");
        vm.SetMarkedItemTextColor("#F59E0B");
        vm.ImageHoverPreviewSize = 480.0;
        vm.UpdateMainWindowBounds(1720, 1020, isMaximized: false);
        vm.UpdateMainWindowBounds(1920, 1080, isMaximized: true);
        vm.UpdateSettingsWindowBounds(1120, 840, isMaximized: false);
        vm.UpdateChildWindowBounds(1050, 740, isMaximized: false);

        var reloaded = new SettingsViewModel(store);
        Assert.Equal("#10B981", reloaded.ActivePanelBorderColor);
        Assert.Equal("#F59E0B", reloaded.MarkedItemTextColor);
        Assert.Equal(480.0, reloaded.ImageHoverPreviewSize);
        Assert.Equal(360.0, reloaded.ImageHoverPreviewHeight);
        Assert.Equal(520.0, reloaded.ImageHoverCardMaxWidth);
        Assert.Equal(1720, reloaded.MainWindowWidth);
        Assert.Equal(1020, reloaded.MainWindowHeight);
        Assert.True(reloaded.MainWindowIsMaximized);
        Assert.Equal(1120, reloaded.SettingsWindowWidth);
        Assert.Equal(840, reloaded.SettingsWindowHeight);
        Assert.False(reloaded.SettingsWindowIsMaximized);
        Assert.Equal(1050, reloaded.ChildWindowWidth);
        Assert.Equal(740, reloaded.ChildWindowHeight);
        Assert.False(reloaded.ChildWindowIsMaximized);

        reloaded.ResetWindowBoundsToDefaults();
        Assert.Equal(1040, reloaded.SettingsWindowWidth);
        Assert.Equal(760, reloaded.SettingsWindowHeight);
        Assert.False(reloaded.MainWindowIsMaximized);
    }

    [Fact]
    public async Task PanelViewModel_UpdateSelectedDrive_HighlightsActiveTabDrive()
    {
        using var hub = new WatcherHub();
        using var panel = new PanelViewModel(PanelSide.Left, hub, new ImmediateDispatcher());

        panel.Drives.Clear();
        panel.Drives.Add(new DriveItemViewModel(@"C:\", "C:", "", DriveType.Fixed, "C:", @"C:\"));
        panel.Drives.Add(new DriveItemViewModel(@"D:\", "D:", "", DriveType.Fixed, "D:", @"D:\"));

        var tempRoot = Path.GetPathRoot(_tempDir) ?? @"C:\";
        if (!panel.Drives.Any(d => string.Equals(d.RootPath, tempRoot, StringComparison.OrdinalIgnoreCase)))
            panel.Drives.Add(new DriveItemViewModel(tempRoot, tempRoot.TrimEnd('\\'), "", DriveType.Fixed, tempRoot.TrimEnd('\\'), tempRoot));

        await panel.CreateTabAsync(_tempDir, activate: true);

        var selected = panel.Drives.Single(d => d.IsSelected);
        Assert.Equal(tempRoot.TrimEnd('\\'), selected.RootPath.TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public void SettingsViewModel_PersistsBehaviorSettings_AndUpdatesFileItemDisplayName()
    {
        var file = Path.Combine(_tempDir, "settings_behavior.json");
        var store = new ModernExplorer.Core.Settings.SettingsStore(file);
        var vm = new SettingsViewModel(store);

        Assert.False(vm.ShowHiddenFilesByDefault);
        Assert.True(vm.ShowFileExtensions);
        Assert.True(vm.SortFoldersFirst);
        Assert.True(vm.ConfirmDeleteToRecycleBin);
        Assert.True(vm.AutoCalculateFolderSizeOnMark);
        Assert.True(vm.ShowDriveBar);
        Assert.True(vm.ShowFunctionKeyBar);
        Assert.Equal(26.0, vm.FileListRowHeight);

        var entry = new FileEntry(
            Name: "dokument.pdf",
            FullPath: @"C:\Temp\dokument.pdf",
            IsDirectory: false,
            Size: 1024,
            LastWriteTime: DateTime.UtcNow,
            Attributes: FileAttributes.Normal);
        var item = new FileItemViewModel(entry);
        Assert.Equal("dokument.pdf", item.DisplayName);

        vm.ShowFileExtensions = false;
        item.RefreshDisplayName();
        Assert.Equal("dokument", item.DisplayName);

        vm.ShowHiddenFilesByDefault = true;
        vm.SortFoldersFirst = false;
        vm.ConfirmDeleteToRecycleBin = false;
        vm.AutoCalculateFolderSizeOnMark = false;
        vm.ShowDriveBar = false;
        vm.ShowFunctionKeyBar = false;
        vm.FileListRowHeight = 32.0;

        var reloaded = new SettingsViewModel(store);
        Assert.True(reloaded.ShowHiddenFilesByDefault);
        Assert.False(reloaded.ShowFileExtensions);
        Assert.False(reloaded.SortFoldersFirst);
        Assert.False(reloaded.ConfirmDeleteToRecycleBin);
        Assert.False(reloaded.AutoCalculateFolderSizeOnMark);
        Assert.False(reloaded.ShowDriveBar);
        Assert.False(reloaded.ShowFunctionKeyBar);
        Assert.Equal(32.0, reloaded.FileListRowHeight);

        reloaded.ResetAllToDefaults();
        Assert.True(reloaded.ShowFileExtensions);
        Assert.True(reloaded.SortFoldersFirst);
        Assert.Equal(26.0, reloaded.FileListRowHeight);
    }

    [Fact]
    public async Task FileContentService_DetectsAndLoadsIntelligentPreviewModes()
    {
        // 1. Image file (PNG with magic header)
        var pngPath = Path.Combine(_tempDir, "bild.png");
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        await File.WriteAllBytesAsync(pngPath, pngHeader);

        var pngDoc = await ModernExplorer.Core.Viewer.FileContentService.LoadPreviewAsync(pngPath);
        Assert.Equal(ModernExplorer.Core.Viewer.PreviewMode.Image, pngDoc.Mode);
        Assert.Contains("PNG", pngDoc.FileTypeDescription);
        Assert.False(string.IsNullOrEmpty(pngDoc.Sha256Hex));

        // 2. CSV spreadsheet file
        var csvPath = Path.Combine(_tempDir, "daten.csv");
        await File.WriteAllTextAsync(csvPath, "Name;Alter;Stadt\nAnna;29;Berlin\nMax;34;München");
        var csvDoc = await ModernExplorer.Core.Viewer.FileContentService.LoadPreviewAsync(csvPath);
        Assert.Equal(ModernExplorer.Core.Viewer.PreviewMode.Csv, csvDoc.Mode);
        Assert.NotNull(csvDoc.CsvTable);
        Assert.Equal(3, csvDoc.CsvTable!.Headers.Count);
        Assert.Equal(2, csvDoc.CsvTable.Rows.Count);
        Assert.Equal("Berlin", csvDoc.CsvTable.Rows[0][2]);

        // 3. ZIP archive
        var zipPath = Path.Combine(_tempDir, "paket.zip");
        using (var fs = File.Create(zipPath))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("ordner/readme.txt");
            await using var s1 = e1.Open();
            await s1.WriteAsync(System.Text.Encoding.UTF8.GetBytes("Hallo Welt"));
        }
        var zipDoc = await ModernExplorer.Core.Viewer.FileContentService.LoadPreviewAsync(zipPath);
        Assert.Equal(ModernExplorer.Core.Viewer.PreviewMode.Archive, zipDoc.Mode);
        Assert.NotNull(zipDoc.ArchiveEntries);
        Assert.Single(zipDoc.ArchiveEntries!);
        Assert.Equal("ordner/readme.txt", zipDoc.ArchiveEntries![0].FullName);

        // 4. Directory / folder
        var dirDoc = await ModernExplorer.Core.Viewer.FileContentService.LoadPreviewAsync(_tempDir);
        Assert.Equal(ModernExplorer.Core.Viewer.PreviewMode.Directory, dirDoc.Mode);
        Assert.NotNull(dirDoc.DirectoryInfo);
        Assert.True(dirDoc.DirectoryInfo!.FileCount >= 3);
    }

    [Fact]
    public void LocalizationService_DefaultsToSystem_AndSupportsSwitchingLanguages()
    {
        var file = Path.Combine(_tempDir, "settings_lang.json");
        var store = new ModernExplorer.Core.Settings.SettingsStore(file);
        var vm = new SettingsViewModel(store);

        Assert.Equal("system", vm.Language);
        Assert.True(vm.CheckForUpdatesOnStartup);

        vm.Language = "en";
        Assert.Equal("en", ModernExplorer.Core.Localization.LocalizationService.Instance.EffectiveLanguage);
        Assert.Equal("Settings", ModernExplorer.Core.Localization.LocalizationService.T("Toolbar.Settings"));
        Assert.Equal("Copy", ModernExplorer.Core.Localization.LocalizationService.T("Fn.F5"));

        vm.Language = "es";
        Assert.Equal("es", ModernExplorer.Core.Localization.LocalizationService.Instance.EffectiveLanguage);
        Assert.Equal("Ajustes", ModernExplorer.Core.Localization.LocalizationService.T("Toolbar.Settings"));

        vm.Language = "de";
        Assert.Equal("de", ModernExplorer.Core.Localization.LocalizationService.Instance.EffectiveLanguage);
        Assert.Equal("Einstellungen", ModernExplorer.Core.Localization.LocalizationService.T("Toolbar.Settings"));

        vm.CheckForUpdatesOnStartup = false;

        var reloaded = new SettingsViewModel(store);
        Assert.Equal("de", reloaded.Language);
        Assert.False(reloaded.CheckForUpdatesOnStartup);

        reloaded.Language = "system";
    }

    [Fact]
    public void GitHubUpdateService_ParsesReleaseJson_ComparesVersions_AndSelectsBestAsset()
    {
        Assert.True(ModernExplorer.Core.Update.GitHubUpdateService.TryParseVersion("v1.2.0", out var v120));
        Assert.True(ModernExplorer.Core.Update.GitHubUpdateService.TryParseVersion("1.0.0", out var v100));
        Assert.True(ModernExplorer.Core.Update.GitHubUpdateService.TryParseVersion("v0.9.5", out var v095));

        Assert.True(ModernExplorer.Core.Update.GitHubUpdateService.IsNewerVersion(v100, v120));
        Assert.False(ModernExplorer.Core.Update.GitHubUpdateService.IsNewerVersion(v100, v100));
        Assert.False(ModernExplorer.Core.Update.GitHubUpdateService.IsNewerVersion(v100, v095));

        const string sampleReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "name": "Modern Explorer 1.2.0",
          "body": "Bug fixes and multi-language support",
          "html_url": "https://github.com/Skybeat/ModernExplorer/releases/tag/v1.2.0",
          "published_at": "2026-10-10T20:00:00Z",
          "assets": [
            {
              "name": "ModernExplorer-win-x64.zip",
              "browser_download_url": "https://github.com/Skybeat/ModernExplorer/releases/download/v1.2.0/ModernExplorer-win-x64.zip",
              "size": 10485760,
              "content_type": "application/zip"
            }
          ]
        }
        """;

        var info = ModernExplorer.Core.Update.GitHubUpdateService.ParseReleaseJson(sampleReleaseJson);
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 2, 0), info!.ParsedVersion);
        Assert.True(ModernExplorer.Core.Update.GitHubUpdateService.IsNewerVersion(v100, info.ParsedVersion));

        var bestAsset = ModernExplorer.Core.Update.GitHubUpdateService.SelectBestAsset(info);
        Assert.NotNull(bestAsset);
        Assert.Equal("ModernExplorer-win-x64.zip", bestAsset!.Name);
    }

    private sealed class NullShellUi : IShellUi, ModernExplorer.Core.Operations.IConflictResolver
    {
        public Task<ModernExplorer.Core.Operations.ConflictDecision> ResolveAsync(ModernExplorer.Core.Operations.ConflictInfo conflict, CancellationToken ct) =>
            Task.FromResult(new ModernExplorer.Core.Operations.ConflictDecision(ModernExplorer.Core.Operations.ConflictChoice.Skip, false));
        public Task<NewFileRequest?> ShowNewFileDialogAsync(string directory, IReadOnlyList<FileTemplate> templates, FileTemplate selected, Func<FileTemplate, string> suggestName, Func<string, string?> validate) => Task.FromResult<NewFileRequest?>(null);
        public Task<string?> ShowNewFolderDialogAsync(string directory, Func<string, string?> validate) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string title, string label, string initial, int selectLength, Func<string, string?>? validate = null) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, string primaryText, bool destructive = false) => Task.FromResult(true);
        public void ShowMessage(string title, string message) { }
        public void FocusFileList(PanelSide side) { }
        public void FocusAddressBar() { }
        public void FocusFilter() { }
        public void ToggleFullScreen() { }
        public void ShowCommandPalette() { }
        public void ShowHelp() { }
        public void OpenNewWindow(TabState tab) { }
        public void OpenViewer(string path) { }
        public void OpenEditor(string path) { }
        public void SetClipboardText(string text) { }
        public Task SetClipboardFilesAsync(IReadOnlyList<string> paths, bool cut) => Task.CompletedTask;
        public Task<(IReadOnlyList<string> Paths, bool Cut)?> GetClipboardFilesAsync() => Task.FromResult<(IReadOnlyList<string> Paths, bool Cut)?>(null);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

