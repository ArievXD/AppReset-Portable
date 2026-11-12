namespace AppReset.Core;

/// <summary>
/// Hard limits on what AppReset may ever delete. The scanner uses these to filter what it
/// suggests, and the reset and restore services check them again right before changing
/// anything, so a bug in the UI can never widen what gets removed.
/// </summary>
public static class SafetyRules
{
    /// <summary>
    /// Folder and key names shared by Windows or by many programs. They are never offered
    /// as an item on their own. Stored in compact form (see NameMatcher.Compact).
    /// </summary>
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.Ordinal)
    {
        "microsoft", "windows", "temp", "tmp", "cache", "caches", "logs", "log", "packages",
        "programs", "commonfiles", "applicationdata", "local", "roaming", "locallow", "appdata",
        "desktop", "documents", "downloads", "pictures", "music", "videos", "crashdumps",
        "d3dscache", "connecteddevicesplatform", "virtualstore", "packagecache", "installer",
        "history", "publishers", "fonts", "comms", "identities", "programdata", "users", "user",
        "system", "system32", "syswow64", "default", "public", "data", "config", "settings",
        "classes", "policies", "wow6432node", "clients", "registeredapplications", "odbc",
        "appdatalow", "uninstall", "sharedtools", "intel", "nvidia", "amd", "realtek",
        "apps", "app", "bin", "lib", "common", "shared", "tools", "updater", "update", "updates",
        "crashpad", "crashreports", "squirreltemp", "electron", "appreset",
    };

    /// <summary>
    /// Folders AppReset never looks inside, even when a child name matches the app.
    /// "Programs" in LocalAppData holds per-user installs, for example.
    /// </summary>
    private static readonly HashSet<string> NeverDescendFolders = new(StringComparer.Ordinal)
    {
        "temp", "tmp", "packages", "programs", "crashdumps", "d3dscache", "virtualstore",
        "packagecache", "installer", "connecteddevicesplatform", "comms", "history",
        "publishers", "fonts", "squirreltemp", "crashpad", "windows", "appreset",
    };

    /// <summary>Registry keys under Software that AppReset never looks inside.</summary>
    private static readonly HashSet<string> NeverDescendKeys = new(StringComparer.Ordinal)
    {
        "classes", "policies", "clients", "registeredapplications", "odbc", "wow6432node",
        "appdatalow", "windows",
    };

    public static bool IsProtectedName(string compactName) => ProtectedNames.Contains(compactName);

    public static bool IsNeverDescendFolder(string name) => NeverDescendFolders.Contains(NameMatcher.Compact(name));

    public static bool IsNeverDescendKey(string name) => NeverDescendKeys.Contains(NameMatcher.Compact(name));

    /// <summary>
    /// True when a folder may be offered for removal (pass a null app when restoring a backup):
    /// one or two levels inside a scan root, not a protected name, not inside a folder
    /// AppReset never enters, not AppReset's own folder, and not overlapping the app's
    /// program files (the app must stay installed).
    /// </summary>
    public static bool IsFolderAllowed(string path, ScanRoots roots, InstalledApp? app)
    {
        var p = PathText.Clean(path);
        if (p.Length == 0 || PathText.HasRelativeSegments(p))
        {
            return false;
        }

        var baseFolders = BaseFolders(roots);
        var baseFolder = baseFolders.FirstOrDefault(b => PathText.DepthBelow(p, b) is 1 or 2);
        if (baseFolder is null)
        {
            return false;
        }

        // Never a folder that contains another scan root.
        if (baseFolders.Any(b => PathText.AreSame(p, b) || PathText.IsUnder(b, p)))
        {
            return false;
        }

        if (IsProtectedName(NameMatcher.Compact(PathText.LastSegment(p))))
        {
            return false;
        }

        if (PathText.DepthBelow(p, baseFolder) == 2)
        {
            var parentName = PathText.LastSegment(PathText.Parent(p));
            if (IsNeverDescendFolder(parentName))
            {
                return false;
            }
        }

        if (PathText.Overlaps(p, roots.OwnDataFolder))
        {
            return false;
        }

        if (app is not null && ProgramPaths(app).Any(programPath => PathText.Overlaps(p, programPath)))
        {
            return false;
        }

        return true;
    }

    private static List<string> BaseFolders(ScanRoots roots) =>
        new[] { roots.RoamingAppData, roots.LocalAppData, roots.LocalLowAppData, roots.ProgramData }
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => PathText.Clean(b))
            .ToList();

    /// <summary>
    /// Folders that hold the installed program itself. Data inside or around them is never
    /// removed. Over-matching here only makes AppReset more careful, never less.
    /// </summary>
    public static IEnumerable<string> ProgramPaths(InstalledApp app)
    {
        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            yield return PathText.Clean(app.InstallLocation);
        }

        foreach (var exe in new[] { PathText.ExePath(app.DisplayIcon), PathText.ExePath(app.UninstallString) })
        {
            var dir = PathText.Parent(exe);
            if (dir is not null && dir.Length > 3)
            {
                yield return dir;
            }
        }
    }

    /// <summary>
    /// True when a registry key may be offered for removal: HKCU\Software\X(\Y),
    /// HKLM\SOFTWARE\X(\Y) or HKLM\SOFTWARE\WOW6432Node\X(\Y). X must not be a key AppReset
    /// never enters, and the last part must not be a protected name.
    /// </summary>
    public static bool IsRegistryKeyAllowed(string fullKeyPath)
    {
        if (!RegistryPath.TryParse(fullKeyPath, out var root, out var subKey))
        {
            return false;
        }

        var parts = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = parts.Skip(1).ToList();
        if (root == RegistryRoot.LocalMachine && rest[0].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest.Skip(1).ToList();
        }

        if (rest.Count is < 1 or > 2 || rest.Any(r => r is "." or ".."))
        {
            return false;
        }

        if (IsNeverDescendKey(rest[0]))
        {
            return false;
        }

        return !IsProtectedName(NameMatcher.Compact(rest[^1]));
    }

    public static bool IsAllowed(DataItem item, ScanRoots roots, InstalledApp? app) => item.Kind switch
    {
        ItemKind.Folder => IsFolderAllowed(item.Path, roots, app),
        ItemKind.RegistryKey => IsRegistryKeyAllowed(item.Path),
        _ => false,
    };
}
