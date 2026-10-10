using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModernExplorer.Core.Operations;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class OperationItemViewModel : ObservableObject
{
    private readonly OperationHandle _handle;
    private readonly IUiDispatcher _dispatcher;

    public Guid Id => _handle.Operation.Id;
    public string Title => _handle.Operation.Describe();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsPaused), nameof(CanPauseOrResume), nameof(CanCancel), nameof(PauseResumeLabel))]
    private OperationStatus _status;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _detailText = "In Warteschlange …";

    public bool IsRunning => Status == OperationStatus.Running;
    public bool IsPaused => Status == OperationStatus.Paused;
    public bool CanPauseOrResume => Status is OperationStatus.Running or OperationStatus.Paused;
    public bool CanCancel => Status is OperationStatus.Queued or OperationStatus.Running or OperationStatus.Paused;
    public string PauseResumeLabel => Status == OperationStatus.Paused ? "Fortsetzen" : "Pause";

    public OperationItemViewModel(OperationHandle handle, IUiDispatcher dispatcher)
    {
        _handle = handle;
        _dispatcher = dispatcher;
        Status = handle.Status;
        handle.ProgressChanged += (_, _) => _dispatcher.Post(Refresh);
        handle.StatusChanged += (_, _) => _dispatcher.Post(Refresh);
        Refresh();
    }

    [RelayCommand]
    public void TogglePause()
    {
        if (Status == OperationStatus.Paused) _handle.Resume();
        else if (Status == OperationStatus.Running) _handle.Pause();
    }

    [RelayCommand]
    public void Cancel() => _handle.Cancel();

    private void Refresh()
    {
        Status = _handle.Status;
        var p = _handle.Progress;
        ProgressPercent = Math.Round(p.Fraction * 100.0, 1);

        DetailText = Status switch
        {
            OperationStatus.Queued => "In Warteschlange …",
            OperationStatus.Paused => $"Pausiert · {p.FilesDone}/{p.FilesTotal}",
            OperationStatus.Completed => _handle.Errors.Count > 0 ? $"Fertig ({_handle.Errors.Count} Hinweise)" : "Abgeschlossen",
            OperationStatus.Cancelled => "Abgebrochen",
            OperationStatus.Failed => $"Fehlgeschlagen: {_handle.Error?.Message}",
            _ => FormatRunning(p),
        };
    }

    private static string FormatRunning(OperationProgress p)
    {
        var item = string.IsNullOrEmpty(p.CurrentItem) ? "" : $"{p.CurrentItem} · ";
        var speed = p.BytesPerSecond > 1 ? $" · {Format.Size((long)p.BytesPerSecond)}/s" : "";
        var eta = p.Eta is { } e ? $" · Rest {Format.Duration(e)}" : "";
        return $"{item}{p.FilesDone}/{p.FilesTotal} ({Format.Size(p.BytesDone)}/{Format.Size(p.BytesTotal)}){speed}{eta}";
    }
}
