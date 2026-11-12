using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AppReset.Core.Windows;

/// <summary>Reads the same list Windows shows under Settings, Apps, Installed apps.</summary>
[SupportedOSPlatform("windows")]
public static partial class InstalledAppReader
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    [GeneratedRegex(@"^(Security )?Update for |\(KB\d+\)", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUpdateName();

    public static IReadOnlyList<InstalledApp> ReadAll()
    {
        var apps = new List<InstalledApp>();
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, AppScope.Machine, "HKLM64", apps);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, AppScope.Machine, "HKLM32", apps);
        Read(RegistryHive.CurrentUser, RegistryView.Registry64, AppScope.User, "HKCU", apps);

        return apps
            .GroupBy(a => (NameMatcher.Compact(a.DisplayName), a.Version ?? string.Empty))
            .Select(g => g.First())
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Read(RegistryHive hive, RegistryView view, AppScope scope, string source, List<InstalledApp> into)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallKey, writable: false);
            if (uninstall is null)
            {
                return;
            }

            foreach (var keyName in uninstall.GetSubKeyNames())
            {
                var app = ReadOne(uninstall, keyName, scope, source);
                if (app is not null)
                {
                    into.Add(app);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            // An unreadable view just contributes nothing.
        }
    }

    private static InstalledApp? ReadOne(RegistryKey uninstall, string keyName, AppScope scope, string source)
    {
        try
        {
            using var key = uninstall.OpenSubKey(keyName, writable: false);
            if (key is null)
            {
                return null;
            }

            var displayName = (key.GetValue("DisplayName") as string)?.Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                return null;
            }

            // Hidden entries, updates and patches are not apps a person would reset.
            if (key.GetValue("SystemComponent") is int systemComponent && systemComponent == 1)
            {
                return null;
            }

            if (key.GetValue("ParentKeyName") is string parent && parent.Length > 0)
            {
                return null;
            }

            if (key.GetValue("ReleaseType") is string releaseType
                && releaseType is "Update" or "Hotfix" or "Security Update" or "Service Pack")
            {
                return null;
            }

            if (WindowsUpdateName().IsMatch(displayName))
            {
                return null;
            }

            return new InstalledApp
            {
                Id = source + "\\" + keyName,
                DisplayName = displayName,
                Publisher = (key.GetValue("Publisher") as string)?.Trim(),
                Version = (key.GetValue("DisplayVersion") as string)?.Trim(),
                InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim(),
                DisplayIcon = (key.GetValue("DisplayIcon") as string)?.Trim(),
                UninstallString = (key.GetValue("UninstallString") as string)?.Trim(),
                Scope = scope,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
