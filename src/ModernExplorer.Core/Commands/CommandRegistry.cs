using System.Text.Json.Nodes;
using ModernExplorer.Core.Input;

namespace ModernExplorer.Core.Commands;

public sealed record CommandInfo(string Id, string Title, string Category);

public interface ICommandRegistry
{
    bool CanExecute(string id, FocusContext ctx);
    void Execute(string id, JsonObject? args, FocusContext ctx);
    IReadOnlyList<CommandInfo> All { get; }
}

/// <summary>
/// Central command registry — single source of truth for hotkeys, menus, and Command Palette.
/// </summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private sealed record Entry(CommandInfo Info, Func<CommandContext, Task> Handler, Func<FocusContext, bool>? CanExecute);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Raised when an unhandled exception occurs inside a command handler.</summary>
    public event EventHandler<(string Command, Exception Error)>? Failed;

    public IReadOnlyList<CommandInfo> All => _entries.Values.Select(e => e.Info).OrderBy(i => i.Category).ThenBy(i => i.Title).ToList();

    public CommandRegistry Register(string id, string title, string category,
        Func<CommandContext, Task> handler, Func<FocusContext, bool>? canExecute = null)
    {
        _entries[id] = new Entry(new CommandInfo(id, title, category), handler, canExecute);
        return this;
    }

    public CommandRegistry Register(string id, string title, string category,
        Action<CommandContext> handler, Func<FocusContext, bool>? canExecute = null)
        => Register(id, title, category, c => { handler(c); return Task.CompletedTask; }, canExecute);

    public bool IsRegistered(string id) => _entries.ContainsKey(id);

    public bool CanExecute(string id, FocusContext ctx) =>
        _entries.TryGetValue(id, out var e) && (e.CanExecute?.Invoke(ctx) ?? true);

    public void Execute(string id, JsonObject? args, FocusContext ctx)
    {
        if (!_entries.TryGetValue(id, out var e)) return;
        _ = RunAsync(e, new CommandContext(id, args, ctx));
    }

    private async Task RunAsync(Entry e, CommandContext ctx)
    {
        try { await e.Handler(ctx); }
        catch (Exception ex) { Failed?.Invoke(this, (e.Info.Id, ex)); }
    }
}

public sealed record CommandContext(string Command, JsonObject? Args, FocusContext Focus)
{
    public T Arg<T>(string name, T fallback)
    {
        var node = Args?[name];
        if (node is null) return fallback;
        try { return node.GetValue<T>(); } catch { return fallback; }
    }
}

