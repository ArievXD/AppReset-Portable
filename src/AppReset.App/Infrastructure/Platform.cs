using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using AppReset.Core;

namespace AppReset.App.Infrastructure;

/// <summary>Windows-specific helpers the view models use.</summary>
public static class Platform
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Starts AppReset again with administrator rights and the same app selected.
    /// Returns false when the person cancels the Windows prompt.
    /// </summary>
    public static bool RestartAsAdministrator(string? selectAppId)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return false;
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        if (selectAppId is not null)
        {
            info.ArgumentList.Add(App.SelectArgument);
            info.ArgumentList.Add(selectAppId);
        }

        try
        {
            Process.Start(info);
            return true;
        }
        catch (Win32Exception)
        {
            // 1223: the person chose "No" on the administrator prompt.
            return false;
        }
    }

    public static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        info.ArgumentList.Add(path);
        Process.Start(info);
    }
}

/// <summary>Language and theme, saved in AppReset's own folder.</summary>
public sealed record AppSettings(string Language, string Theme)
{
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppResetPaths.SettingsFile))
            {
                var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppResetPaths.SettingsFile));
                if (saved is not null)
                {
                    return saved;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall back to the defaults below.
        }

        return new AppSettings(Loc.DefaultLanguage(), ThemeChoice.System.ToString());
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppResetPaths.DataFolder);
            File.WriteAllText(AppResetPaths.SettingsFile, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saving a preference is not worth interrupting the person for.
        }
    }
}

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>Native Windows message boxes, owned by the main window.</summary>
public static class Dialogs
{
    public static bool Confirm(string title, string message) =>
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public static void Inform(string title, string message) =>
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}
