using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.Archive;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Remote;
using ModernExplorer.Core.Session;

namespace ModernExplorer.Core.ViewModels;

public sealed record BreadcrumbSegment(string Label, string FullPath)
{
    public SettingsViewModel Settings => SettingsViewModel.Current;
}

public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private readonly WatcherHub _watcherHub;
    private readonly IUiDispatcher _dispatcher;
    private readonly List<FileItemViewModel> _allItems = [];
    private readonly List<FileEntry> _customSearchResults = [];
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private CancellationTokenSource? _loadCts;
    private IDisposable? _watcherSub;
    private bool _isWatcherActive;

    public Guid Id { get; }
    public SettingsViewModel Settings => SettingsViewModel.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DisplayTitle), nameof(CanGoUp), nameof(IsVirtualArchive), nameof(IsRemoteConnection), nameof(IsSearchResultsTab))]
    private string _currentPath = "";

    [ObservableProperty]
    private string _addressText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _isPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _isLocked;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsView), nameof(ViewModeDisplayName), nameof(ViewModeIconGlyph), nameof(ViewModeButtonLabel))]
    private FileViewMode _viewMode = FileViewMode.Details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _isBranchView;

    [ObservableProperty]
    private bool _isTreeSizeActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitRepository))]
    private string _gitSummaryBadge = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BookmarkGlyph))]
    private bool _isBookmarked;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _hasPathWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortColumnName), nameof(SortIndicatorName), nameof(SortIndicatorSize), nameof(SortIndicatorModified), nameof(SortIndicatorType))]
    private SortSpec _sort = new();

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private FileItemViewModel? _focusedItem;

    [ObservableProperty]
    private string _statusSummary = "";

    [ObservableProperty]
    private string _driveSummary = "";

    [ObservableProperty]
    private string? _errorBanner;

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    public bool IsVirtualArchive => ArchiveService.IsVirtualArchivePath(CurrentPath);
    public bool IsRemoteConnection => RemoteFileSystemService.IsRemoteUri(CurrentPath);
    public bool IsSearchResultsTab => CurrentPath.StartsWith("search://", StringComparison.OrdinalIgnoreCase);
    public bool HasGitRepository => !string.IsNullOrEmpty(GitSummaryBadge);
    public string BookmarkGlyph => IsBookmarked ? "★" : "☆";

    public static IReadOnlyList<FileViewMode> AllViewModes { get; } =
    [
        FileViewMode.ExtraLargeIcons,
        FileViewMode.LargeIcons,
        FileViewMode.MediumIcons,
        FileViewMode.SmallIcons,
        FileViewMode.List,
        FileViewMode.Details,
        FileViewMode.Tiles,
        FileViewMode.Content,
    ];

    public bool IsDetailsView => ViewMode == FileViewMode.Details;
    public string ViewModeIconGlyph => GetViewModeGlyph(ViewMode);
    public string ViewModeDisplayName => GetViewModeDisplayName(ViewMode);
    public string ViewModeButtonLabel => $"{ViewModeIconGlyph} {ViewModeDisplayName} ▾";

    public static string GetViewModeGlyph(FileViewMode mode) => mode switch
    {
        FileViewMode.ExtraLargeIcons => "🖼",
        FileViewMode.LargeIcons => "⊞",
        FileViewMode.MediumIcons => "⊡",
        FileViewMode.SmallIcons => "∷",
        FileViewMode.List => "≣",
        FileViewMode.Details => "☰",
        FileViewMode.Tiles => "▦",
        FileViewMode.Content => "▤",
        _ => "☰",
    };

    public static string GetViewModeDisplayName(FileViewMode mode) => mode switch
    {
        FileViewMode.ExtraLargeIcons => LocalizationService.T("ViewMode.ExtraLargeIcons"),
        FileViewMode.LargeIcons => LocalizationService.T("ViewMode.LargeIcons"),
        FileViewMode.MediumIcons => LocalizationService.T("ViewMode.MediumIcons"),
        FileViewMode.SmallIcons => LocalizationService.T("ViewMode.SmallIcons"),
        FileViewMode.List => LocalizationService.T("ViewMode.List"),
        FileViewMode.Details => LocalizationService.T("ViewMode.Details"),
        FileViewMode.Tiles => LocalizationService.T("ViewMode.Tiles"),
        FileViewMode.Content => LocalizationService.T("ViewMode.Content"),
        _ => LocalizationService.T("ViewMode.Details"),
    };

    public static string GetViewModeShortcutText(FileViewMode mode) => mode switch
    {
        FileViewMode.ExtraLargeIcons => "Ctrl+Shift+1",
        FileViewMode.LargeIcons => "Ctrl+Shift+2",
        FileViewMode.MediumIcons => "Ctrl+Shift+3",
        FileViewMode.SmallIcons => "Ctrl+Shift+4",
        FileViewMode.List => "Ctrl+Shift+5",
        FileViewMode.Details => "Ctrl+Shift+6",
        FileViewMode.Tiles => "Ctrl+Shift+7",
        FileViewMode.Content => "Ctrl+Shift+8",
        _ => "",
    };

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPath)) return false;
            if (IsVirtualArchive) return !string.IsNullOrEmpty(ArchiveService.GetParentPathForVirtualArchive(CurrentPath));
            if (IsRemoteConnection) return !string.IsNullOrEmpty(RemoteFileSystemService.GetParentRemoteUri(CurrentPath));
            if (IsSearchResultsTab) return false;
            try { return Directory.GetParent(CurrentPath) is not null; }
            catch { return false; }
        }
    }

    public string Title
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPath)) return "Tab";
            if (IsSearchResultsTab) return "🔍 " + CurrentPath["search://".Length..];
            if (IsVirtualArchive && ArchiveService.TryParseVirtualArchivePath(CurrentPath, out var arch, out var inner))
            {
                return string.IsNullOrEmpty(inner)
                    ? "📦 " + Path.GetFileName(arch)
                    : "📦 " + Path.GetFileName(inner);
            }
            if (IsRemoteConnection && RemoteFileSystemService.TryParseRemoteUri(CurrentPath, out var proto, out var host, out _, out _, out var rPath))
            {
                string leaf = rPath == "/" ? host : $"{host}:{Path.GetFileName(rPath)}";
                return $"🌐 {proto.ToString().ToUpperInvariant()} {leaf}";
            }
            var trimmed = CurrentPath.TrimEnd('\\');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    public string DisplayTitle
    {
        get
        {
            var prefix = (IsPinned ? "📌 " : "") + (IsLocked ? "🔒 " : "") + (IsBranchView ? "🌿 " : "") + (HasPathWarning ? "⚠ " : "");
            return prefix + Title;
        }
    }

    public string SortColumnName => Sort.Column.ToString();
    public string SortIndicatorName => Sort.Column == SortColumn.Name ? (Sort.Descending ? "▼" : "▲") : "";
    public string SortIndicatorSize => Sort.Column == SortColumn.Size ? (Sort.Descending ? "▼" : "▲") : "";
    public string SortIndicatorModified => Sort.Column == SortColumn.Modified ? (Sort.Descending ? "▼" : "▲") : "";
    public string SortIndicatorType => Sort.Column == SortColumn.Type ? (Sort.Descending ? "▼" : "▲") : "";

    /// <summary>Raised when a locked tab attempts to navigate to a different folder.</summary>
    public event Action<string>? RequestOpenInNewTab;

    /// <summary>Raised on state changes for session persistence.</summary>
    public event EventHandler? StateChanged;

    public TabViewModel(WatcherHub watcherHub, IUiDispatcher dispatcher, TabState? state = null)
    {
        _watcherHub = watcherHub;
        _dispatcher = dispatcher;
        Id = state?.Id ?? Guid.NewGuid();
        if (state is not null)
        {
            IsPinned = state.Pinned;
            IsLocked = state.Locked;
            ShowHidden = state.ShowHidden;
            ViewMode = Enum.IsDefined(typeof(FileViewMode), state.ViewMode) ? state.ViewMode : FileViewMode.Details;
            Sort = state.Sort;
            Filter = state.Filter ?? "";
            foreach (var b in Enumerable.Reverse(state.Back)) _back.Push(b);
            foreach (var f in Enumerable.Reverse(state.Forward)) _forward.Push(f);
        }
        else
        {
            ShowHidden = SettingsViewModel.Current.ShowHiddenFilesByDefault;
            ViewMode = SettingsViewModel.Current.DefaultViewMode;
            Sort = new SortSpec
            {
                Column = SortColumn.Name,
                Descending = false,
                FoldersFirst = SettingsViewModel.Current.SortFoldersFirst,
            };
        }

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        ModernExplorer.Core.Features.FileTagService.TagsChanged += OnTagsChanged;
    }

    private void OnTagsChanged(object? sender, EventArgs e)
    {
        _dispatcher.Post(() =>
        {
            foreach (var item in _allItems)
                item.RefreshTagState();
            foreach (var item in Items)
                item.RefreshTagState();

            if (!string.IsNullOrWhiteSpace(Filter) &&
                (Filter.Trim().StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ||
                 Filter.Trim().StartsWith("note:", StringComparison.OrdinalIgnoreCase)))
            {
                RebuildVisibleList(FocusedItem?.Name);
            }
        });
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ViewModeDisplayName));
        OnPropertyChanged(nameof(ViewModeButtonLabel));
        UpdateStatusSummary();
        UpdateDriveSummary(CurrentPath);
        foreach (var item in _allItems)
        {
            item.RefreshLocalization();
            item.RefreshTagState();
        }
        foreach (var item in Items)
        {
            item.RefreshLocalization();
            item.RefreshTagState();
        }
    }

    partial void OnViewModeChanged(FileViewMode value)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetViewMode(FileViewMode mode)
    {
        if (Enum.IsDefined(typeof(FileViewMode), mode))
            ViewMode = mode;
    }

    public void CycleViewMode(bool forward = true)
    {
        var list = AllViewModes;
        int idx = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == ViewMode) { idx = i; break; }
        }
        if (idx < 0) idx = 5; // Details
        int nextIdx = forward
            ? (idx + 1) % list.Count
            : (idx - 1 + list.Count) % list.Count;
        ViewMode = list[nextIdx];
    }

    partial void OnFocusedItemChanged(FileItemViewModel? oldValue, FileItemViewModel? newValue)
    {
        if (newValue is null && Items.Count > 0 && !IsLoading && oldValue is not null && Items.Contains(oldValue))
        {
            FocusedItem = oldValue;
            return;
        }

        if (oldValue is not null) oldValue.IsFocused = false;
        if (newValue is not null) newValue.IsFocused = true;
        if (!IsLoading)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnFilterChanged(string value)
    {
        RebuildVisibleList(FocusedItem?.Name);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsPinnedChanged(bool value) => StateChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsLockedChanged(bool value) => StateChanged?.Invoke(this, EventArgs.Empty);

    public async Task NavigateAsync(string rawPath, bool addToHistory = true, string? selectName = null, bool bypassLock = false)
    {
        var resolved = ResolveSpecialAndFallback(rawPath, out var warning);
        if (resolved is null)
        {
            ErrorBanner = LocalizationService.Format("Nav.PathNotFound", rawPath);
            return;
        }

        // Locked tab: navigating to a different folder opens a new tab instead
        if (IsLocked && !bypassLock && !string.IsNullOrEmpty(CurrentPath) &&
            !string.Equals(CurrentPath, resolved, StringComparison.OrdinalIgnoreCase))
        {
            RequestOpenInNewTab?.Invoke(resolved);
            return;
        }

        if (addToHistory && !string.IsNullOrEmpty(CurrentPath) &&
            !string.Equals(CurrentPath, resolved, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(CurrentPath);
            _forward.Clear();
        }

        HasPathWarning = warning;
        ErrorBanner = warning ? LocalizationService.Format("Nav.FallbackWarning", resolved) : null;
        CurrentPath = resolved;
        AddressText = resolved;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        UpdateBreadcrumbs(resolved);
        UpdateDriveSummary(resolved);
        RebindWatcher();

        await ReloadAsync(selectName);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReloadAsync(string? selectName = null)
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        var preferredName = selectName ?? FocusedItem?.Name;
        IsLoading = true;
        ErrorBanner = HasPathWarning ? ErrorBanner : null;
        _allItems.Clear();
        Items.Clear();

        try
        {
            if (IsSearchResultsTab)
            {
                foreach (var entry in _customSearchResults)
                    _allItems.Add(new FileItemViewModel(entry));
                GitSummaryBadge = "";
                RebuildVisibleList(preferredName);
            }
            else if (IsVirtualArchive)
            {
                var entries = await Task.Run(() => ArchiveService.ListVirtualArchiveEntries(CurrentPath), token);
                foreach (var entry in entries)
                    _allItems.Add(new FileItemViewModel(entry));
                GitSummaryBadge = "";
                RebuildVisibleList(preferredName);
            }
            else if (IsRemoteConnection)
            {
                var entries = await RemoteFileSystemService.ListDirectoryAsync(CurrentPath, token);
                foreach (var entry in entries)
                {
                    if (ShowHidden || !entry.IsHidden)
                        _allItems.Add(new FileItemViewModel(entry));
                }
                GitSummaryBadge = "";
                RebuildVisibleList(preferredName);
            }
            else if (IsBranchView)
            {
                var entries = await Task.Run(() => EnumerateBranchFiles(CurrentPath, ShowHidden, token), token);
                foreach (var entry in entries)
                    _allItems.Add(new FileItemViewModel(entry));
                RebuildVisibleList(preferredName);
                _ = RefreshGitStatusAsync(CurrentPath, token);
            }
            else
            {
                await foreach (var batch in DirectoryEnumerator.EnumerateBatchesAsync(CurrentPath, ShowHidden, ct: token))
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var entry in batch)
                        _allItems.Add(new FileItemViewModel(entry));
                }

                token.ThrowIfCancellationRequested();
                RebuildVisibleList(preferredName);
                _ = RefreshGitStatusAsync(CurrentPath, token);
                if (IsTreeSizeActive)
                    _ = AnalyzeDirectorySizesAsync(sortBySizeDescending: false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a newer navigation request — not an error
        }
        catch (Exception ex)
        {
            ErrorBanner = ex.Message;
            RebuildVisibleList(preferredName);
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    public async Task ToggleBranchViewAsync()
    {
        if (IsVirtualArchive || IsRemoteConnection || IsSearchResultsTab)
            return;
        IsBranchView = !IsBranchView;
        await ReloadAsync(FocusedItem?.Name);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleTreeSizeAsync()
    {
        if (IsTreeSizeActive)
        {
            IsTreeSizeActive = false;
            foreach (var item in Items)
            {
                item.ComputedFolderSize = null;
                item.SizeRatio = 0;
            }
            return;
        }

        await AnalyzeDirectorySizesAsync(sortBySizeDescending: true);
    }

    public void RefreshBookmarkState()
    {
        OnPropertyChanged(nameof(IsBookmarked));
        OnPropertyChanged(nameof(BookmarkGlyph));
    }

    public async Task AnalyzeDirectorySizesAsync(bool sortBySizeDescending = true)
    {
        if (IsVirtualArchive || IsRemoteConnection || IsSearchResultsTab || string.IsNullOrEmpty(CurrentPath))
            return;

        IsTreeSizeActive = true;
        var dirItems = Items.Where(i => i.IsDirectory && !i.IsParentLink).ToList();

        await Task.Run(() =>
        {
            Parallel.ForEach(dirItems, new ParallelOptions { MaxDegreeOfParallelism = 4 }, item =>
            {
                try
                {
                    long sum = 0;
                    var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
                    foreach (var f in new DirectoryInfo(item.FullPath).EnumerateFiles("*", opts))
                        sum += f.Length;
                    _dispatcher.Post(() => item.ComputedFolderSize = sum);
                }
                catch { }
            });
        });

        _dispatcher.Post(() =>
        {
            long maxBytes = Items.Where(i => !i.IsParentLink).Select(i => i.EffectiveSize).DefaultIfEmpty(0).Max();
            foreach (var item in Items.Where(i => !i.IsParentLink))
            {
                item.SizeRatio = maxBytes > 0 ? (double)item.EffectiveSize / maxBytes : 0;
            }
            if (sortBySizeDescending)
            {
                Sort = new SortSpec
                {
                    Column = SortColumn.Size,
                    Descending = true,
                    FoldersFirst = false,
                };
                RebuildVisibleList(FocusedItem?.Name);
                // Reapply ratios after rebuild
                long max2 = Items.Where(i => !i.IsParentLink).Select(i => i.EffectiveSize).DefaultIfEmpty(0).Max();
                foreach (var item in Items.Where(i => !i.IsParentLink))
                    item.SizeRatio = max2 > 0 ? (double)item.EffectiveSize / max2 : 0;
            }
            UpdateStatusSummary();
        });
    }

    public async Task LoadSearchResultsAsync(string queryTitle, IReadOnlyList<FileEntry> results)
    {
        _customSearchResults.Clear();
        _customSearchResults.AddRange(results);
        string virtualPath = $"search://{queryTitle}";
        await NavigateAsync(virtualPath, addToHistory: true, bypassLock: true);
    }

    private async Task RefreshGitStatusAsync(string directoryPath, CancellationToken ct)
    {
        try
        {
            var snap = await GitStatusService.GetStatusForDirectoryAsync(directoryPath, ct);
            if (ct.IsCancellationRequested || !string.Equals(CurrentPath, directoryPath, StringComparison.OrdinalIgnoreCase))
                return;

            _dispatcher.Post(() =>
            {
                if (!snap.IsGitRepository)
                {
                    GitSummaryBadge = "";
                    foreach (var item in Items)
                        item.GitStatusBadge = "";
                    return;
                }

                GitSummaryBadge = snap.SummaryBadge;

                foreach (var item in Items)
                {
                    if (item.IsParentLink) continue;
                    var norm = item.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    item.GitStatusBadge = snap.EntryStatusBadges.TryGetValue(norm, out var badge) ? badge : "";
                }
            });
        }
        catch
        {
            // Ignore Git inspection errors
        }
    }

    private static List<FileEntry> EnumerateBranchFiles(string rootDir, bool showHidden, CancellationToken ct)
    {
        var list = new List<FileEntry>();
        if (!Directory.Exists(rootDir)) return list;

        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden,
        };

        foreach (var file in new DirectoryInfo(rootDir).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string rel = Path.GetRelativePath(rootDir, file.FullName);
                list.Add(new FileEntry(
                    file.Name,
                    file.FullName,
                    false,
                    file.Length,
                    file.LastWriteTime,
                    file.Attributes)
                {
                    RelativeSubPath = rel
                });
            }
            catch { }
        }
        return list;
    }

    public async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        if (IsVirtualArchive)
        {
            var parentArch = ArchiveService.GetParentPathForVirtualArchive(CurrentPath);
            if (!string.IsNullOrEmpty(parentArch))
            {
                string? select = null;
                if (ArchiveService.TryParseVirtualArchivePath(CurrentPath, out var archPath, out var inner))
                    select = string.IsNullOrEmpty(inner) ? Path.GetFileName(archPath) : Path.GetFileName(inner);
                await NavigateAsync(parentArch, addToHistory: true, selectName: select);
            }
            return;
        }

        if (IsRemoteConnection)
        {
            var parentUri = RemoteFileSystemService.GetParentRemoteUri(CurrentPath);
            if (!string.IsNullOrEmpty(parentUri))
            {
                string? select = null;
                if (RemoteFileSystemService.TryParseRemoteUri(CurrentPath, out _, out _, out _, out _, out var rPath))
                    select = Path.GetFileName(rPath.TrimEnd('/'));
                await NavigateAsync(parentUri, addToHistory: true, selectName: select);
            }
            return;
        }

        if (IsSearchResultsTab) return;

        var parent = Directory.GetParent(CurrentPath);
        if (parent is null) return;
        var childName = Path.GetFileName(CurrentPath.TrimEnd('\\'));
        await NavigateAsync(parent.FullName, addToHistory: true, selectName: childName);
    }

    public async Task GoBackAsync()
    {
        if (_back.Count == 0) return;
        var target = _back.Pop();
        _forward.Push(CurrentPath);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        await NavigateAsync(target, addToHistory: false, bypassLock: true);
    }

    public async Task GoForwardAsync()
    {
        if (_forward.Count == 0) return;
        var target = _forward.Pop();
        _back.Push(CurrentPath);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        await NavigateAsync(target, addToHistory: false, bypassLock: true);
    }

    public void ApplySort(SortColumn column)
    {
        Sort = Sort.Column == column
            ? Sort with { Descending = !Sort.Descending, FoldersFirst = Settings.SortFoldersFirst }
            : Sort with { Column = column, Descending = false, FoldersFirst = Settings.SortFoldersFirst };
        RebuildVisibleList(FocusedItem?.Name);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySortFoldersFirst(bool foldersFirst)
    {
        Sort = Sort with { FoldersFirst = foldersFirst };
        RebuildVisibleList(FocusedItem?.Name);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshDisplayNames()
    {
        foreach (var item in Items)
            item.RefreshDisplayName();
    }

    public async Task ToggleHiddenAsync()
    {
        ShowHidden = !ShowHidden;
        await ReloadAsync(FocusedItem?.Name);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMarkFocusedAndAdvance()
    {
        if (FocusedItem is null || FocusedItem.IsParentLink) return;
        var item = FocusedItem;
        item.IsMarked = !item.IsMarked;
        if (item.IsMarked && item.IsDirectory && item.ComputedFolderSize is null && Settings.AutoCalculateFolderSizeOnMark && !IsRemoteConnection && !IsVirtualArchive)
            _ = ComputeFolderSizeAsync(item);

        int idx = Items.IndexOf(item);
        if (idx >= 0 && idx + 1 < Items.Count)
            FocusedItem = Items[idx + 1];

        UpdateStatusSummary();
    }

    public void SelectAll()
    {
        foreach (var item in Items)
            if (!item.IsParentLink) item.IsMarked = true;
        UpdateStatusSummary();
    }

    public void InvertSelection()
    {
        foreach (var item in Items)
            if (!item.IsParentLink) item.IsMarked = !item.IsMarked;
        UpdateStatusSummary();
    }

    public IReadOnlyList<string> GetTargetPaths()
    {
        var marked = Items.Where(i => i.IsMarked && !i.IsParentLink).Select(i => i.FullPath).ToList();
        if (marked.Count > 0) return marked;
        if (FocusedItem is { IsParentLink: false } f) return [f.FullPath];
        return [];
    }

    public void SelectByPath(string fullPath)
    {
        var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        var match = Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null) FocusedItem = match;
    }

    public void MoveFocus(int delta)
    {
        if (Items.Count == 0) return;
        int current = FocusedItem is not null ? Items.IndexOf(FocusedItem) : 0;
        if (current < 0) current = 0;
        int next = Math.Clamp(current + delta, 0, Items.Count - 1);
        FocusedItem = Items[next];
    }

    public void MoveFocusToFirst()
    {
        if (Items.Count > 0)
            FocusedItem = Items[0];
    }

    public void MoveFocusToLast()
    {
        if (Items.Count > 0)
            FocusedItem = Items[^1];
    }

    /// <summary>Jumps to the first entry starting with the typed prefix (Quick Search).</summary>
    public bool JumpToPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return false;
        var match = Items.FirstOrDefault(i => !i.IsParentLink &&
            i.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;
        FocusedItem = match;
        return true;
    }

    public IReadOnlyList<string> GetAutoCompleteSuggestions(string input, int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var trimmed = input.Trim();
        var results = new List<string>();

        if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            string[] shells = ["shell:Downloads", "shell:Documents", "shell:Desktop", "shell:Pictures", "shell:Music", "shell:Videos", "shell:UserProfile", "shell:AppData"];
            return shells.Where(s => s.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
            return results;

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        try
        {
            string? baseDir;
            string prefix;
            if (Directory.Exists(expanded) && (expanded.EndsWith('\\') || expanded.EndsWith('/')))
            {
                baseDir = expanded;
                prefix = "";
            }
            else
            {
                baseDir = Path.GetDirectoryName(expanded);
                prefix = Path.GetFileName(expanded);
            }

            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(baseDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(dir);
                        if (results.Count >= maxResults) break;
                    }
                }
            }
        }
        catch (Exception) { }
        return results;
    }

    public void SetWatcherActive(bool active)
    {
        if (_isWatcherActive == active) return;
        _isWatcherActive = active;
        RebindWatcher();
    }

    public TabState ToState() => new()
    {
        Id = Id,
        Path = CurrentPath,
        Pinned = IsPinned,
        Locked = IsLocked,
        ShowHidden = ShowHidden,
        ViewMode = ViewMode,
        Sort = Sort,
        Filter = string.IsNullOrEmpty(Filter) ? null : Filter,
        FocusedName = FocusedItem?.Name,
        Back = _back.Reverse().Take(25).ToList(),
        Forward = _forward.Reverse().Take(25).ToList(),
    };

    private void RebindWatcher()
    {
        _watcherSub?.Dispose();
        _watcherSub = null;
        if (_isWatcherActive && !IsVirtualArchive && !IsRemoteConnection && !IsSearchResultsTab &&
            !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
        {
            _watcherSub = _watcherHub.Subscribe(CurrentPath, OnWatcherBatch);
        }
    }

    private void OnWatcherBatch(IReadOnlyList<FsChange> changes)
    {
        _dispatcher.Post(() => ApplyWatcherChanges(changes));
    }

    internal void ApplyWatcherChanges(IReadOnlyList<FsChange> changes)
    {
        if (IsBranchView || changes.Any(c => c.Kind == FsChangeKind.Overflow))
        {
            _ = ReloadAsync(FocusedItem?.Name);
            return;
        }

        var markedPaths = _allItems.Where(i => i.IsMarked).Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var focusedName = FocusedItem?.Name;

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case FsChangeKind.Deleted:
                    _allItems.RemoveAll(i => string.Equals(i.FullPath, change.FullPath, StringComparison.OrdinalIgnoreCase));
                    break;

                case FsChangeKind.Renamed:
                    if (change.OldFullPath is not null)
                        _allItems.RemoveAll(i => string.Equals(i.FullPath, change.OldFullPath, StringComparison.OrdinalIgnoreCase));
                    goto case FsChangeKind.Created;

                case FsChangeKind.Created:
                case FsChangeKind.Changed:
                    var entry = FileEntry.TryFromPath(change.FullPath);
                    _allItems.RemoveAll(i => string.Equals(i.FullPath, change.FullPath, StringComparison.OrdinalIgnoreCase));
                    if (entry is not null && (ShowHidden || !entry.IsHidden))
                    {
                        var vm = new FileItemViewModel(entry) { IsMarked = markedPaths.Contains(entry.FullPath) };
                        _allItems.Add(vm);
                    }
                    break;
            }
        }

        RebuildVisibleList(focusedName);
        if (!string.IsNullOrEmpty(CurrentPath))
            _ = RefreshGitStatusAsync(CurrentPath, CancellationToken.None);
    }

    private void RebuildVisibleList(string? preferredFocusName)
    {
        var effectiveSort = Sort with { FoldersFirst = Settings.SortFoldersFirst };
        var comparer = new FileEntryComparer(effectiveSort);
        var filtered = _allItems
            .Where(i => MatchesFilter(i, Filter))
            .OrderBy(i => i.Entry, comparer)
            .ToList();

        Items.Clear();

        if (IsVirtualArchive)
        {
            var parentArch = ArchiveService.GetParentPathForVirtualArchive(CurrentPath);
            if (!string.IsNullOrEmpty(parentArch))
                Items.Add(new FileItemViewModel(FileEntry.ParentLink(parentArch)));
        }
        else if (IsRemoteConnection)
        {
            var parentUri = RemoteFileSystemService.GetParentRemoteUri(CurrentPath);
            if (!string.IsNullOrEmpty(parentUri))
                Items.Add(new FileItemViewModel(FileEntry.ParentLink(parentUri)));
        }
        else if (!IsSearchResultsTab)
        {
            var parent = string.IsNullOrEmpty(CurrentPath) ? null : Directory.GetParent(CurrentPath);
            if (parent is not null)
                Items.Add(new FileItemViewModel(FileEntry.ParentLink(parent.FullName)));
        }

        foreach (var item in filtered)
        {
            item.IsFocused = false;
            Items.Add(item);
        }

        var nextFocus = (preferredFocusName is not null
            ? Items.FirstOrDefault(i => string.Equals(i.Name, preferredFocusName, StringComparison.OrdinalIgnoreCase))
            : null) ?? Items.FirstOrDefault();

        FocusedItem = nextFocus;
        if (nextFocus is not null)
            nextFocus.IsFocused = true;

        UpdateStatusSummary();
    }

    private static bool MatchesFilter(FileItemViewModel item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = filter.Trim();
        if (f.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("note:", StringComparison.OrdinalIgnoreCase))
        {
            return ModernExplorer.Core.Features.FileTagService.MatchesTagFilter(item.FullPath, f);
        }
        if (f.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
        {
            string extQuery = f["ext:".Length..].Trim().TrimStart('.');
            if (string.IsNullOrEmpty(extQuery)) return true;
            return string.Equals(item.Entry.Extension.TrimStart('.'), extQuery, StringComparison.OrdinalIgnoreCase);
        }
        string name = item.Name;
        if (f.Contains('*') || f.Contains('?'))
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, name, ignoreCase: true);
        return name.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(path)) return;

        if (IsSearchResultsTab)
        {
            Breadcrumbs.Add(new BreadcrumbSegment(Title, path));
            return;
        }

        if (IsVirtualArchive && ArchiveService.TryParseVirtualArchivePath(path, out var arch, out var inner))
        {
            Breadcrumbs.Add(new BreadcrumbSegment("📦 " + Path.GetFileName(arch), ArchiveService.BuildVirtualArchivePath(arch)));
            if (!string.IsNullOrEmpty(inner))
            {
                string accum = "";
                foreach (var part in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    accum = string.IsNullOrEmpty(accum) ? part : $"{accum}/{part}";
                    Breadcrumbs.Add(new BreadcrumbSegment(part, ArchiveService.BuildVirtualArchivePath(arch, accum)));
                }
            }
            return;
        }

        if (IsRemoteConnection && RemoteFileSystemService.TryParseRemoteUri(path, out var proto, out var host, out var port, out var user, out var rPath))
        {
            string scheme = proto switch
            {
                RemoteProtocol.Ftp => "ftp",
                RemoteProtocol.Ftps => "ftps",
                _ => "sftp"
            };
            string userPart = string.IsNullOrEmpty(user) ? "" : $"{Uri.EscapeDataString(user)}@";
            string rootUri = $"{scheme}://{userPart}{host}:{port}/";
            Breadcrumbs.Add(new BreadcrumbSegment($"🌐 {host}", rootUri));

            string accum = "";
            foreach (var part in rPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                accum = $"{accum}/{part}";
                Breadcrumbs.Add(new BreadcrumbSegment(part, $"{scheme}://{userPart}{host}:{port}{accum}"));
            }
            return;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return;

        Breadcrumbs.Add(new BreadcrumbSegment(root.TrimEnd('\\'), root));
        var rest = path[root.Length..].Trim('\\');
        if (string.IsNullOrEmpty(rest)) return;

        var current = root;
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            Breadcrumbs.Add(new BreadcrumbSegment(part, current));
        }
    }

    private void UpdateDriveSummary(string path)
    {
        if (IsVirtualArchive)
        {
            if (ArchiveService.TryParseVirtualArchivePath(path, out var arch, out _) && File.Exists(arch))
                DriveSummary = $"📦 {Path.GetFileName(arch)} ({Format.Size(new FileInfo(arch).Length)})";
            else
                DriveSummary = "📦 Archive";
            return;
        }

        if (IsRemoteConnection)
        {
            if (RemoteFileSystemService.TryParseRemoteUri(path, out var proto, out var host, out var port, out _, out _))
                DriveSummary = $"🌐 {proto.ToString().ToUpperInvariant()} {host}:{port}";
            else
                DriveSummary = "🌐 Remote";
            return;
        }

        if (IsSearchResultsTab)
        {
            DriveSummary = "🔍 Search";
            return;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) { DriveSummary = ""; return; }
            var drive = new DriveInfo(root);
            if (!drive.IsReady) { DriveSummary = root; return; }
            var freeInfo = LocalizationService.Format("Drive.FreeOfTotal", Format.Size(drive.AvailableFreeSpace), Format.Size(drive.TotalSize));
            DriveSummary = $"{drive.Name.TrimEnd('\\')} {freeInfo}";
        }
        catch
        {
            DriveSummary = "";
        }
    }

    public void UpdateStatusSummary()
    {
        var realItems = Items.Where(i => !i.IsParentLink).ToList();
        int folders = realItems.Count(i => i.IsDirectory);
        int files = realItems.Count - folders;
        var marked = realItems.Where(i => i.IsMarked).ToList();
        long markedBytes = marked.Sum(i => i.EffectiveSize);

        var s = LocalizationService.Format("Status.ItemsSummary", realItems.Count, folders, files);
        if (marked.Count > 0)
            s += LocalizationService.Format("Status.MarkedSummary", marked.Count, Format.Size(markedBytes));
        if (!string.IsNullOrWhiteSpace(Filter))
            s += LocalizationService.Format("Status.FilterSummary", Filter);
        StatusSummary = s;
    }

    private async Task ComputeFolderSizeAsync(FileItemViewModel item)
    {
        try
        {
            var size = await Task.Run(() =>
            {
                long sum = 0;
                var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
                foreach (var f in new DirectoryInfo(item.FullPath).EnumerateFiles("*", opts))
                    sum += f.Length;
                return sum;
            });
            _dispatcher.Post(() =>
            {
                item.ComputedFolderSize = size;
                UpdateStatusSummary();
            });
        }
        catch { }
    }

    public static string? ResolveSpecialAndFallback(string rawPath, out bool fellBackToParent)
    {
        fellBackToParent = false;
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        var trimmed = rawPath.Trim().Trim('"');

        if (ArchiveService.IsVirtualArchivePath(trimmed))
            return trimmed;

        if (RemoteFileSystemService.IsRemoteUri(trimmed))
            return trimmed;

        if (trimmed.StartsWith("search://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var key = trimmed["shell:".Length..].ToLowerInvariant();
            trimmed = key switch
            {
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "userprofile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                _ => trimmed,
            };
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        string full;
        try { full = Path.GetFullPath(expanded); }
        catch { return null; }

        if (Directory.Exists(full)) return full;

        // If user entered an archive file path directly, open it as a virtual archive
        if (File.Exists(full) && ArchiveService.IsSupportedArchiveFile(full))
            return ArchiveService.BuildVirtualArchivePath(full);

        // Fall back to the nearest existing parent directory (per specification 2.4)
        var current = Directory.GetParent(full);
        while (current is not null)
        {
            if (current.Exists)
            {
                fellBackToParent = true;
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }

    public void Dispose()
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        ModernExplorer.Core.Features.FileTagService.TagsChanged -= OnTagsChanged;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _watcherSub?.Dispose();
    }
}
