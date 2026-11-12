using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppReset.Core;

/// <summary>Saved as backup.json inside every backup folder.</summary>
public sealed record BackupManifest
{
    public int FormatVersion { get; init; } = 1;
    public required string AppName { get; init; }
    public string? AppVersion { get; init; }
    public string? AppId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required List<BackupEntry> Entries { get; init; }

    public const string FileName = "backup.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static BackupManifest? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record BackupEntry
{
    public required ItemKind Kind { get; init; }
    public required DataLocation Location { get; init; }
    public required string OriginalPath { get; init; }

    /// <summary>Folder or .reg file name inside the backup's "items" folder.</summary>
    public required string StoredAs { get; init; }

    public long? SizeBytes { get; init; }

    public bool RequiresAdmin => Location is DataLocation.ProgramData or DataLocation.MachineRegistry;
}

/// <summary>A backup found on disk.</summary>
public sealed record BackupInfo(string Folder, BackupManifest Manifest, long SizeBytes)
{
    public string ItemsFolder => Path.Combine(Folder, "items");
}

/// <summary>Something that went wrong with one item. The rest still runs.</summary>
/// <param name="Stage">Which step failed. A failed backup means the item was left untouched.</param>
/// <param name="Detail">The system's own error text when there is one. Windows already translates it.</param>
public sealed record ItemFailure(string Path, FailureKind Kind, FailureStage Stage, string? Detail = null);

public enum FailureStage
{
    Backup,
    Remove,
    Restore,
}

public enum FailureKind
{
    /// <summary>The safety rules refused the path.</summary>
    Blocked,
    /// <summary>A file is open in another program.</summary>
    InUse,
    AccessDenied,
    /// <summary>Machine-wide data and AppReset is not running as administrator.</summary>
    NeedsAdmin,
    /// <summary>The folder contains a junction or symbolic link.</summary>
    ContainsLink,
    /// <summary>The backup is missing the saved copy of this item.</summary>
    MissingFromBackup,
    Other,
}

/// <summary>Thrown inside the services for a failure that has a known kind.</summary>
public sealed class ItemException(FailureKind kind, string? detail = null) : Exception(detail ?? kind.ToString())
{
    public FailureKind Kind { get; } = kind;
    public string? Detail { get; } = detail;

    /// <summary>Turns any exception from a file or registry operation into an ItemFailure.</summary>
    public static ItemFailure ToFailure(string path, FailureStage stage, Exception e) => e switch
    {
        ItemException known => new ItemFailure(path, known.Kind, stage, known.Detail),
        UnauthorizedAccessException or System.Security.SecurityException => new ItemFailure(path, FailureKind.AccessDenied, stage, e.Message),
        // 32 = sharing violation, 33 = lock violation.
        IOException io when (io.HResult & 0xFFFF) is 32 or 33 => new ItemFailure(path, FailureKind.InUse, stage, e.Message),
        _ => new ItemFailure(path, FailureKind.Other, stage, e.Message),
    };
}

public sealed record ProgressInfo(int Done, int Total, string CurrentPath);
