namespace ModernExplorer.Core.Input;

/// <summary>
/// Context in which a key was pressed. Created by the UI per key press
/// and evaluated by <see cref="WhenExpression"/>.
/// </summary>
public sealed class FocusContext
{
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase) { "global" };

    /// <summary>True when a text input control currently has focus.</summary>
    public bool IsTextInput { get; init; }

    public FocusContext Set(string flag, bool value = true)
    {
        if (value) _flags.Add(flag); else _flags.Remove(flag);
        return this;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public IReadOnlyCollection<string> Flags => _flags;

    public static FocusContext For(params string[] flags)
    {
        var ctx = new FocusContext();
        foreach (var f in flags) ctx.Set(f);
        return ctx;
    }

    public static FocusContext ForTextInput(params string[] flags)
    {
        var ctx = new FocusContext { IsTextInput = true };
        foreach (var f in flags) ctx.Set(f);
        return ctx;
    }
}

/// <summary>Well-known context flags.</summary>
public static class ContextFlags
{
    public const string Global = "global";
    public const string Panel = "panel";
    public const string FileList = "fileList";
    public const string AddressBar = "addressBar";
    public const string Dialog = "dialog";
    public const string Editor = "editor";
    public const string HasFocusedItem = "hasFocusedItem";
    public const string HasSelection = "hasSelection";
    public const string IsDual = "panel.isDual";
}

