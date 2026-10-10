using ModernExplorer.Core.Commands;
using ModernExplorer.Core.Input;

namespace ModernExplorer.Core.Tests;

public sealed class KeyRouterTests
{
    [Fact]
    public void KeyChord_ParsesGermanAndStandardSyntax()
    {
        var c1 = KeyChord.Parse("Strg+Umschalt+T");
        var c2 = KeyChord.Parse("Ctrl+Shift+T");
        Assert.Equal(c1, c2);
        Assert.Equal("Strg+Shift+T", c1.ToDisplayString());
    }

    [Fact]
    public void ReservedKeys_AreNeverHandledByRouter()
    {
        var store = KeyBindingStore.LoadDefaultsWithUserOverride(null);
        var reg = new CommandRegistry();
        var router = new KeyRouter(store, reg);
        var ctx = FocusContext.For(ContextFlags.FileList);

        Assert.Equal(KeyRouteResult.NotHandled, router.Handle(KeyChord.Parse("Alt+F4"), ctx));
        Assert.Equal(KeyRouteResult.NotHandled, router.Handle(KeyChord.Parse("Win+E"), ctx));
        Assert.Equal(KeyRouteResult.NotHandled, router.Handle(KeyChord.Parse("Shift+F10"), ctx));
    }

    [Fact]
    public void F7_ExecutesCreateNewFile_AndShiftF7_ExecutesCreateNewFolder()
    {
        var store = KeyBindingStore.LoadDefaultsWithUserOverride(null);
        var reg = new CommandRegistry();
        string? executed = null;
        reg.Register("file.createNew", "Neue Datei", "Datei", _ => executed = "file.createNew");
        reg.Register("folder.createNew", "Neuer Ordner", "Datei", _ => executed = "folder.createNew");

        var router = new KeyRouter(store, reg);
        var ctx = FocusContext.For(ContextFlags.FileList);

        Assert.Equal(KeyRouteResult.Executed, router.Handle(KeyChord.Parse("F7"), ctx));
        Assert.Equal("file.createNew", executed);

        Assert.Equal(KeyRouteResult.Executed, router.Handle(KeyChord.Parse("Shift+F7"), ctx));
        Assert.Equal("folder.createNew", executed);
    }

    [Fact]
    public void ChordSequence_WaitsForSecondKey_ThenExecutes()
    {
        var store = KeyBindingStore.LoadDefaultsWithUserOverride(null);
        var reg = new CommandRegistry();
        bool pinned = false;
        reg.Register("tab.togglePin", "Pin", "Tabs", _ => pinned = true);

        var router = new KeyRouter(store, reg);
        var ctx = FocusContext.For(ContextFlags.FileList);

        var first = router.Handle(KeyChord.Parse("Ctrl+K"), ctx);
        Assert.Equal(KeyRouteResult.PendingChord, first);
        Assert.NotNull(router.PendingDisplay);
        Assert.False(pinned);

        var second = router.Handle(KeyChord.Parse("Ctrl+P"), ctx);
        Assert.Equal(KeyRouteResult.Executed, second);
        Assert.Null(router.PendingDisplay);
        Assert.True(pinned);
    }

    [Fact]
    public void TextInputProtection_BlocksNormalKeys_AllowsExplicitlyPermitted()
    {
        var store = KeyBindingStore.LoadDefaultsWithUserOverride(null);
        var reg = new CommandRegistry();
        string? executed = null;
        reg.Register("file.createNew", "Neue Datei", "Datei", _ => executed = "file.createNew");
        reg.Register("tab.next", "Nächster Tab", "Tabs", _ => executed = "tab.next");

        var router = new KeyRouter(store, reg);
        var textCtx = FocusContext.ForTextInput(ContextFlags.FileList);

        Assert.Equal(KeyRouteResult.NotHandled, router.Handle(KeyChord.Parse("F7"), textCtx));
        Assert.Null(executed);

        Assert.Equal(KeyRouteResult.Executed, router.Handle(KeyChord.Parse("Ctrl+Tab"), textCtx));
        Assert.Equal("tab.next", executed);
    }

    [Fact]
    public void UserOverride_CanRemoveAndReplaceDefaultBinding()
    {
        var store = new KeyBindingStore();
        store.LoadFromJson(
            """
            {
              "version": 1,
              "bindings": [
                { "key": "F7", "command": "file.createNew", "when": "fileList" }
              ]
            }
            """,
            """
            {
              "version": 1,
              "bindings": [
                { "key": "F7", "command": "-file.createNew", "when": "fileList" },
                { "key": "Ctrl+N", "command": "file.createNew", "when": "fileList" }
              ]
            }
            """);

        var reg = new CommandRegistry();
        int count = 0;
        reg.Register("file.createNew", "Neue Datei", "Datei", _ => count++);
        var router = new KeyRouter(store, reg);
        var ctx = FocusContext.For(ContextFlags.FileList);

        Assert.Equal(KeyRouteResult.NotHandled, router.Handle(KeyChord.Parse("F7"), ctx));
        Assert.Equal(KeyRouteResult.Executed, router.Handle(KeyChord.Parse("Ctrl+N"), ctx));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Tab_AlwaysSwitchesPanels_EvenOutsideFileListFocus()
    {
        var store = KeyBindingStore.LoadDefaultsWithUserOverride(null);
        var reg = new CommandRegistry();
        int switchCount = 0;
        reg.Register("panel.switch", "Panel wechseln", "Navigation", _ => switchCount++);

        var router = new KeyRouter(store, reg);
        var nonFileListCtx = FocusContext.For(ContextFlags.Panel);

        Assert.Equal(KeyRouteResult.Executed, router.Handle(KeyChord.Parse("Tab"), nonFileListCtx));
        Assert.Equal(KeyRouteResult.Executed, router.Handle(KeyChord.Parse("Shift+Tab"), nonFileListCtx));
        Assert.Equal(2, switchCount);
    }
}

