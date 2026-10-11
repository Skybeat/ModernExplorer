using System.IO;
using System.Text.Json;
using ModernExplorer.Core.Session;
using ModernExplorer.Core.ViewModels;

namespace ModernExplorer.Core.Features;

public sealed record WorkspaceProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsDualMode { get; init; } = true;
    public double SplitRatio { get; init; } = 0.5;
    public PanelSide ActiveSide { get; init; } = PanelSide.Left;
    public PanelState LeftPanel { get; init; } = new();
    public PanelState RightPanel { get; init; } = new();

    public string SummaryText
    {
        get
        {
            string leftFocus = LeftPanel.Tabs.Count > 0
                ? Path.GetFileName(LeftPanel.Tabs[Math.Clamp(LeftPanel.ActiveIndex, 0, LeftPanel.Tabs.Count - 1)].Path.TrimEnd('\\', '/'))
                : "-";
            if (string.IsNullOrEmpty(leftFocus) && LeftPanel.Tabs.Count > 0)
                leftFocus = LeftPanel.Tabs[0].Path;

            string rightFocus = RightPanel.Tabs.Count > 0
                ? Path.GetFileName(RightPanel.Tabs[Math.Clamp(RightPanel.ActiveIndex, 0, RightPanel.Tabs.Count - 1)].Path.TrimEnd('\\', '/'))
                : "-";
            if (string.IsNullOrEmpty(rightFocus) && RightPanel.Tabs.Count > 0)
                rightFocus = RightPanel.Tabs[0].Path;

            return $"L: {leftFocus} ({LeftPanel.Tabs.Count} Tabs)  ⇄  R: {rightFocus} ({RightPanel.Tabs.Count} Tabs)";
        }
    }
}

/// <summary>
/// Saves and restores named dual-pane workspace profiles (left & right tab sets, lock states, view modes, and split ratio).
/// </summary>
public static class WorkspaceProfileService
{
    private static readonly object SyncLock = new();
    private static List<WorkspaceProfile>? _cached;
    private static string? _customStorePath;

    public static void UseCustomStorePath(string? filePath)
    {
        lock (SyncLock)
        {
            _customStorePath = filePath;
            _cached = null;
        }
    }

    public static IReadOnlyList<WorkspaceProfile> GetProfiles()
    {
        lock (SyncLock)
        {
            EnsureLoaded();
            return _cached!.OrderByDescending(p => p.UpdatedAtUtc).ToList();
        }
    }

    public static WorkspaceProfile CaptureProfile(
        string name,
        string description,
        bool isDualMode,
        double splitRatio,
        PanelSide activeSide,
        PanelState leftState,
        PanelState rightState)
    {
        var profile = new WorkspaceProfile
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name) ? $"Workspace {DateTime.Now:dd.MM HH:mm}" : name.Trim(),
            Description = (description ?? "").Trim(),
            UpdatedAtUtc = DateTime.UtcNow,
            IsDualMode = isDualMode,
            SplitRatio = Math.Clamp(splitRatio, 0.15, 0.85),
            ActiveSide = activeSide,
            LeftPanel = leftState,
            RightPanel = rightState,
        };

        SaveProfile(profile);
        return profile;
    }

    public static void SaveProfile(WorkspaceProfile profile)
    {
        lock (SyncLock)
        {
            EnsureLoaded();
            int existingIdx = _cached!.FindIndex(p =>
                p.Id == profile.Id || string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

            var updated = profile with { UpdatedAtUtc = DateTime.UtcNow };
            if (existingIdx >= 0)
                _cached[existingIdx] = updated;
            else
                _cached.Add(updated);

            PersistInternal();
        }
    }

    public static bool DeleteProfile(Guid id)
    {
        lock (SyncLock)
        {
            EnsureLoaded();
            int removed = _cached!.RemoveAll(p => p.Id == id);
            if (removed > 0)
                PersistInternal();
            return removed > 0;
        }
    }

    public static async Task ApplyWorkspaceAsync(MainViewModel vm, WorkspaceProfile profile)
    {
        vm.IsDualMode = profile.IsDualMode;
        vm.SplitRatio = Math.Clamp(profile.SplitRatio, 0.15, 0.85);

        await RestorePanelTabsAsync(vm.Left, profile.LeftPanel);
        await RestorePanelTabsAsync(vm.Right, profile.RightPanel);

        vm.ActiveSide = profile.IsDualMode ? profile.ActiveSide : PanelSide.Left;
        vm.RequestSessionSave();
    }

    private static async Task RestorePanelTabsAsync(PanelViewModel panel, PanelState state)
    {
        if (state.Tabs.Count == 0)
            return;

        var oldTabs = panel.Tabs.ToList();
        foreach (var tabState in state.Tabs)
        {
            var clone = new TabState
            {
                Id = Guid.NewGuid(),
                Path = tabState.Path,
                Pinned = tabState.Pinned,
                Locked = tabState.Locked,
                ShowHidden = tabState.ShowHidden,
                ViewMode = tabState.ViewMode,
                Sort = tabState.Sort,
                Filter = tabState.Filter,
                FocusedName = tabState.FocusedName,
                Back = [.. tabState.Back],
                Forward = [.. tabState.Forward],
            };
            await panel.AddTabFromStateAsync(clone, activate: false);
        }

        // Remove previous tabs
        foreach (var old in oldTabs)
        {
            if (panel.Tabs.Count > 1)
                await panel.CloseTabAsync(old, forceIfPinned: true);
        }

        int targetIdx = Math.Clamp(state.ActiveIndex, 0, Math.Max(0, panel.Tabs.Count - 1));
        panel.GoToTab(targetIdx);
    }

    private static string GetStoreFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_customStorePath)) return _customStorePath;
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModernExplorer");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "workspaces.json");
    }

    private static void EnsureLoaded()
    {
        if (_cached is not null) return;
        _cached = [];
        try
        {
            string file = GetStoreFilePath();
            if (!File.Exists(file)) return;
            string json = File.ReadAllText(file);
            var list = JsonSerializer.Deserialize<List<WorkspaceProfile>>(json);
            if (list is not null)
                _cached.AddRange(list);
        }
        catch { }
    }

    private static void PersistInternal()
    {
        try
        {
            string file = GetStoreFilePath();
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_cached, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }
}
