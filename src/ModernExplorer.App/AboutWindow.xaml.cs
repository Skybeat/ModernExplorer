using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ModernExplorer.App.Dialogs;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Settings;
using ModernExplorer.Core.Update;
using ModernExplorer.Core.ViewModels;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace ModernExplorer.App;

public sealed partial class AboutWindow : Window
{
    private readonly MainWindow _owner;
    public AboutViewModel ViewModel { get; }

    public AboutWindow(MainWindow owner, GitHubUpdateService updateService)
    {
        _owner = owner;
        ViewModel = new AboutViewModel(updateService);

        InitializeComponent();
        ConfigureWindowAndCenterOverOwner();

        ApplyTheme(_owner.ViewModel.Settings.Theme);
        ApplyLocalization();
        UpdateVersionAndStatusUi();

        _owner.ViewModel.Settings.ThemeChanged += OnThemeChanged;
        _owner.ViewModel.Settings.FontSizesChanged += OnFontSizesChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        ViewModel.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateVersionAndStatusUi);

        RootGrid.Loaded += async (_, _) =>
        {
            ApplyTheme(_owner.ViewModel.Settings.Theme);
            ApplyLocalization();
            ApplyAboutFontScaling();
            await ViewModel.CheckForUpdateAsync();
        };

        Closed += (_, _) =>
        {
            _owner.ViewModel.Settings.ThemeChanged -= OnThemeChanged;
            _owner.ViewModel.Settings.FontSizesChanged -= OnFontSizesChanged;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            ViewModel.Dispose();
        };
    }

    private void OnFontSizesChanged(object? sender, EventArgs e)
    {
        ApplyAboutFontScaling();
    }

    private void ApplyAboutFontScaling()
    {
        double targetFont = _owner.ViewModel.Settings.GetChildWindowFontSize(ChildWindowFontTarget.AboutWindow);
        ShellDialogService.ApplyFontScalingToTree(RootGrid, targetFont);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyTheme(_owner.ViewModel.Settings.Theme);
        UpdateVersionAndStatusUi();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        UpdateVersionAndStatusUi();
    }

    public void ApplyTheme(AppTheme theme)
    {
        WindowChromeHelper.ApplyCustomTitleBarAndTheme(this, RootGrid, AboutTitleBarDragRegion, theme);
    }

    private void ApplyLocalization()
    {
        Title = ViewModel.WindowTitle;
        if (WindowTitleLabel is not null) WindowTitleLabel.Text = ViewModel.WindowTitle;
        if (AppHeadingLabel is not null) AppHeadingLabel.Text = ViewModel.Heading;
        if (AppSubtitleLabel is not null) AppSubtitleLabel.Text = ViewModel.Subtitle;
        if (AppDescriptionLabel is not null) AppDescriptionLabel.Text = ViewModel.Description;
        if (RepositoryHeaderLabel is not null) RepositoryHeaderLabel.Text = ViewModel.RepositoryLabel;
        if (RepositoryLinkButton is not null)
        {
            RepositoryLinkButton.Content = ViewModel.RepositoryUrl;
            ToolTipService.SetToolTip(RepositoryLinkButton, ViewModel.OpenRepoButtonText);
        }
        if (VersionSectionTitleLabel is not null) VersionSectionTitleLabel.Text = ViewModel.VersionSectionTitle;
        if (InstalledVersionTitleLabel is not null) InstalledVersionTitleLabel.Text = ViewModel.InstalledVersionLabel;
        if (LatestVersionTitleLabel is not null) LatestVersionTitleLabel.Text = ViewModel.LatestVersionLabel;
        if (UpdateNowButton is not null) UpdateNowButton.Content = $"🚀 {ViewModel.UpdateNowButtonText}";
        if (CheckAgainButton is not null) CheckAgainButton.Content = ViewModel.CheckAgainButtonText;
        if (CloseAboutButton is not null) CloseAboutButton.Content = ViewModel.CloseButtonText;
    }

    private void UpdateVersionAndStatusUi()
    {
        if (InstalledVersionValueLabel is not null)
            InstalledVersionValueLabel.Text = ViewModel.InstalledVersion;

        if (LatestVersionValueLabel is not null)
        {
            LatestVersionValueLabel.Text = ViewModel.LatestVersionText;
            LatestVersionValueLabel.Foreground = ViewModel.IsUpdateAvailable
                ? new SolidColorBrush(Color.FromArgb(255, 56, 189, 248))
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        if (UpdateStatusText is not null)
            UpdateStatusText.Text = ViewModel.StatusMessage;

        if (UpdateNowButton is not null)
        {
            UpdateNowButton.Visibility = ViewModel.ShowUpdateButton ? Visibility.Visible : Visibility.Collapsed;
            UpdateNowButton.IsEnabled = !ViewModel.IsChecking;
        }

        if (CheckAgainButton is not null)
            CheckAgainButton.IsEnabled = !ViewModel.IsChecking;
    }

    private void ConfigureWindowAndCenterOverOwner()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AboutTitleBarDragRegion);
            SystemBackdrop = new MicaBackdrop();

            if (AppWindow is { } aw)
            {
                if (aw.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                }

                const int w = 580;
                const int h = 500;

                if (_owner.AppWindow is { } ownerAw)
                {
                    var pos = ownerAw.Position;
                    var size = ownerAw.Size;
                    int x = pos.X + (size.Width - w) / 2;
                    int y = pos.Y + (size.Height - h) / 2;
                    aw.MoveAndResize(new RectInt32(x, y, w, h));
                }
                else
                {
                    aw.Resize(new SizeInt32(w, h));
                }
            }
        }
        catch { }
    }

    private void OnOpenRepositoryClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenRepositoryInBrowser();
    }

    private async void OnCheckAgainClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.CheckForUpdateAsync();
    }

    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FoundRelease is not { } release) return;

        UpdateNowButton.IsEnabled = false;
        _owner.ViewModel.StatusNotification = LocalizationService.Format("Update.DownloadingMsg", release.TagName);
        UpdateStatusText.Text = LocalizationService.Format("Update.DownloadingMsg", release.TagName);

        try
        {
            bool shouldExit = await ViewModel.InstallFoundUpdateAsync();
            if (shouldExit)
            {
                Close();
                _owner.PrepareForUpdateAndExit();
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = ex.Message;
            UpdateNowButton.IsEnabled = true;
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

