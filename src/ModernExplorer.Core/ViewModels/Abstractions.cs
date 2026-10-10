using ModernExplorer.Core.FileSystem;
using ModernExplorer.Core.Session;

namespace ModernExplorer.Core.ViewModels;

/// <summary>Executes actions on the UI thread.</summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    void Post(Action action);
}

/// <summary>Synchronous dispatcher for unit tests.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => true;
    public void Post(Action action) => action();
}

public sealed record NewFileRequest(string Name, FileTemplate Template, bool OpenAfterCreate);

/// <summary>
/// UI-implemented interactions (dialogs, focus, clipboard, windows).
/// Keeps ViewModels free of WinUI dependencies.
/// </summary>
public interface IShellUi
{
    Task<NewFileRequest?> ShowNewFileDialogAsync(string directory, IReadOnlyList<FileTemplate> templates,
        FileTemplate selected, Func<FileTemplate, string> suggestName, Func<string, string?> validate);

    Task<string?> ShowNewFolderDialogAsync(string directory, Func<string, string?> validate);

    /// <summary>Text input dialog; <paramref name="selectLength"/> selects the initial characters (e.g. stem without extension).</summary>
    Task<string?> PromptAsync(string title, string label, string initial, int selectLength, Func<string, string?>? validate = null);

    Task<bool> ConfirmAsync(string title, string message, string primaryText, bool destructive = false);

    void ShowMessage(string title, string message);

    void FocusFileList(PanelSide side);
    void FocusAddressBar();
    void FocusFilter();
    void ToggleFullScreen();
    void ShowCommandPalette();
    void ShowHelp();
    void OpenNewWindow(TabState tab);
    void OpenViewer(string path);
    void OpenEditor(string path);

    void SetClipboardText(string text);
    Task SetClipboardFilesAsync(IReadOnlyList<string> paths, bool cut);
    Task<(IReadOnlyList<string> Paths, bool Cut)?> GetClipboardFilesAsync();
}

