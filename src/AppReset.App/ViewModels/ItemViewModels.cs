using AppReset.App.Infrastructure;
using AppReset.Core;

namespace AppReset.App.ViewModels;

/// <summary>A choice in the language or theme list.</summary>
public sealed class OptionViewModel(string value, string labelKey) : ObservableObject
{
    public string Value { get; } = value;
    public string Label => Loc.Get(labelKey);
}

/// <summary>One row in the installed apps list.</summary>
public sealed class AppItemViewModel(InstalledApp app) : ObservableObject
{
    public InstalledApp App { get; } = app;
    public string Name => App.DisplayName;

    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(App.Publisher))
            {
                parts.Add(App.Publisher);
            }

            if (!string.IsNullOrWhiteSpace(App.Version))
            {
                parts.Add(Loc.Format("Detail.Version", App.Version));
            }

            return string.Join(" · ", parts);
        }
    }

    public string? InstallLocation => string.IsNullOrWhiteSpace(App.InstallLocation) ? null : App.InstallLocation;

    public bool Matches(string[] terms) =>
        terms.All(t =>
            Name.Contains(t, StringComparison.CurrentCultureIgnoreCase)
            || (App.Publisher?.Contains(t, StringComparison.CurrentCultureIgnoreCase) ?? false));
}

/// <summary>One folder or registry key found by the scan.</summary>
public sealed class DataItemViewModel : ObservableObject
{
    private readonly Action _checkedChanged;
    private bool _isChecked;

    public DataItemViewModel(DataItem item, bool isAdministrator, Action checkedChanged)
    {
        Item = item;
        _checkedChanged = checkedChanged;
        CanSelect = !item.RequiresAdmin || isAdministrator;
        _isChecked = CanSelect && item.SelectedByDefault;
    }

    public DataItem Item { get; }
    public string Path => Item.Path;
    public bool IsFolder => Item.Kind == ItemKind.Folder;
    public bool CanSelect { get; }
    public bool ShowNeedsAdmin => !CanSelect;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value && CanSelect))
            {
                _checkedChanged();
            }
        }
    }

    public string Explanation =>
        Loc.Get("Strength." + Item.Strength) + ". " + Loc.Get("Reason." + Item.Reason) + ".";

    public string? Warning => Item.Location switch
    {
        DataLocation.MachineRegistry => Loc.Get("Warn.MachineRegistry"),
        DataLocation.ProgramData => Loc.Get("Warn.MachineWide"),
        _ => null,
    };

    public string SizeText => IsFolder ? Loc.Size(Item.SizeBytes) : string.Empty;

    public string OpenLabel => Loc.Format("Item.OpenName", Path);
}

/// <summary>Scan results under one heading, such as AppData\Roaming.</summary>
public sealed class DataGroupViewModel(DataLocation location, IReadOnlyList<DataItemViewModel> items) : ObservableObject
{
    public DataLocation Location { get; } = location;
    public string Label => Loc.Get("Location." + Location);
    public IReadOnlyList<DataItemViewModel> Items { get; } = items;

    public override void RefreshAll()
    {
        base.RefreshAll();
        foreach (var item in Items)
        {
            item.RefreshAll();
        }
    }
}

/// <summary>One saved backup on the Backups tab.</summary>
public sealed class BackupItemViewModel(BackupInfo info) : ObservableObject
{
    public BackupInfo Info { get; } = info;
    public string AppName => Info.Manifest.AppName;

    public string Meta => Loc.Format(
        "Backups.Meta",
        Loc.Date(Info.Manifest.CreatedAt),
        Info.Manifest.Entries.Count,
        Loc.Size(Info.SizeBytes));

    public bool NeedsAdmin => Info.Manifest.Entries.Any(e => e.RequiresAdmin);
}

public sealed record FailureLine(string Path, string Message);

/// <summary>The banner shown after a reset or a restore.</summary>
public sealed class ResultViewModel : ObservableObject
{
    private readonly bool _isRestore;
    private readonly string _appName;
    private readonly int _done;
    private readonly int _total;
    private readonly bool _backupRequested;
    private readonly IReadOnlyList<ItemFailure> _failures;
    private readonly string? _error;

    private ResultViewModel(
        bool isRestore,
        string appName,
        int done,
        int total,
        string? backupFolder,
        bool backupRequested,
        IReadOnlyList<ItemFailure> failures,
        string? error)
    {
        _isRestore = isRestore;
        _appName = appName;
        _done = done;
        _total = total;
        BackupFolder = backupFolder;
        _backupRequested = backupRequested;
        _failures = failures;
        _error = error;
    }

    public static ResultViewModel ForReset(string appName, int total, bool backupRequested, Core.Windows.ResetOutcome outcome) =>
        new(false, appName, outcome.Removed, total, outcome.Backup?.Folder, backupRequested, outcome.Failures, null);

    public static ResultViewModel ForRestore(string appName, int total, Core.Windows.RestoreOutcome outcome) =>
        new(true, appName, outcome.Restored, total, null, false, outcome.Failures, null);

    public static ResultViewModel ForError(bool isRestore, string appName, string message) =>
        new(isRestore, appName, 0, 0, null, false, [], message);

    public string? BackupFolder { get; }
    public bool IsClean => _error is null && _failures.Count == 0;
    public bool HasFailures => _failures.Count > 0;

    public string Title => _error is not null
        ? Loc.Get(_isRestore ? "Result.RestoreErrorTitle" : "Result.ResetErrorTitle")
        : Loc.Get(_isRestore ? "Result.RestoreTitle" : "Result.ResetTitle");

    public string Summary
    {
        get
        {
            if (_error is not null)
            {
                return _error;
            }

            if (_isRestore)
            {
                return Loc.Format("Result.Restored", _done, _total, _appName);
            }

            var summary = Loc.Format("Result.Removed", _done, _total);
            if (BackupFolder is not null)
            {
                summary += " " + Loc.Get("Result.BackupSaved");
            }
            else if (!_backupRequested)
            {
                summary += " " + Loc.Get("Result.NoBackup");
            }

            return summary;
        }
    }

    public string? NextStep => !_isRestore && _done > 0 && _error is null
        ? Loc.Format("Result.NextStep", _appName)
        : null;

    public string FailuresHeader => Loc.Get(_isRestore ? "Result.NotRestored" : "Result.NotRemoved");

    public IReadOnlyList<FailureLine> Failures =>
        _failures.Select(f => new FailureLine(f.Path, Describe(f))).ToList();

    public static string Describe(ItemFailure failure)
    {
        var text = Loc.Get("Failure." + failure.Kind);
        if ((failure.Kind is FailureKind.InUse or FailureKind.AccessDenied or FailureKind.Other)
            && !string.IsNullOrWhiteSpace(failure.Detail))
        {
            text += " (" + failure.Detail.Trim() + ")";
        }

        if (failure.Stage == FailureStage.Backup)
        {
            text += " " + Loc.Get("Failure.LeftInPlace");
        }

        return text;
    }
}
