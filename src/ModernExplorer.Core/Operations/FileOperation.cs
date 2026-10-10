namespace ModernExplorer.Core.Operations;

public enum OpKind { Copy, Move, Delete, Rename }

public enum ConflictPolicy { Ask, Overwrite, Skip, Rename, KeepNewer }

public enum ConflictChoice { Overwrite, Skip, Rename, KeepNewer, Cancel }

public sealed record OperationOptions
{
    public ConflictPolicy Conflicts { get; init; } = ConflictPolicy.Ask;
    /// <summary>Only for <see cref="OpKind.Delete"/>: move to Recycle Bin instead of permanent deletion.</summary>
    public bool Recycle { get; init; } = true;
}

public sealed record FileOperation(OpKind Kind, IReadOnlyList<string> Sources, string? Destination, OperationOptions Options)
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>For <see cref="OpKind.Rename"/>: new file or folder name.</summary>
    public string? NewName { get; init; }

    public string Describe()
    {
        var what = Sources.Count == 1 ? $"„{Path.GetFileName(Sources[0].TrimEnd('\\'))}“" : $"{Sources.Count} Elemente";
        return Kind switch
        {
            OpKind.Copy => $"Kopiere {what} nach {Destination}",
            OpKind.Move => $"Verschiebe {what} nach {Destination}",
            OpKind.Delete => Options.Recycle ? $"Lösche {what} (Papierkorb)" : $"Lösche {what} endgültig",
            OpKind.Rename => $"Benenne {what} um in „{NewName}“",
            _ => what,
        };
    }
}

public sealed record ConflictInfo(string Source, string Destination, long SourceSize, DateTime SourceModified,
                                  long DestinationSize, DateTime DestinationModified);

public sealed record ConflictDecision(ConflictChoice Choice, bool ApplyToAll);

/// <summary>Implemented by the UI layer (conflict resolution dialog).</summary>
public interface IConflictResolver
{
    Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct);
}

public enum OperationStatus { Queued, Running, Paused, Completed, Cancelled, Failed }

public sealed record OperationProgress(long BytesDone, long BytesTotal, int FilesDone, int FilesTotal,
                                       string CurrentItem, double BytesPerSecond)
{
    public double Fraction => BytesTotal > 0 ? Math.Clamp((double)BytesDone / BytesTotal, 0, 1)
                            : FilesTotal > 0 ? Math.Clamp((double)FilesDone / FilesTotal, 0, 1) : 0;

    public TimeSpan? Eta => BytesPerSecond > 1 && BytesTotal > BytesDone
        ? TimeSpan.FromSeconds((BytesTotal - BytesDone) / BytesPerSecond) : null;

    public static readonly OperationProgress Empty = new(0, 0, 0, 0, "", 0);
}

