using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModernExplorer.Core.Localization;
using ModernExplorer.Core.Operations;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class OperationItemViewModel : ObservableObject
{
    private readonly OperationHandle _handle;
    private readonly IUiDispatcher _dispatcher;

    public Guid Id => _handle.Operation.Id;
    public string Title => _handle.Operation.Describe();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsPaused), nameof(CanPauseOrResume), nameof(CanCancel), nameof(PauseResumeLabel), nameof(CancelLabel))]
    private OperationStatus _status;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _detailText = LocalizationService.T("Ops.Queued");

    public bool IsRunning => Status == OperationStatus.Running;
    public bool IsPaused => Status == OperationStatus.Paused;
    public bool CanPauseOrResume => Status is OperationStatus.Running or OperationStatus.Paused;
    public bool CanCancel => Status is OperationStatus.Queued or OperationStatus.Running or OperationStatus.Paused;
    public string PauseResumeLabel => Status == OperationStatus.Paused ? LocalizationService.T("Ops.Resume") : LocalizationService.T("Ops.Pause");
    public string CancelLabel => LocalizationService.T("Ops.Cancel");

    public OperationItemViewModel(OperationHandle handle, IUiDispatcher dispatcher)
    {
        _handle = handle;
        _dispatcher = dispatcher;
        Status = handle.Status;
        handle.ProgressChanged += (_, _) => _dispatcher.Post(Refresh);
        handle.StatusChanged += (_, _) => _dispatcher.Post(Refresh);
        LocalizationService.Instance.LanguageChanged += (_, _) => _dispatcher.Post(() =>
        {
            Refresh();
            OnPropertyChanged(nameof(PauseResumeLabel));
            OnPropertyChanged(nameof(CancelLabel));
            OnPropertyChanged(nameof(Title));
        });
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
            OperationStatus.Queued => LocalizationService.T("Ops.Queued"),
            OperationStatus.Paused => LocalizationService.Format("Ops.Paused", p.FilesDone, p.FilesTotal),
            OperationStatus.Completed => _handle.Errors.Count > 0
                ? LocalizationService.Format("Ops.CompletedWithWarnings", _handle.Errors.Count)
                : LocalizationService.T("Ops.Completed"),
            OperationStatus.Cancelled => LocalizationService.T("Ops.Cancelled"),
            OperationStatus.Failed => LocalizationService.Format("Ops.Failed", _handle.Error?.Message ?? ""),
            _ => FormatRunning(p),
        };
    }

    private static string FormatRunning(OperationProgress p)
    {
        var item = string.IsNullOrEmpty(p.CurrentItem) ? "" : $"{p.CurrentItem} · ";
        var speed = p.BytesPerSecond > 1 ? $" · {Format.Size((long)p.BytesPerSecond)}/s" : "";
        var eta = p.Eta is { } e ? $" · {LocalizationService.Format("Ops.Remaining", Format.Duration(e))}" : "";
        return $"{item}{p.FilesDone}/{p.FilesTotal} ({Format.Size(p.BytesDone)}/{Format.Size(p.BytesTotal)}){speed}{eta}";
    }
}
