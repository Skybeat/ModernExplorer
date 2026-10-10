using CommunityToolkit.Mvvm.ComponentModel;

namespace ModernExplorer.Core.ViewModels;

public sealed partial class DriveItemViewModel : ObservableObject
{
    public string RootPath { get; }
    public string DriveCode { get; }
    public string VolumeLabel { get; }
    public DriveType DriveType { get; }
    public string DisplayText { get; }
    public string ToolTip { get; }
    public SettingsViewModel Settings => SettingsViewModel.Current;

    [ObservableProperty]
    private bool _isSelected;

    public DriveItemViewModel(string rootPath, string driveCode, string volumeLabel, DriveType driveType, string displayText, string toolTip)
    {
        RootPath = rootPath;
        DriveCode = driveCode;
        VolumeLabel = volumeLabel;
        DriveType = driveType;
        DisplayText = displayText;
        ToolTip = toolTip;
    }

    public static IReadOnlyList<DriveItemViewModel> LoadSystemDrives()
    {
        var list = new List<DriveItemViewModel>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                var root = d.Name; // e.g. "C:\"
                var code = root.TrimEnd('\\'); // e.g. "C:"
                if (d.IsReady)
                {
                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel.Trim();
                    var display = string.IsNullOrEmpty(label) ? code : $"{code} {label}";
                    var tip = $"{root}" + (string.IsNullOrEmpty(label) ? "" : $" ({label})") +
                              $" · {Format.Size(d.AvailableFreeSpace)} frei von {Format.Size(d.TotalSize)}";
                    list.Add(new DriveItemViewModel(root, code, label, d.DriveType, display, tip));
                }
                else
                {
                    list.Add(new DriveItemViewModel(root, code, "", d.DriveType, code, $"{root} (Nicht bereit)"));
                }
            }
            catch
            {
                // Skip inaccessible drive
            }
        }
        return list;
    }
}

