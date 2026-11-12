using System.Runtime.Versioning;

namespace AppReset.Core.Windows;

public sealed record RestoreOutcome(int Restored, IReadOnlyList<ItemFailure> Failures);

/// <summary>Creates, lists, restores and deletes backups in AppResetPaths.BackupsFolder.</summary>
[SupportedOSPlatform("windows")]
public static class BackupStore
{
    /// <summary>
    /// Copies every item into a new backup folder. Returns the items that were saved
    /// completely; only those may be removed afterwards.
    /// </summary>
    public static (BackupInfo? Backup, List<DataItem> BackedUp, List<ItemFailure> Failures) Create(
        InstalledApp app,
        IReadOnlyList<DataItem> items,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTimeOffset.Now;
        var folder = UniqueFolder(createdAt, app.DisplayName);
        var itemsFolder = Path.Combine(folder, "items");
        Directory.CreateDirectory(itemsFolder);

        var entries = new List<BackupEntry>();
        var backedUp = new List<DataItem>();
        var failures = new List<ItemFailure>();

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            progress?.Report(new ProgressInfo(i, items.Count, item.Path));

            var storedAs = item.Kind == ItemKind.Folder ? $"item-{i + 1:D2}" : $"item-{i + 1:D2}.reg";
            var target = Path.Combine(itemsFolder, storedAs);
            try
            {
                if (item.Kind == ItemKind.Folder)
                {
                    if (!Directory.Exists(item.Path))
                    {
                        continue;
                    }

                    FileAndRegistryOps.CopyDirectory(item.Path, target, cancellationToken);
                }
                else
                {
                    if (!FileAndRegistryOps.RegistryKeyExists(item.Path))
                    {
                        continue;
                    }

                    FileAndRegistryOps.ExportRegistryKey(item.Path, target);
                }

                entries.Add(new BackupEntry
                {
                    Kind = item.Kind,
                    Location = item.Location,
                    OriginalPath = item.Path,
                    StoredAs = storedAs,
                    SizeBytes = item.SizeBytes,
                });
                backedUp.Add(item);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add(ItemException.ToFailure(item.Path, FailureStage.Backup, e));
                TryDelete(target);
            }
        }

        if (entries.Count == 0)
        {
            TryDelete(folder);
            return (null, backedUp, failures);
        }

        var manifest = new BackupManifest
        {
            AppName = app.DisplayName,
            AppVersion = app.Version,
            AppId = app.Id,
            CreatedAt = createdAt,
            Entries = entries,
        };
        File.WriteAllText(Path.Combine(folder, BackupManifest.FileName), manifest.ToJson());

        return (new BackupInfo(folder, manifest, FolderSize(folder)), backedUp, failures);
    }

    public static IReadOnlyList<BackupInfo> List()
    {
        var root = AppResetPaths.BackupsFolder;
        if (!Directory.Exists(root))
        {
            return [];
        }

        var backups = new List<BackupInfo>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var manifestPath = Path.Combine(folder, BackupManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = BackupManifest.FromJson(File.ReadAllText(manifestPath));
            if (manifest is not null)
            {
                backups.Add(new BackupInfo(folder, manifest, FolderSize(folder)));
            }
        }

        return backups.OrderByDescending(b => b.Manifest.CreatedAt).ToList();
    }

    /// <summary>
    /// Puts every saved item back. The current folder or key is replaced by the saved copy,
    /// so the app returns to exactly how it was before that reset.
    /// </summary>
    public static RestoreOutcome Restore(
        BackupInfo backup,
        ScanRoots roots,
        bool isAdministrator,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ItemFailure>();
        var restored = 0;
        var entries = backup.Manifest.Entries;

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            progress?.Report(new ProgressInfo(i, entries.Count, entry.OriginalPath));

            // backup.json is a plain file on disk, so its paths are checked like anything else.
            var allowed = entry.Kind == ItemKind.Folder
                ? SafetyRules.IsFolderAllowed(entry.OriginalPath, roots, app: null)
                : SafetyRules.IsRegistryKeyAllowed(entry.OriginalPath);
            if (!allowed || !IsPlainName(entry.StoredAs))
            {
                failures.Add(new ItemFailure(entry.OriginalPath, FailureKind.Blocked, FailureStage.Restore));
                continue;
            }

            if (entry.RequiresAdmin && !isAdministrator)
            {
                failures.Add(new ItemFailure(entry.OriginalPath, FailureKind.NeedsAdmin, FailureStage.Restore));
                continue;
            }

            var stored = Path.Combine(backup.ItemsFolder, entry.StoredAs);
            try
            {
                if (entry.Kind == ItemKind.Folder)
                {
                    if (!Directory.Exists(stored))
                    {
                        throw new ItemException(FailureKind.MissingFromBackup);
                    }

                    FileAndRegistryOps.DeleteDirectory(entry.OriginalPath);
                    FileAndRegistryOps.CopyDirectory(stored, entry.OriginalPath, cancellationToken);
                }
                else
                {
                    if (!File.Exists(stored))
                    {
                        throw new ItemException(FailureKind.MissingFromBackup);
                    }

                    FileAndRegistryOps.DeleteRegistryKey(entry.OriginalPath);
                    FileAndRegistryOps.ImportRegistryFile(stored);
                }

                restored++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add(ItemException.ToFailure(entry.OriginalPath, FailureStage.Restore, e));
            }
        }

        progress?.Report(new ProgressInfo(entries.Count, entries.Count, string.Empty));
        return new RestoreOutcome(restored, failures);
    }

    /// <summary>Permanently deletes one backup folder.</summary>
    public static void Delete(BackupInfo backup)
    {
        if (!PathText.IsUnder(backup.Folder, AppResetPaths.BackupsFolder))
        {
            throw new ItemException(FailureKind.Blocked);
        }

        FileAndRegistryOps.DeleteDirectory(backup.Folder);
    }

    private static bool IsPlainName(string name) =>
        name.Length > 0
        && name.IndexOfAny(['\\', '/', ':']) < 0
        && name is not "." and not "..";

    private static string UniqueFolder(DateTimeOffset createdAt, string appName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(appName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (safeName.Length > 50)
        {
            safeName = safeName[..50].Trim();
        }

        var baseName = $"{createdAt:yyyyMMdd-HHmmss} {safeName}";
        var folder = Path.Combine(AppResetPaths.BackupsFolder, baseName);
        for (var n = 2; Directory.Exists(folder); n++)
        {
            folder = Path.Combine(AppResetPaths.BackupsFolder, $"{baseName} ({n})");
        }

        return folder;
    }

    private static long FolderSize(string folder)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            return new DirectoryInfo(folder).EnumerateFiles("*", options).Sum(f => f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                FileAndRegistryOps.DeleteDirectory(path);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ItemException)
        {
            // Leftovers inside AppReset's own backup folder are harmless.
        }
    }
}
