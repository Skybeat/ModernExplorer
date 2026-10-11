using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Update;

namespace ModernExplorer.Core.ViewModels;

/// <summary>
/// ViewModel for the About ("Über") window, exposing application info, the GitHub repository link,
/// currently installed version, live GitHub release check status, found update version, and one-click update action.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject, IDisposable
{
    private readonly GitHubUpdateService _updateService;

    public string RepositoryUrl => GitHubUpdateService.RepositoryWebUrl;

    [ObservableProperty]
    private string _installedVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLatestVersionText))]
    private string _latestVersionText = "—";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton), nameof(ReleaseNotesText), nameof(HasReleaseNotes))]
    private GitHubReleaseInfo? _foundRelease;

    public bool ShowUpdateButton => IsUpdateAvailable && FoundRelease is not null;
    public bool HasLatestVersionText => !string.IsNullOrWhiteSpace(LatestVersionText) && LatestVersionText != "—";
    public string ReleaseNotesText => GitHubUpdateService.FormatReleaseNotesForDisplay(FoundRelease?.Body);
    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotesText);

    public string WindowTitle => LocalizationService.T("About.WindowTitle");
    public string Heading => LocalizationService.T("About.Heading");
    public string Subtitle => LocalizationService.T("About.Subtitle");
    public string Description => LocalizationService.T("About.Description");
    public string RepositoryLabel => LocalizationService.T("About.RepositoryLabel");
    public string OpenRepoButtonText => LocalizationService.T("About.OpenRepoButton");
    public string VersionSectionTitle => LocalizationService.T("About.VersionSectionTitle");
    public string InstalledVersionLabel => LocalizationService.T("About.InstalledVersion");
    public string LatestVersionLabel => LocalizationService.T("About.LatestVersion");
    public string ReleaseNotesHeaderLabel => LocalizationService.T("Update.ReleaseNotes");
    public string OpenReleaseInBrowserText => LocalizationService.T("Update.OpenGitHub");
    public string UpdateNowButtonText => LocalizationService.T("About.UpdateNowButton");
    public string CheckAgainButtonText => LocalizationService.T("About.CheckAgainButton");
    public string CloseButtonText => $"{LocalizationService.T("Settings.Close")} (Esc)";

    public AboutViewModel(GitHubUpdateService? updateService = null, Version? currentVersionOverride = null)
    {
        _updateService = updateService ?? new GitHubUpdateService();
        var ver = currentVersionOverride ?? GitHubUpdateService.GetCurrentVersion();
        _installedVersion = $"{ver.Major}.{Math.Max(0, ver.Minor)}.{Math.Max(0, ver.Build)}";
        _statusMessage = LocalizationService.T("About.CheckingUpdates");

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RepositoryLabel));
        OnPropertyChanged(nameof(OpenRepoButtonText));
        OnPropertyChanged(nameof(VersionSectionTitle));
        OnPropertyChanged(nameof(InstalledVersionLabel));
        OnPropertyChanged(nameof(LatestVersionLabel));
        OnPropertyChanged(nameof(ReleaseNotesHeaderLabel));
        OnPropertyChanged(nameof(OpenReleaseInBrowserText));
        OnPropertyChanged(nameof(UpdateNowButtonText));
        OnPropertyChanged(nameof(CheckAgainButtonText));
        OnPropertyChanged(nameof(CloseButtonText));

        if (IsChecking)
        {
            StatusMessage = LocalizationService.T("About.CheckingUpdates");
        }
        else if (IsUpdateAvailable && FoundRelease is not null)
        {
            var foundVer = FormatVersionNumber(FoundRelease.ParsedVersion);
            StatusMessage = LocalizationService.Format("About.UpdateFoundBadge", foundVer);
        }
        else if (FoundRelease is not null)
        {
            StatusMessage = LocalizationService.T("About.UpToDate");
        }
    }

    public async Task CheckForUpdateAsync(Version? currentVersionOverride = null, CancellationToken ct = default)
    {
        IsChecking = true;
        StatusMessage = LocalizationService.T("About.CheckingUpdates");

        try
        {
            Version? overrideVer = currentVersionOverride;
            if (overrideVer is null && GitHubUpdateService.TryParseVersion(InstalledVersion, out var parsedInstalled))
                overrideVer = parsedInstalled;

            var result = await _updateService.CheckForUpdateAsync(overrideVer, ct);
            ApplyCheckResult(result);
        }
        catch (Exception ex)
        {
            IsUpdateAvailable = false;
            FoundRelease = null;
            LatestVersionText = InstalledVersion;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsChecking = false;
        }
    }

    public void ApplyCheckResult(UpdateCheckResult result)
    {
        IsChecking = false;
        InstalledVersion = FormatVersionNumber(result.CurrentVersion);

        if (result.LatestRelease is { } release)
        {
            FoundRelease = release;
            var latestVerStr = FormatVersionNumber(release.ParsedVersion);
            LatestVersionText = latestVerStr;
            IsUpdateAvailable = result.IsUpdateAvailable;

            StatusMessage = result.IsUpdateAvailable
                ? LocalizationService.Format("About.UpdateFoundBadge", latestVerStr)
                : LocalizationService.T("About.UpToDate");
        }
        else
        {
            FoundRelease = null;
            LatestVersionText = InstalledVersion;
            IsUpdateAvailable = false;
            StatusMessage = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : LocalizationService.T("About.UpToDate");
        }
    }

    public void OpenRepositoryInBrowser() =>
        GitHubUpdateService.OpenUrlInBrowser(RepositoryUrl);

    public void OpenFoundReleaseInBrowser() =>
        GitHubUpdateService.OpenUrlInBrowser(FoundRelease?.HtmlUrl ?? GitHubUpdateService.RepositoryReleasesWebUrl);

    public async Task<bool> InstallFoundUpdateAsync(CancellationToken ct = default)
    {
        if (FoundRelease is null)
            return false;

        return await _updateService.DownloadAndInstallUpdateAsync(FoundRelease, ct: ct);
    }

    public static string FormatVersionNumber(Version v) =>
        $"{Math.Max(0, v.Major)}.{Math.Max(0, v.Minor)}.{Math.Max(0, v.Build)}";

    public void Dispose()
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
}

