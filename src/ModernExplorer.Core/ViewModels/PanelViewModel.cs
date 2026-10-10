using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Session;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class PanelViewModel : ObservableObject, IDisposable
{
    private readonly WatcherHub _watcherHub;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<TabState, PanelSide>? _onTabClosed;
    private bool _isDisposing;

    public PanelSide Side { get; }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private TabViewModel? _activeTab;

    [ObservableProperty]
    private bool _isActivePanel;

    public event EventHandler? StateChanged;

    public PanelViewModel(PanelSide side, WatcherHub watcherHub, IUiDispatcher dispatcher, Action<TabState, PanelSide>? onTabClosed = null)
    {
        Side = side;
        _watcherHub = watcherHub;
        _dispatcher = dispatcher;
        _onTabClosed = onTabClosed;
        Tabs.CollectionChanged += (_, _) =>
        {
            if (!_isDisposing)
                StateChanged?.Invoke(this, EventArgs.Empty);
        };
        RefreshDrives();
    }

    public void RefreshDrives()
    {
        Drives.Clear();
        foreach (var d in DriveItemViewModel.LoadSystemDrives())
            Drives.Add(d);
        UpdateSelectedDrive();
    }

    public async Task NavigateToDriveAsync(string rootPath)
    {
        if (ActiveTab is not null)
            await ActiveTab.NavigateAsync(rootPath);
        else
            await CreateTabAsync(rootPath, activate: true);
        UpdateSelectedDrive();
    }

    public void UpdateSelectedDrive()
    {
        var currentRoot = ActiveTab?.CurrentPath is { Length: > 0 } p ? Path.GetPathRoot(p)?.TrimEnd('\\', '/') : null;
        foreach (var d in Drives)
        {
            var driveRoot = d.RootPath.TrimEnd('\\', '/');
            d.IsSelected = !string.IsNullOrEmpty(currentRoot) &&
                           string.Equals(driveRoot, currentRoot, StringComparison.OrdinalIgnoreCase);
        }
    }

    partial void OnActiveTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (_isDisposing) return;
        if (newValue is null && Tabs.Count > 0)
        {
            ActiveTab = oldValue is not null && Tabs.Contains(oldValue) ? oldValue : Tabs[0];
            return;
        }

        oldValue?.SetWatcherActive(false);
        newValue?.SetWatcherActive(true);
        UpdateSelectedDrive();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TabViewModel> CreateTabAsync(string? path = null, bool activate = true)
    {
        var initialPath = path ?? ActiveTab?.CurrentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tab = new TabViewModel(_watcherHub, _dispatcher);
        WireTab(tab);
        Tabs.Add(tab);
        if (activate || ActiveTab is null)
            ActiveTab = tab;
        await tab.NavigateAsync(initialPath, addToHistory: false, bypassLock: true);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    public async Task<TabViewModel> AddTabFromStateAsync(TabState state, bool activate = true, int? insertIndex = null)
    {
        var tab = new TabViewModel(_watcherHub, _dispatcher, state);
        WireTab(tab);

        if (insertIndex is { } idx && idx >= 0 && idx <= Tabs.Count)
            Tabs.Insert(idx, tab);
        else if (tab.IsPinned)
            Tabs.Insert(CountPinned(), tab);
        else
            Tabs.Add(tab);

        if (activate || ActiveTab is null)
            ActiveTab = tab;

        var targetPath = string.IsNullOrEmpty(state.Path)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : state.Path;
        await tab.NavigateAsync(targetPath, addToHistory: false, selectName: state.FocusedName, bypassLock: true);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return tab;
    }

    public async Task<bool> CloseTabAsync(TabViewModel? tab = null, bool forceIfPinned = false)
    {
        tab ??= ActiveTab;
        if (tab is null) return false;
        if (tab.IsPinned && !forceIfPinned) return false;

        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return false;

        _onTabClosed?.Invoke(tab.ToState(), Side);

        if (Tabs.Count == 1)
        {
            // Last tab: reset to user profile folder instead of leaving an empty panel
            var fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            tab.IsLocked = false;
            await tab.NavigateAsync(fallback, addToHistory: false, bypassLock: true);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        UnwireTab(tab);
        Tabs.RemoveAt(idx);
        tab.Dispose();

        if (ReferenceEquals(ActiveTab, tab))
        {
            var nextIdx = Math.Clamp(idx, 0, Tabs.Count - 1);
            ActiveTab = Tabs[nextIdx];
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<TabViewModel?> DuplicateActiveTabAsync()
    {
        if (ActiveTab is null) return null;
        var clone = ActiveTab.ToState();
        clone.Id = Guid.NewGuid();
        clone.Pinned = false;
        int insertAt = Tabs.IndexOf(ActiveTab) + 1;
        return await AddTabFromStateAsync(clone, activate: true, insertIndex: insertAt);
    }

    public void TogglePinActiveTab()
    {
        if (ActiveTab is null) return;
        var tab = ActiveTab;
        tab.IsPinned = !tab.IsPinned;

        // Pinned tabs form a dedicated zone on the left
        int currentIndex = Tabs.IndexOf(tab);
        int targetIndex = tab.IsPinned
            ? CountPinned() - 1
            : CountPinned();

        if (currentIndex >= 0 && targetIndex >= 0 && targetIndex < Tabs.Count && currentIndex != targetIndex)
            Tabs.Move(currentIndex, targetIndex);

        ActiveTab = tab;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleLockActiveTab()
    {
        if (ActiveTab is null) return;
        ActiveTab.IsLocked = !ActiveTab.IsLocked;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NextTab()
    {
        if (Tabs.Count <= 1 || ActiveTab is null) return;
        int idx = (Tabs.IndexOf(ActiveTab) + 1) % Tabs.Count;
        ActiveTab = Tabs[idx];
    }

    public void PreviousTab()
    {
        if (Tabs.Count <= 1 || ActiveTab is null) return;
        int idx = (Tabs.IndexOf(ActiveTab) - 1 + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[idx];
    }

    public void GoToTab(int index)
    {
        if (index >= 0 && index < Tabs.Count)
            ActiveTab = Tabs[index];
    }

    public void GoToLastTab()
    {
        if (Tabs.Count > 0)
            ActiveTab = Tabs[^1];
    }

    public void MoveActiveTabLeft()
    {
        if (ActiveTab is null) return;
        int idx = Tabs.IndexOf(ActiveTab);
        MoveTab(idx, idx - 1);
    }

    public void MoveActiveTabRight()
    {
        if (ActiveTab is null) return;
        int idx = Tabs.IndexOf(ActiveTab);
        MoveTab(idx, idx + 1);
    }

    public bool MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Tabs.Count) return false;
        if (toIndex < 0 || toIndex >= Tabs.Count || fromIndex == toIndex) return false;

        var tab = Tabs[fromIndex];
        int pinnedCount = CountPinned();
        if (tab.IsPinned && toIndex >= pinnedCount) return false;
        if (!tab.IsPinned && toIndex < pinnedCount) return false;

        Tabs.Move(fromIndex, toIndex);
        ActiveTab = tab;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public TabState? DetachTab(TabViewModel? tab = null)
    {
        tab ??= ActiveTab;
        if (tab is null) return null;
        var state = tab.ToState();

        if (Tabs.Count == 1)
            return state;

        int idx = Tabs.IndexOf(tab);
        UnwireTab(tab);
        Tabs.RemoveAt(idx);
        tab.Dispose();
        ActiveTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)];
        StateChanged?.Invoke(this, EventArgs.Empty);
        return state;
    }

    public PanelState ToState()
    {
        var activeIdx = ActiveTab is not null ? Math.Max(0, Tabs.IndexOf(ActiveTab)) : 0;
        return new PanelState
        {
            ActiveIndex = activeIdx,
            Tabs = Tabs.Select(t => t.ToState()).ToList(),
        };
    }

    private int CountPinned() => Tabs.Count(t => t.IsPinned);

    private void WireTab(TabViewModel tab)
    {
        tab.RequestOpenInNewTab += OnTabRequestOpenInNewTab;
        tab.StateChanged += OnTabStateChanged;
        tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void UnwireTab(TabViewModel tab)
    {
        tab.RequestOpenInNewTab -= OnTabRequestOpenInNewTab;
        tab.StateChanged -= OnTabStateChanged;
        tab.PropertyChanged -= OnTabPropertyChanged;
    }

    private void OnTabRequestOpenInNewTab(string path) => _ = CreateTabAsync(path, activate: true);

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isDisposing) return;
        if (ReferenceEquals(sender, ActiveTab) && e.PropertyName == nameof(TabViewModel.CurrentPath))
            UpdateSelectedDrive();
    }

    private void OnTabStateChanged(object? sender, EventArgs e)
    {
        if (_isDisposing) return;
        UpdateSelectedDrive();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _isDisposing = true;
        foreach (var t in Tabs)
        {
            UnwireTab(t);
            t.Dispose();
        }
        Tabs.Clear();
    }
}
