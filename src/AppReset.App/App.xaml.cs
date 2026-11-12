using System.Windows;
using System.Windows.Threading;
using AppReset.App.Infrastructure;
using AppReset.App.ViewModels;

namespace AppReset.App;

public partial class App : Application
{
    /// <summary>Passed when AppReset restarts itself as administrator, followed by the app id to reopen.</summary>
    public const string SelectArgument = "--select";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var settings = AppSettings.Load();
        ApplyTheme(Enum.TryParse<ThemeChoice>(settings.Theme, out var theme) ? theme : ThemeChoice.System);
        Loc.SetLanguage(settings.Language);

        string? selectAppId = null;
        var index = Array.IndexOf(e.Args, SelectArgument);
        if (index >= 0 && index + 1 < e.Args.Length)
        {
            selectAppId = e.Args[index + 1];
        }

        var viewModel = new MainViewModel(settings, selectAppId);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync();
    }

    /// <summary>Windows 11 Fluent styling, following the Windows setting or forced light or dark.</summary>
    public static void ApplyTheme(ThemeChoice choice)
    {
        Current.ThemeMode = choice switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            MainWindow,
            Loc.Format("Result.Unexpected", e.Exception.Message),
            Loc.Get("App.Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
