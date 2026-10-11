using System.IO;
using System.Net;
using System.Text;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.Update;
using ModernExplorer.Core.ViewModels;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ModernExplorer.Core.Tests;

public sealed class AboutAndLocalizationTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    [Fact]
    public async Task AboutViewModel_ShowsInstalledAndFoundUpdateVersion_AndEnablesUpdateButton()
    {
        LocalizationService.Instance.SetLanguage("de");

        const string json = """
        {
          "tag_name": "v1.0.2",
          "name": "Modern Explorer v1.0.2",
          "html_url": "https://github.com/Skybeat/ModernExplorer/releases/tag/v1.0.2",
          "body": "Bug fixes and improvements",
          "assets": [
            {
              "name": "ModernExplorer-Setup-win-x64.exe",
              "browser_download_url": "https://github.com/Skybeat/ModernExplorer/releases/download/v1.0.2/ModernExplorer-Setup-win-x64.exe",
              "size": 1048576
            }
          ]
        }
        """;

        using var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        var service = new GitHubUpdateService(http);
        using var vm = new AboutViewModel(service, new Version(1, 0, 1));

        Assert.Equal("https://github.com/Skybeat/ModernExplorer", vm.RepositoryUrl);
        Assert.Equal("1.0.1", vm.InstalledVersion);

        await vm.CheckForUpdateAsync(new Version(1, 0, 1));

        Assert.True(vm.IsUpdateAvailable);
        Assert.True(vm.ShowUpdateButton);
        Assert.Equal("1.0.2", vm.LatestVersionText);
        Assert.Contains("1.0.2", vm.StatusMessage);

        // Switching language dynamically updates StatusMessage and button texts
        LocalizationService.Instance.SetLanguage("en");
        Assert.Contains("1.0.2", vm.StatusMessage);
        Assert.Equal("Update Now", vm.UpdateNowButtonText);
        Assert.Equal("1.0.2", vm.LatestVersionText);
    }

    [Fact]
    public async Task AboutViewModel_WhenUpToDate_HidesUpdateButton()
    {
        LocalizationService.Instance.SetLanguage("en");

        const string json = """
        {
          "tag_name": "v1.0.1",
          "name": "Modern Explorer v1.0.1",
          "html_url": "https://github.com/Skybeat/ModernExplorer/releases/tag/v1.0.1",
          "body": "Release 1.0.1",
          "assets": []
        }
        """;

        using var http = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        var service = new GitHubUpdateService(http);
        using var vm = new AboutViewModel(service, new Version(1, 0, 1));

        await vm.CheckForUpdateAsync(new Version(1, 0, 1));

        Assert.False(vm.IsUpdateAvailable);
        Assert.False(vm.ShowUpdateButton);
        Assert.Equal("1.0.1", vm.LatestVersionText);
        Assert.Equal(LocalizationService.T("About.UpToDate"), vm.StatusMessage);
    }

    [Fact]
    public void FileItemViewModel_And_SettingsViewModel_UpdateOnLanguageSwitch()
    {
        LocalizationService.Instance.SetLanguage("de");

        var folderItem = new FileItemViewModel(new FileEntry(
            Name: "TestFolder",
            FullPath: @"C:\TestFolder",
            IsDirectory: true,
            Size: 0,
            LastWriteTime: DateTime.Now,
            Attributes: FileAttributes.Directory));

        var fileWithoutExt = new FileItemViewModel(new FileEntry(
            Name: "Makefile",
            FullPath: @"C:\Makefile",
            IsDirectory: false,
            Size: 128,
            LastWriteTime: DateTime.Now,
            Attributes: FileAttributes.Normal));

        Assert.Equal("<Ordner>", folderItem.SizeText);
        Assert.Equal("Ordner", folderItem.TypeText);
        Assert.Equal("Datei", fileWithoutExt.TypeText);

        var tempSettingsPath = Path.Combine(Path.GetTempPath(), $"modernexplorer-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settingsVm = new SettingsViewModel(new SettingsStore(tempSettingsPath));
            settingsVm.UpdateMainWindowBounds(1480, 900, isMaximized: true);
            settingsVm.Language = "de";
            Assert.Contains("Dunkel", settingsVm.ThemeDisplayName);
            Assert.Contains("Maximiert", settingsVm.MainWindowBoundsSummary);

            // Switch to English
            settingsVm.Language = "en";
            folderItem.RefreshLocalization();
            fileWithoutExt.RefreshLocalization();

            Assert.Equal("<Folder>", folderItem.SizeText);
            Assert.Equal("Folder", folderItem.TypeText);
            Assert.Equal("File", fileWithoutExt.TypeText);
            Assert.Contains("Dark", settingsVm.ThemeDisplayName);
            Assert.Contains("Maximized", settingsVm.MainWindowBoundsSummary);
        }
        finally
        {
            if (File.Exists(tempSettingsPath))
                File.Delete(tempSettingsPath);
        }
    }

    [Theory]
    [InlineData("de", "Über", "Über Modern Explorer", "Jetzt aktualisieren")]
    [InlineData("en", "About", "About Modern Explorer", "Update Now")]
    [InlineData("es", "Acerca de", "Acerca de Modern Explorer", "Actualizar ahora")]
    [InlineData("fr", "À propos", "À propos de Modern Explorer", "Mettre à jour maintenant")]
    public void LocalizationService_HasTranslatedAboutAndSettingsKeys(
        string code,
        string expectedToolbarAbout,
        string expectedWindowTitle,
        string expectedUpdateBtn)
    {
        LocalizationService.Instance.SetLanguage(code);
        Assert.Equal(expectedToolbarAbout, LocalizationService.T("Toolbar.About"));
        Assert.Equal(expectedWindowTitle, LocalizationService.T("About.WindowTitle"));
        Assert.Equal(expectedUpdateBtn, LocalizationService.T("About.UpdateNowButton"));
        Assert.NotEqual("Settings.Nav.Colors.Title", LocalizationService.T("Settings.Nav.Colors.Title"));
        Assert.NotEqual("Menu.OpenFolder", LocalizationService.T("Menu.OpenFolder"));
        Assert.NotEqual("Menu.Tab.New", LocalizationService.T("Menu.Tab.New"));
    }

    [Fact]
    public void GitHubUpdateService_CanWriteToDirectory_And_BuildSelfUpdatePowerShellScript_CopiesFilesCorrectly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"me_update_test_{Guid.NewGuid():N}");
        var stagingDir = Path.Combine(tempRoot, "staging");
        var targetDir = Path.Combine(tempRoot, "target");

        try
        {
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(Path.Combine(stagingDir, "Assets"));
            Directory.CreateDirectory(Path.Combine(targetDir, "Assets"));

            File.WriteAllText(Path.Combine(targetDir, "ModernExplorer.runtimeconfig.json"), "{\"old\":true}");
            File.WriteAllText(Path.Combine(targetDir, "Assets", "AppIcon.ico"), "old-icon");
            File.WriteAllText(Path.Combine(stagingDir, "ModernExplorer.runtimeconfig.json"), "{\"new\":true}");
            File.WriteAllText(Path.Combine(stagingDir, "Assets", "AppIcon.ico"), "new-icon");

            Assert.True(GitHubUpdateService.CanWriteToDirectory(targetDir));
            Assert.False(GitHubUpdateService.CanWriteToDirectory(Path.Combine(tempRoot, "non_existent_folder")));

            var exePath = Path.Combine(targetDir, "ModernExplorer.exe");
            var script = GitHubUpdateService.BuildSelfUpdatePowerShellScript(999999, stagingDir, targetDir, exePath);

            Assert.Contains($"$exePath = \"{exePath}\"", script);
            Assert.Contains($"$targetAppDir = \"{targetDir}\"", script);
            Assert.Contains("New-Object System.Windows.Forms.Form", script);
            Assert.Contains("New-Object System.Windows.Forms.ProgressBar", script);
            Assert.Contains("function Set-UpdateProgress", script);
            Assert.Contains("for ($attempt = 1; $attempt -le 15; $attempt++)", script);
            Assert.Contains("Get-ChildItem -LiteralPath $stagingDir -Recurse -Force", script);
            Assert.Contains("Copy-Item -LiteralPath $item.FullName -Destination $destPath -Force -ErrorAction Stop", script);
            Assert.Contains("icacls $dir /grant '*S-1-5-32-545:(OI)(CI)M'", script);
            Assert.Contains("-Verb RunAs", script);
            Assert.Contains("Start-Process -FilePath \"$exePath\" -WorkingDirectory \"$targetAppDir\"", script);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void TabViewModel_AllEightWindowsExplorerViewModes_CyclePersistAndLocalizeCorrectly()
    {
        var watcherHub = new ModernExplorer.Core.FileSystem.WatcherHub();
        var dispatcher = new ImmediateTestDispatcher();
        var tab = new TabViewModel(watcherHub, dispatcher);

        // Default view mode is Details
        Assert.Equal(ModernExplorer.Core.Session.FileViewMode.Details, tab.ViewMode);
        Assert.True(tab.IsDetailsView);
        Assert.Equal(8, TabViewModel.AllViewModes.Count);

        // Verify German display names match Windows Explorer
        LocalizationService.Instance.SetLanguage("de");
        Assert.Equal("Extra große Symbole", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.ExtraLargeIcons));
        Assert.Equal("Große Symbole", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.LargeIcons));
        Assert.Equal("Mittelgroße Symbole", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.MediumIcons));
        Assert.Equal("Kleine Symbole", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.SmallIcons));
        Assert.Equal("Liste", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.List));
        Assert.Equal("Details", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.Details));
        Assert.Equal("Kacheln", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.Tiles));
        Assert.Equal("Inhalt", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.Content));

        // Verify English display names match Windows Explorer
        LocalizationService.Instance.SetLanguage("en");
        Assert.Equal("Extra large icons", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.ExtraLargeIcons));
        Assert.Equal("Large icons", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.LargeIcons));
        Assert.Equal("Medium icons", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.MediumIcons));
        Assert.Equal("Small icons", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.SmallIcons));
        Assert.Equal("List", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.List));
        Assert.Equal("Details", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.Details));
        Assert.Equal("Tiles", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.Tiles));
        Assert.Equal("Content", TabViewModel.GetViewModeDisplayName(ModernExplorer.Core.Session.FileViewMode.Content));

        // Verify SetViewMode, CycleViewMode, and ToState() persistence
        tab.SetViewMode(ModernExplorer.Core.Session.FileViewMode.ExtraLargeIcons);
        Assert.False(tab.IsDetailsView);
        Assert.Equal("Ctrl+Shift+1", TabViewModel.GetViewModeShortcutText(tab.ViewMode));

        var state = tab.ToState();
        Assert.Equal(ModernExplorer.Core.Session.FileViewMode.ExtraLargeIcons, state.ViewMode);

        // Cycle through all 8 modes and verify we return to ExtraLargeIcons
        for (int i = 0; i < 8; i++)
        {
            tab.CycleViewMode();
        }
        Assert.Equal(ModernExplorer.Core.Session.FileViewMode.ExtraLargeIcons, tab.ViewMode);
    }

    [Fact]
    public void GitHubUpdateService_SemanticVersioning_CalculatesNextVersionAndDeterminesBumpCorrectly()
    {
        var baseVer = new Version(1, 0, 3);

        Assert.Equal(new Version(1, 0, 4), GitHubUpdateService.CalculateNextSemanticVersion(baseVer, SemVerBumpType.Patch));
        Assert.Equal(new Version(1, 1, 0), GitHubUpdateService.CalculateNextSemanticVersion(baseVer, SemVerBumpType.Minor));
        Assert.Equal(new Version(2, 0, 0), GitHubUpdateService.CalculateNextSemanticVersion(baseVer, SemVerBumpType.Major));

        Assert.Equal(SemVerBumpType.Patch, GitHubUpdateService.DetermineSemanticVersionBump(["fix: resolve update bug", "chore: cleanup"]));
        Assert.Equal(SemVerBumpType.Minor, GitHubUpdateService.DetermineSemanticVersionBump(["fix: minor tweak", "feat: add 8 Windows Explorer view modes"]));
        Assert.Equal(SemVerBumpType.Major, GitHubUpdateService.DetermineSemanticVersionBump(["feat: new view mode", "feat!: redesign session format"]));
        Assert.Equal(SemVerBumpType.Major, GitHubUpdateService.DetermineSemanticVersionBump(["refactor: update API\n\nBREAKING CHANGE: session v2"]));
    }

    [Fact]
    public void LocalizationService_AllChildWindows_Treemap_And_SettingsCards_AreLocalizedInEnglish()
    {
        var loc = LocalizationService.Instance;
        string prevLang = loc.EffectiveLanguage;
        try
        {
            loc.SetLanguage("en");

            Assert.Equal("Disk Space Analyzer & Graphical Treemap (WinDirStat / Ctrl+Shift+W)", LocalizationService.T("Treemap.WindowTitle"));
            Assert.Equal("Multi-Rename Tool (Ctrl+M)", LocalizationService.T("BatchRename.HeaderTitle"));
            Assert.Equal("Synchronize Directories (Ctrl+Shift+S)", LocalizationService.T("DirSync.WindowTitle"));
            Assert.Equal("Recursive Deep Search (Alt+F7)", LocalizationService.T("DeepSearch.WindowTitle"));
            Assert.Equal("Duplicate Finder — Locate Identical Files", LocalizationService.T("Duplicates.WindowTitle"));
            Assert.Equal("FTP / FTPS / SFTP Connection Manager", LocalizationService.T("Remote.WindowTitle"));
            Assert.Equal("Pack to Archive (Alt+F5)", LocalizationService.T("Archive.WindowTitle"));
            Assert.Contains("Quick Viewer (F3)", LocalizationService.Format("QuickView.WindowTitle", "test.txt"));
            Assert.Contains("Editor (F4)", LocalizationService.Format("Editor.WindowTitle", "test.txt"));
            Assert.Contains("Drag to resize the width", LocalizationService.T("Treemap.SplitterHorizontalTip"));
            Assert.Contains("Drag to resize the height", LocalizationService.T("Treemap.SplitterVerticalTip"));
            Assert.Contains("File category colors in Disk Space Analyzer", LocalizationService.T("Settings.Colors.Treemap.Title"));
            Assert.Contains("Child Windows", LocalizationService.T("Settings.Fonts.ChildWinCardTitle"));
        }
        finally
        {
            loc.SetLanguage(prevLang);
        }
    }

    private sealed class ImmediateTestDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;
        public void Post(Action action) => action();
    }
}



