using System.Text.Json.Serialization;

namespace ModernExplorer.Core.Session;

public enum SortColumn { Name, Size, Modified, Type }

public sealed record SortSpec
{
    public SortColumn Column { get; init; } = SortColumn.Name;
    public bool Descending { get; init; }
    public bool FoldersFirst { get; init; } = true;
}

public sealed class TabState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    public bool Pinned { get; set; }
    public bool Locked { get; set; }
    public bool ShowHidden { get; set; }
    public SortSpec Sort { get; set; } = new();
    public string? Filter { get; set; }
    public string? FocusedName { get; set; }
    public List<string> Back { get; set; } = [];
    public List<string> Forward { get; set; } = [];
}

public sealed class PanelState
{
    public int ActiveIndex { get; set; }
    public List<TabState> Tabs { get; set; } = [];
}

public sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1400;
    public int Height { get; set; } = 850;
    public bool Maximized { get; set; }
}

public enum PanelSide { Left, Right }

public sealed class LayoutState
{
    public bool Dual { get; set; } = true;
    public double SplitRatio { get; set; } = 0.5;
    public PanelSide ActivePanel { get; set; } = PanelSide.Left;
}

public sealed class WindowState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WindowBounds? Bounds { get; set; }
    public LayoutState Layout { get; set; } = new();
    public PanelState Left { get; set; } = new();
    public PanelState Right { get; set; } = new();
}

public sealed class ClosedTab
{
    public TabState Tab { get; set; } = new();
    public PanelSide Panel { get; set; }
    public DateTime ClosedAt { get; set; }
}

public sealed class SessionState
{
    public int Version { get; set; } = 1;
    public DateTime SavedAt { get; set; }
    public List<WindowState> Windows { get; set; } = [];
    public List<ClosedTab> ClosedTabs { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Windows.Count == 0;
}

