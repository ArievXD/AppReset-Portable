namespace AppReset.Core;

/// <summary>
/// Finds the folders and registry keys an app keeps its data in.
/// It only reads. Nothing is changed until ResetService runs.
/// </summary>
public sealed class DataScanner
{
    private readonly ScanRoots _roots;
    private readonly IFileSystemView _fileSystem;
    private readonly IRegistryView _registry;

    public DataScanner(ScanRoots roots, IFileSystemView fileSystem, IRegistryView registry)
    {
        _roots = roots;
        _fileSystem = fileSystem;
        _registry = registry;
    }

    public IReadOnlyList<DataItem> Scan(
        InstalledApp app,
        IReadOnlyCollection<InstalledApp> allApps,
        CancellationToken cancellationToken = default)
    {
        var keys = NameMatcher.NameKeys(app);
        if (keys.Count == 0)
        {
            return [];
        }

        var publisherShared = IsPublisherShared(app, allApps);
        var found = new List<DataItem>();

        foreach (var (location, basePath) in FolderBases())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = FindCandidates(
                basePath,
                _fileSystem.GetDirectoryNames,
                PathText.Join,
                SafetyRules.IsNeverDescendFolder,
                app,
                keys,
                publisherShared);

            foreach (var (path, match) in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SafetyRules.IsFolderAllowed(path, _roots, app) || _fileSystem.IsReparsePoint(path))
                {
                    continue;
                }

                found.Add(new DataItem
                {
                    Kind = ItemKind.Folder,
                    Location = location,
                    Path = path,
                    Strength = match.Strength,
                    Reason = match.Reason,
                    SizeBytes = _fileSystem.GetDirectorySize(path, cancellationToken),
                });
            }
        }

        foreach (var (location, root, basePath) in RegistryBases())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = FindCandidates(
                basePath,
                path => _registry.GetSubKeyNames(root, path),
                (parent, child) => parent + "\\" + child,
                SafetyRules.IsNeverDescendKey,
                app,
                keys,
                publisherShared);

            foreach (var (subKey, match) in candidates)
            {
                var fullPath = RegistryPath.Build(root, subKey);
                if (!SafetyRules.IsRegistryKeyAllowed(fullPath))
                {
                    continue;
                }

                found.Add(new DataItem
                {
                    Kind = ItemKind.RegistryKey,
                    Location = location,
                    Path = fullPath,
                    Strength = match.Strength,
                    Reason = match.Reason,
                });
            }
        }

        return found
            .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(i => i.Strength).First())
            .OrderBy(i => i.Location)
            .ThenByDescending(i => i.Strength)
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True when another installed app has the same publisher. Then the publisher folder
    /// belongs to all of them and is never offered as a whole.
    /// </summary>
    public static bool IsPublisherShared(InstalledApp app, IEnumerable<InstalledApp> allApps)
    {
        var key = NameMatcher.PublisherKey(app.Publisher);
        if (key is null)
        {
            return true;
        }

        var ownName = NameMatcher.Compact(app.DisplayName);
        return allApps.Any(other =>
            other.Id != app.Id
            && NameMatcher.PublisherKey(other.Publisher) == key
            && NameMatcher.Compact(other.DisplayName) != ownName);
    }

    private IEnumerable<(DataLocation Location, string Path)> FolderBases()
    {
        yield return (DataLocation.RoamingAppData, _roots.RoamingAppData);
        yield return (DataLocation.LocalAppData, _roots.LocalAppData);
        if (!string.IsNullOrWhiteSpace(_roots.LocalLowAppData))
        {
            yield return (DataLocation.LocalLowAppData, _roots.LocalLowAppData);
        }

        yield return (DataLocation.ProgramData, _roots.ProgramData);
    }

    private static IEnumerable<(DataLocation Location, RegistryRoot Root, string Path)> RegistryBases()
    {
        yield return (DataLocation.UserRegistry, RegistryRoot.CurrentUser, "Software");
        yield return (DataLocation.MachineRegistry, RegistryRoot.LocalMachine, "SOFTWARE");
        yield return (DataLocation.MachineRegistry, RegistryRoot.LocalMachine, @"SOFTWARE\WOW6432Node");
    }

    /// <summary>
    /// Shared by folders and registry keys. Looks one level down, and one more level inside
    /// the publisher's own folder (AppData\Mozilla\Firefox, Software\Mozilla\Firefox).
    /// </summary>
    internal static IEnumerable<(string Path, NameMatch Match)> FindCandidates(
        string basePath,
        Func<string, IReadOnlyList<string>> children,
        Func<string, string, string> join,
        Func<string, bool> neverDescend,
        InstalledApp app,
        IReadOnlyList<NameKey> keys,
        bool publisherShared)
    {
        foreach (var name in children(basePath))
        {
            if (neverDescend(name))
            {
                continue;
            }

            var path = join(basePath, name);
            var isPublisherFolder = NameMatcher.IsPublisherFolder(name, app.Publisher);
            var direct = NameMatcher.Compare(name, keys);

            // "Spotify" by "Spotify AB": the folder is both the app and the publisher.
            if (direct is { Strength: MatchStrength.Exact } && !(isPublisherFolder && publisherShared))
            {
                yield return (path, direct);
                continue;
            }

            if (isPublisherFolder)
            {
                var foundInside = false;
                foreach (var child in children(path))
                {
                    var inner = NameMatcher.Compare(child, keys);
                    if (inner is null)
                    {
                        continue;
                    }

                    foundInside = true;
                    var reason = inner.Strength == MatchStrength.Exact ? MatchReason.InsidePublisherFolder : inner.Reason;
                    yield return (join(path, child), inner with { Reason = reason });
                }

                var compact = NameMatcher.Compact(name);
                var publisherKey = NameMatcher.PublisherKey(name) ?? compact;
                if (!publisherShared && !foundInside
                    && !SafetyRules.IsProtectedName(compact)
                    && !SafetyRules.IsProtectedName(publisherKey))
                {
                    yield return (path, new NameMatch(MatchStrength.Likely, MatchReason.PublisherFolder));
                }

                continue;
            }

            if (direct is not null)
            {
                yield return (path, direct);
            }
        }
    }
}
