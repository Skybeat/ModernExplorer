using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.FileSystem;
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
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private CancellationTokenSource? _loadCts;
    private IDisposable? _watcherSub;
    private bool _isWatcherActive;

    public Guid Id { get; }
    public SettingsViewModel Settings => SettingsViewModel.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DisplayTitle), nameof(CanGoUp))]
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

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) is not null;

    public string Title
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPath)) return "Tab";
            var trimmed = CurrentPath.TrimEnd('\\');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }

    public string DisplayTitle
    {
        get
        {
            var prefix = (IsPinned ? "📌 " : "") + (IsLocked ? "🔒 " : "") + (HasPathWarning ? "⚠ " : "");
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
            Sort = state.Sort;
            Filter = state.Filter ?? "";
            foreach (var b in Enumerable.Reverse(state.Back)) _back.Push(b);
            foreach (var f in Enumerable.Reverse(state.Forward)) _forward.Push(f);
        }
        else
        {
            ShowHidden = SettingsViewModel.Current.ShowHiddenFilesByDefault;
            Sort = new SortSpec
            {
                Column = SortColumn.Name,
                Descending = false,
                FoldersFirst = SettingsViewModel.Current.SortFoldersFirst,
            };
        }
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
            ErrorBanner = $"Pfad nicht gefunden: {rawPath}";
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
        ErrorBanner = warning ? $"Ursprünglicher Pfad nicht verfügbar – gewechselt zu {resolved}" : null;
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
        _allItems.Clear();
        Items.Clear();

        try
        {
            await foreach (var batch in DirectoryEnumerator.EnumerateBatchesAsync(CurrentPath, ShowHidden, ct: token))
            {
                token.ThrowIfCancellationRequested();
                foreach (var entry in batch)
                    _allItems.Add(new FileItemViewModel(entry));
            }

            token.ThrowIfCancellationRequested();
            RebuildVisibleList(preferredName);
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

    public async Task GoUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
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
        if (item.IsMarked && item.IsDirectory && item.ComputedFolderSize is null && Settings.AutoCalculateFolderSizeOnMark)
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
        var name = Path.GetFileName(fullPath.TrimEnd('\\'));
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
        if (_isWatcherActive && !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
            _watcherSub = _watcherHub.Subscribe(CurrentPath, OnWatcherBatch);
    }

    private void OnWatcherBatch(IReadOnlyList<FsChange> changes)
    {
        _dispatcher.Post(() => ApplyWatcherChanges(changes));
    }

    internal void ApplyWatcherChanges(IReadOnlyList<FsChange> changes)
    {
        if (changes.Any(c => c.Kind == FsChangeKind.Overflow))
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
    }

    private void RebuildVisibleList(string? preferredFocusName)
    {
        var effectiveSort = Sort with { FoldersFirst = Settings.SortFoldersFirst };
        var comparer = new FileEntryComparer(effectiveSort);
        var filtered = _allItems
            .Where(i => MatchesFilter(i.Name, Filter))
            .OrderBy(i => i.Entry, comparer)
            .ToList();

        Items.Clear();
        var parent = string.IsNullOrEmpty(CurrentPath) ? null : Directory.GetParent(CurrentPath);
        if (parent is not null)
            Items.Add(new FileItemViewModel(FileEntry.ParentLink(parent.FullName)));

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

    private static bool MatchesFilter(string name, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = filter.Trim();
        if (f.Contains('*') || f.Contains('?'))
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, name, ignoreCase: true);
        return name.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(path)) return;
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
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) { DriveSummary = ""; return; }
            var drive = new DriveInfo(root);
            if (!drive.IsReady) { DriveSummary = root; return; }
            DriveSummary = $"{drive.Name.TrimEnd('\\')} {Format.Size(drive.AvailableFreeSpace)} frei von {Format.Size(drive.TotalSize)}";
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

        var s = $"{realItems.Count} Elemente ({folders} Ordner, {files} Dateien)";
        if (marked.Count > 0)
            s += $" · {marked.Count} markiert ({Format.Size(markedBytes)})";
        if (!string.IsNullOrWhiteSpace(Filter))
            s += $" · Filter: „{Filter}“";
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
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _watcherSub?.Dispose();
    }
}
