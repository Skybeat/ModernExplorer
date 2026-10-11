using System.Diagnostics;
using System.IO;
using System.Text;

namespace ModernExplorer.Core.Features;

public sealed record QuickCommandOutput(
    string CommandText,
    string WorkingDirectory,
    int ExitCode,
    string OutputText,
    string? NavigateToDirectory = null,
    TimeSpan Elapsed = default)
{
    public string Command => CommandText;
    public string CombinedOutput => OutputText;
    public string? NavigatedDirectory => NavigateToDirectory;
}

/// <summary>
/// Executes inline shell commands (PowerShell / CMD / sh) in the active panel's working directory,
/// manages command history, and supports direct directory navigation via 'cd'.
/// </summary>
public sealed class QuickCommandRunnerService
{
    private readonly List<string> _history = [];
    private int _historyCursor = -1;

    public IReadOnlyList<string> History => _history;

    public void RecordCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        string trimmed = command.Trim();
        _history.RemoveAll(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase));
        _history.Add(trimmed);
        if (_history.Count > 100)
            _history.RemoveAt(0);
        _historyCursor = _history.Count;
    }

    public string? NavigateHistory(int delta) =>
        delta < 0 ? GetPreviousHistoryCommand() : GetNextHistoryCommand();

    public string? GetPreviousHistoryCommand()
    {
        if (_history.Count == 0) return null;
        _historyCursor = Math.Clamp(_historyCursor - 1, 0, _history.Count - 1);
        return _history[_historyCursor];
    }

    public string? GetNextHistoryCommand()
    {
        if (_history.Count == 0) return null;
        if (_historyCursor + 1 >= _history.Count)
        {
            _historyCursor = _history.Count;
            return "";
        }
        _historyCursor++;
        return _history[_historyCursor];
    }

    public async Task<QuickCommandOutput> ExecuteAsync(
        string commandText,
        string workingDirectory,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return new QuickCommandOutput("", workingDirectory, 0, "");

        string cmd = commandText.Trim();
        RecordCommand(cmd);

        string safeWorkDir = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.CurrentDirectory;

        // Intercept "cd <path>" or "chdir <path>" so the active explorer panel navigates directly
        if (cmd.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || string.Equals(cmd, "cd..", StringComparison.OrdinalIgnoreCase))
        {
            string arg = string.Equals(cmd, "cd..", StringComparison.OrdinalIgnoreCase)
                ? ".."
                : cmd[3..].Trim().Trim('"', '\'');

            try
            {
                string targetDir = Path.GetFullPath(Path.Combine(safeWorkDir, Environment.ExpandEnvironmentVariables(arg)));
                if (Directory.Exists(targetDir))
                {
                    return new QuickCommandOutput(cmd, targetDir, 0, $"→ {targetDir}", NavigateToDirectory: targetDir);
                }
                return new QuickCommandOutput(cmd, safeWorkDir, 1, $"Verzeichnis nicht gefunden: {targetDir}");
            }
            catch (Exception ex)
            {
                return new QuickCommandOutput(cmd, safeWorkDir, 1, ex.Message);
            }
        }

        return await Task.Run(async () =>
        {
            try
            {
                ProcessStartInfo psi;
                if (OperatingSystem.IsWindows())
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", cmd },
                        WorkingDirectory = safeWorkDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        ArgumentList = { "-c", cmd },
                        WorkingDirectory = safeWorkDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };
                }

                using var proc = Process.Start(psi);
                if (proc is null)
                    return new QuickCommandOutput(cmd, safeWorkDir, -1, "Prozess konnte nicht gestartet werden.");

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                string stdout = (await stdoutTask).TrimEnd();
                string stderr = (await stderrTask).TrimEnd();
                string combined = string.IsNullOrEmpty(stderr)
                    ? stdout
                    : (string.IsNullOrEmpty(stdout) ? stderr : $"{stdout}{Environment.NewLine}{stderr}");

                if (string.IsNullOrEmpty(combined))
                    combined = $"✓ Befehl ausgeführt (Exit-Code {proc.ExitCode})";

                return new QuickCommandOutput(cmd, safeWorkDir, proc.ExitCode, combined);
            }
            catch (Exception ex)
            {
                return new QuickCommandOutput(cmd, safeWorkDir, -1, $"Fehler: {ex.Message}");
            }
        }, ct);
    }
}
