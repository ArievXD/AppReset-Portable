using System.Collections.ObjectModel;
using System.Windows.Input;
using AppReset.App.Infrastructure;
using AppReset.Core;
using AppReset.Core.Windows;

namespace AppReset.App.ViewModels;

public enum LoadState
{
    Loading,
    Ready,
    Error,
}

public enum ScanState
{
    Idle,
    Scanning,
    Done,
    Error,
}

public sealed class MainViewModel : ObservableObject
{
    private readonly ScanRoots _roots = AppResetPaths.DetectRoots();
    private readonly DataScanner _scanner;
    private AppSettings _settings;
    private string? _appToSelect;

    private List<InstalledApp> _installed = [];
    private List<AppItemViewModel> _allApps = [];
    private CancellationTokenSource? _scanCancel;
    private IReadOnlyList<RunningProcess> _running = [];
    private bool _filtering;

    private LoadState _appsState = LoadState.Loading;
    private string? _appsError;
    private string _searchText = string.Empty;
    private AppItemViewModel? _selectedApp;
    private ScanState _scanState = ScanState.Idle;
    private string? _scanError;
    private bool _makeBackup = true;
    private bool _isWorking;
    private double _progressValue;
    private double _progressMaximum = 1;
    private string? _progressText;
    private string? _progressPath;
    private ResultViewModel? _resetResult;

    private LoadState _backupsState = LoadState.Loading;
    private string? _backupsError;
    private ResultViewModel? _restoreResult;

    private OptionViewModel _selectedLanguage;
    private OptionViewModel _selectedTheme;

    public MainViewModel(AppSettings settings, string? appToSelect)
    {
        _settings = settings;
        _appToSelect = appToSelect;
        _scanner = new DataScanner(_roots, new WindowsFileSystemView(), new WindowsRegistryView());
        IsAdministrator = Platform.IsAdministrator();

        Languages =
        [
            new OptionViewModel(Loc.English, "Lang.English"),
            new OptionViewModel(Loc.Indonesian, "Lang.Indonesian"),
        ];
        Themes =
        [
            new OptionViewModel(nameof(ThemeChoice.System), "Theme.System"),
            new OptionViewModel(nameof(ThemeChoice.Light), "Theme.Light"),
            new OptionViewModel(nameof(ThemeChoice.Dark), "Theme.Dark"),
        ];
        _selectedLanguage = Languages.FirstOrDefault(l => l.Value == settings.Language) ?? Languages[0];
        _selectedTheme = Themes.FirstOrDefault(t => t.Value == settings.Theme) ?? Themes[0];

        ReloadAppsCommand = new RelayCommand(() => _ = LoadAppsAsync(), () => !IsWorking);
        ScanAgainCommand = new RelayCommand(() => _ = ScanAsync(), () => SelectedApp is not null && !IsWorking);
        CheckRunningCommand = new RelayCommand(() => _ = CheckRunningAsync(), () => SelectedApp is not null);
        ResetCommand = new RelayCommand(() => _ = ResetAsync(), () => CanReset);
        RestartAsAdminCommand = new RelayCommand(RestartAsAdministrator, () => !IsWorking);
        OpenItemCommand = new RelayCommand(p => { if (p is DataItemViewModel item) Platform.OpenFolder(item.Path); });
        OpenFolderCommand = new RelayCommand(p => { if (p is string path) Platform.OpenFolder(path); });
        RefreshBackupsCommand = new RelayCommand(() => _ = LoadBackupsAsync(), () => !IsWorking);
        RestoreBackupCommand = new RelayCommand(p => _ = RestoreAsync(p as BackupItemViewModel), _ => !IsWorking);
        DeleteBackupCommand = new RelayCommand(p => _ = DeleteBackupAsync(p as BackupItemViewModel), _ => !IsWorking);
    }

    // ---------------------------------------------------------------- settings

    public bool IsAdministrator { get; }
    public IReadOnlyList<OptionViewModel> Languages { get; }
    public IReadOnlyList<OptionViewModel> Themes { get; }

    public OptionViewModel SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            Loc.SetLanguage(value.Value);
            SaveSettings();
            RefreshAllTexts();
        }
    }

    public OptionViewModel SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || !SetProperty(ref _selectedTheme, value))
            {
                return;
            }

            App.ApplyTheme(Enum.TryParse<ThemeChoice>(value.Value, out var theme) ? theme : ThemeChoice.System);
            SaveSettings();
        }
    }

    public string BackupsFolder => AppResetPaths.BackupsFolder;

    private void SaveSettings()
    {
        _settings = new AppSettings(SelectedLanguage.Value, SelectedTheme.Value);
        _settings.Save();
    }

    private void RefreshAllTexts()
    {
        RefreshAll();
        foreach (var option in Languages.Concat(Themes))
        {
            option.RefreshAll();
        }

        foreach (var app in _allApps)
        {
            app.RefreshAll();
        }

        foreach (var group in Groups)
        {
            group.RefreshAll();
        }

        foreach (var backup in Backups)
        {
            backup.RefreshAll();
        }

        ResetResult?.RefreshAll();
        RestoreResult?.RefreshAll();
    }

    // ---------------------------------------------------------------- apps list

    public ObservableCollection<AppItemViewModel> VisibleApps { get; } = [];

    public LoadState AppsState
    {
        get => _appsState;
        private set
        {
            if (SetProperty(ref _appsState, value))
            {
                OnPropertyChanged(nameof(ShowNoMatch));
            }
        }
    }

    public string? AppsError
    {
        get => _appsError;
        private set => SetProperty(ref _appsError, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public string AppCountText => Loc.Format("Apps.Count", VisibleApps.Count);
    public bool ShowNoMatch => AppsState == LoadState.Ready && VisibleApps.Count == 0 && _allApps.Count > 0;
    public string NoMatchText => Loc.Format("Apps.NoMatch", SearchText.Trim());

    public ICommand ReloadAppsCommand { get; }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(LoadAppsAsync(), LoadBackupsAsync());
    }

    private async Task LoadAppsAsync()
    {
        var keepId = SelectedApp?.App.Id ?? _appToSelect;
        AppsState = LoadState.Loading;
        try
        {
            _installed = (await Task.Run(InstalledAppReader.ReadAll)).ToList();
            _allApps = _installed.Select(a => new AppItemViewModel(a)).ToList();
            ApplyFilter();
            AppsState = LoadState.Ready;

            var restore = keepId is null ? null : _allApps.FirstOrDefault(a => a.App.Id == keepId);
            _appToSelect = null;
            if (restore is not null)
            {
                SelectedApp = restore;
            }
        }
        catch (Exception e)
        {
            AppsError = Loc.Format("Apps.Error", e.Message);
            AppsState = LoadState.Error;
        }
    }

    private void ApplyFilter()
    {
        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Rebuilding the list makes the ListBox drop its selection. Keep the app the person
        // is working on selected in the details pane anyway.
        _filtering = true;
        try
        {
            VisibleApps.Clear();
            foreach (var app in _allApps.Where(a => a.Matches(terms)))
            {
                VisibleApps.Add(app);
            }
        }
        finally
        {
            _filtering = false;
        }

        OnPropertyChanged(nameof(AppCountText));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(SelectedApp));
    }

    // ---------------------------------------------------------------- selected app

    public AppItemViewModel? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (_filtering && value is null)
            {
                return;
            }

            if (!SetProperty(ref _selectedApp, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelection));
            ResetResult = null;
            _running = [];
            NotifyRunningChanged();
            _ = CheckRunningAsync();
            _ = ScanAsync();
        }
    }

    public bool HasSelection => SelectedApp is not null;

    public bool IsRunning => _running.Count > 0;

    public string? RunningText => SelectedApp is null || _running.Count == 0
        ? null
        : Loc.Format("Running.Message", SelectedApp.Name, string.Join(", ", _running.Select(r => r.Name + ".exe")));

    public ICommand CheckRunningCommand { get; }

    private async Task CheckRunningAsync()
    {
        var app = SelectedApp;
        if (app is null)
        {
            return;
        }

        IReadOnlyList<RunningProcess> running;
        try
        {
            running = await Task.Run(() => ProcessChecker.FindRunning(app.App));
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            running = [];
        }

        if (SelectedApp == app)
        {
            _running = running;
            NotifyRunningChanged();
        }
    }

    private void NotifyRunningChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(RunningText));
        OnPropertyChanged(nameof(CanReset));
    }

    // ---------------------------------------------------------------- scan

    public ObservableCollection<DataGroupViewModel> Groups { get; } = [];

    public ScanState ScanState
    {
        get => _scanState;
        private set
        {
            if (SetProperty(ref _scanState, value))
            {
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(NothingFound));
                OnPropertyChanged(nameof(CanReset));
            }
        }
    }

    public string? ScanError
    {
        get => _scanError;
        private set => SetProperty(ref _scanError, value);
    }

    public bool HasItems => ScanState == ScanState.Done && Groups.Count > 0;
    public bool NothingFound => ScanState == ScanState.Done && Groups.Count == 0;
    public string NothingFoundTitle => Loc.Format("Scan.NothingTitle", SelectedApp?.Name);

    public int AdminItemCount => AllItems.Count(i => i.Item.RequiresAdmin);
    public bool ShowAdminBanner => !IsAdministrator && AdminItemCount > 0 && ScanState == ScanState.Done;
    public string AdminText => Loc.Format("Admin.Message", AdminItemCount);

    public ICommand ScanAgainCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand RestartAsAdminCommand { get; }

    private IEnumerable<DataItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    private async Task ScanAsync()
    {
        var app = SelectedApp;
        _scanCancel?.Cancel();
        Groups.Clear();
        NotifySelectionChanged();
        if (app is null)
        {
            ScanState = ScanState.Idle;
            return;
        }

        var cancel = new CancellationTokenSource();
        _scanCancel = cancel;
        ScanState = ScanState.Scanning;
        try
        {
            var installed = _installed;
            var items = await Task.Run(() => _scanner.Scan(app.App, installed, cancel.Token), cancel.Token);
            if (cancel.IsCancellationRequested || SelectedApp != app)
            {
                return;
            }

            foreach (var group in items.GroupBy(i => i.Location).OrderBy(g => g.Key))
            {
                var rows = group.Select(i => new DataItemViewModel(i, IsAdministrator, NotifySelectionChanged)).ToList();
                Groups.Add(new DataGroupViewModel(group.Key, rows));
            }

            ScanState = ScanState.Done;
        }
        catch (OperationCanceledException)
        {
            // A newer scan took over.
        }
        catch (Exception e)
        {
            if (SelectedApp == app)
            {
                ScanError = Loc.Format("Scan.Error", e.Message);
                ScanState = ScanState.Error;
            }
        }

        NotifySelectionChanged();
        OnPropertyChanged(nameof(NothingFoundTitle));
        OnPropertyChanged(nameof(AdminItemCount));
        OnPropertyChanged(nameof(AdminText));
        OnPropertyChanged(nameof(ShowAdminBanner));
    }

    private void RestartAsAdministrator()
    {
        if (Platform.RestartAsAdministrator(SelectedApp?.App.Id))
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    // ---------------------------------------------------------------- reset

    public bool MakeBackup
    {
        get => _makeBackup;
        set => SetProperty(ref _makeBackup, value);
    }

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (SetProperty(ref _isWorking, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanReset));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsIdle => !IsWorking;

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double ProgressMaximum
    {
        get => _progressMaximum;
        private set => SetProperty(ref _progressMaximum, value);
    }

    public string? ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string? ProgressPath
    {
        get => _progressPath;
        private set => SetProperty(ref _progressPath, value);
    }

    public ResultViewModel? ResetResult
    {
        get => _resetResult;
        private set => SetProperty(ref _resetResult, value);
    }

    private List<DataItemViewModel> CheckedItems => AllItems.Where(i => i.IsChecked).ToList();

    public string SelectionText
    {
        get
        {
            var items = CheckedItems;
            return items.Count == 0
                ? Loc.Get("Footer.SelectedNone")
                : Loc.Format("Footer.Selected", items.Count, Loc.Size(items.Sum(i => i.Item.SizeBytes ?? 0)));
        }
    }

    public string ResetButtonText => Loc.Format("Footer.Reset", SelectedApp?.Name);

    public bool CanReset =>
        !IsWorking && !IsRunning && ScanState == ScanState.Done && AllItems.Any(i => i.IsChecked);

    public ICommand ResetCommand { get; }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(ResetButtonText));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(NothingFound));
        CommandManager.InvalidateRequerySuggested();
    }

    private Progress<ProgressInfo> StartProgress() => new(p =>
    {
        ProgressMaximum = Math.Max(1, p.Total);
        ProgressValue = p.Done;
        ProgressText = Loc.Format("Progress.Step", Math.Min(p.Done + 1, Math.Max(1, p.Total)), Math.Max(1, p.Total));
        ProgressPath = p.CurrentPath;
    });

    private async Task ResetAsync()
    {
        var app = SelectedApp;
        var chosen = CheckedItems;
        if (app is null || chosen.Count == 0)
        {
            return;
        }

        var size = Loc.Size(chosen.Sum(i => i.Item.SizeBytes ?? 0));
        var message = Loc.Format(MakeBackup ? "Confirm.WithBackup" : "Confirm.NoBackup", chosen.Count, size)
            + "\n\n" + Loc.Get("Confirm.Continue");
        if (!Dialogs.Confirm(Loc.Format("Confirm.Title", app.Name), message))
        {
            return;
        }

        // The app may have been opened since the last check.
        await CheckRunningAsync();
        if (IsRunning || SelectedApp != app)
        {
            return;
        }

        var items = chosen.Select(i => i.Item).ToList();
        var makeBackup = MakeBackup;
        var progress = StartProgress();
        IsWorking = true;
        try
        {
            var outcome = await Task.Run(() => new ResetService(_roots).Run(app.App, items, makeBackup, IsAdministrator, progress));
            ResetResult = ResultViewModel.ForReset(app.Name, items.Count, makeBackup, outcome);
        }
        catch (Exception e)
        {
            ResetResult = ResultViewModel.ForError(false, app.Name, Loc.Format("Result.Unexpected", e.Message));
        }
        finally
        {
            IsWorking = false;
        }

        // Show what is left, and the new backup.
        var result = ResetResult;
        await Task.WhenAll(ScanAsync(), LoadBackupsAsync());
        ResetResult = result;
    }

    // ---------------------------------------------------------------- backups

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    public LoadState BackupsState
    {
        get => _backupsState;
        private set
        {
            if (SetProperty(ref _backupsState, value))
            {
                OnPropertyChanged(nameof(NoBackups));
                OnPropertyChanged(nameof(HasBackups));
            }
        }
    }

    public string? BackupsError
    {
        get => _backupsError;
        private set => SetProperty(ref _backupsError, value);
    }

    public bool NoBackups => BackupsState == LoadState.Ready && Backups.Count == 0;
    public bool HasBackups => BackupsState == LoadState.Ready && Backups.Count > 0;

    public ResultViewModel? RestoreResult
    {
        get => _restoreResult;
        private set => SetProperty(ref _restoreResult, value);
    }

    public ICommand RefreshBackupsCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand DeleteBackupCommand { get; }
    public ICommand OpenFolderCommand { get; }

    private async Task LoadBackupsAsync()
    {
        BackupsState = LoadState.Loading;
        try
        {
            var list = await Task.Run(BackupStore.List);
            Backups.Clear();
            foreach (var backup in list)
            {
                Backups.Add(new BackupItemViewModel(backup));
            }

            BackupsState = LoadState.Ready;
        }
        catch (Exception e)
        {
            BackupsError = Loc.Format("Backups.Error", e.Message);
            BackupsState = LoadState.Error;
        }

        OnPropertyChanged(nameof(NoBackups));
        OnPropertyChanged(nameof(HasBackups));
    }

    private async Task RestoreAsync(BackupItemViewModel? backup)
    {
        if (backup is null)
        {
            return;
        }

        var date = Loc.Date(backup.Info.Manifest.CreatedAt);
        if (!Dialogs.Confirm(
                Loc.Format("Backups.RestoreTitle", backup.AppName),
                Loc.Format("Backups.RestoreConfirm", backup.AppName, date) + "\n\n" + Loc.Get("Confirm.Continue")))
        {
            return;
        }

        var installed = _installed.FirstOrDefault(a => a.Id == backup.Info.Manifest.AppId);
        if (installed is not null)
        {
            var running = await Task.Run(() => ProcessChecker.FindRunning(installed));
            if (running.Count > 0)
            {
                Dialogs.Inform(
                    Loc.Format("Backups.RestoreTitle", backup.AppName),
                    Loc.Format("Running.Message", backup.AppName, string.Join(", ", running.Select(r => r.Name + ".exe"))));
                return;
            }
        }

        var progress = StartProgress();
        IsWorking = true;
        try
        {
            var outcome = await Task.Run(() => BackupStore.Restore(backup.Info, _roots, IsAdministrator, progress));
            RestoreResult = ResultViewModel.ForRestore(backup.AppName, backup.Info.Manifest.Entries.Count, outcome);
        }
        catch (Exception e)
        {
            RestoreResult = ResultViewModel.ForError(true, backup.AppName, Loc.Format("Result.Unexpected", e.Message));
        }
        finally
        {
            IsWorking = false;
        }

        if (SelectedApp is not null)
        {
            await ScanAsync();
        }
    }

    private async Task DeleteBackupAsync(BackupItemViewModel? backup)
    {
        if (backup is null)
        {
            return;
        }

        var date = Loc.Date(backup.Info.Manifest.CreatedAt);
        if (!Dialogs.Confirm(
                Loc.Format("Backups.DeleteTitle", backup.AppName),
                Loc.Format("Backups.DeleteConfirm", backup.AppName, date)))
        {
            return;
        }

        IsWorking = true;
        try
        {
            await Task.Run(() => BackupStore.Delete(backup.Info));
            RestoreResult = null;
        }
        catch (Exception e)
        {
            RestoreResult = ResultViewModel.ForError(true, backup.AppName, Loc.Format("Backups.DeleteFailed", e.Message));
        }
        finally
        {
            IsWorking = false;
        }

        await LoadBackupsAsync();
    }
}
