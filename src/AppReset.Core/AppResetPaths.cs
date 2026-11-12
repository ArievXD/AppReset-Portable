namespace AppReset.Core;

/// <summary>Where AppReset keeps its own files: settings and backups.</summary>
public static class AppResetPaths
{
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppReset");

    public static string BackupsFolder { get; } = Path.Combine(DataFolder, "Backups");

    public static string SettingsFile { get; } = Path.Combine(DataFolder, "settings.json");

    /// <summary>The standard Windows data folders the scanner looks in.</summary>
    public static ScanRoots DetectRoots()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // LocalLow has no SpecialFolder value. It always sits next to Local.
        string? localLow = null;
        var appData = Path.GetDirectoryName(local);
        if (appData is not null)
        {
            var candidate = Path.Combine(appData, "LocalLow");
            if (Directory.Exists(candidate))
            {
                localLow = candidate;
            }
        }

        return new ScanRoots(roaming, local, localLow, programData, DataFolder);
    }
}
