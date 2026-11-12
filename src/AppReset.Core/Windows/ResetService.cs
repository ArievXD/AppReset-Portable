using System.Runtime.Versioning;

namespace AppReset.Core.Windows;

public sealed record ResetOutcome(BackupInfo? Backup, int Removed, IReadOnlyList<ItemFailure> Failures);

/// <summary>Backs up the chosen items, then removes them.</summary>
[SupportedOSPlatform("windows")]
public sealed class ResetService
{
    private readonly ScanRoots _roots;

    public ResetService(ScanRoots roots) => _roots = roots;

    public ResetOutcome Run(
        InstalledApp app,
        IReadOnlyList<DataItem> items,
        bool makeBackup,
        bool isAdministrator,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ItemFailure>();
        var toRemove = new List<DataItem>();

        // Checked again here, not only in the scanner and the UI.
        foreach (var item in items)
        {
            if (!SafetyRules.IsAllowed(item, _roots, app))
            {
                failures.Add(new ItemFailure(item.Path, FailureKind.Blocked, FailureStage.Remove));
            }
            else if (item.RequiresAdmin && !isAdministrator)
            {
                failures.Add(new ItemFailure(item.Path, FailureKind.NeedsAdmin, FailureStage.Remove));
            }
            else
            {
                toRemove.Add(item);
            }
        }

        BackupInfo? backup = null;
        if (makeBackup && toRemove.Count > 0)
        {
            (backup, var backedUp, var backupFailures) = BackupStore.Create(app, toRemove, progress, cancellationToken);
            failures.AddRange(backupFailures);

            // Anything that could not be backed up is left exactly as it is.
            toRemove = backedUp;
        }

        var removed = 0;
        for (var i = 0; i < toRemove.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = toRemove[i];
            progress?.Report(new ProgressInfo(i, toRemove.Count, item.Path));
            try
            {
                if (item.Kind == ItemKind.Folder)
                {
                    FileAndRegistryOps.DeleteDirectory(item.Path);
                }
                else
                {
                    FileAndRegistryOps.DeleteRegistryKey(item.Path);
                }

                removed++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add(ItemException.ToFailure(item.Path, FailureStage.Remove, e));
            }
        }

        progress?.Report(new ProgressInfo(toRemove.Count, toRemove.Count, string.Empty));
        return new ResetOutcome(backup, removed, failures);
    }
}
