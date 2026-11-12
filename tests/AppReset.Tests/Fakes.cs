using AppReset.Core;

namespace AppReset.Tests;

/// <summary>A pretend Windows user folder layout. Paths use backslashes like the real thing.</summary>
internal static class TestRoots
{
    public const string Roaming = @"C:\Users\Ana\AppData\Roaming";
    public const string Local = @"C:\Users\Ana\AppData\Local";
    public const string LocalLow = @"C:\Users\Ana\AppData\LocalLow";
    public const string ProgramData = @"C:\ProgramData";
    public const string Own = @"C:\Users\Ana\AppData\Local\AppReset";

    public static ScanRoots Roots { get; } = new(Roaming, Local, LocalLow, ProgramData, Own);
}

internal sealed class FakeFileSystem : IFileSystemView
{
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _links = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a folder and every parent folder on the way.</summary>
    public FakeFileSystem Folder(string path, bool isLink = false)
    {
        var current = PathText.Clean(path);
        if (isLink)
        {
            _links.Add(current);
        }

        while (PathText.Parent(current) is { } parent && parent.Length > 3)
        {
            var name = PathText.LastSegment(current);
            if (!_children.TryGetValue(parent, out var list))
            {
                list = [];
                _children[parent] = list;
            }

            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(name);
            }

            current = parent;
        }

        return this;
    }

    public IReadOnlyList<string> GetDirectoryNames(string path) =>
        _children.TryGetValue(PathText.Clean(path), out var list) ? list : [];

    public bool IsReparsePoint(string path) => _links.Contains(PathText.Clean(path));

    public long? GetDirectorySize(string path, CancellationToken cancellationToken) => 1024;
}

internal sealed class FakeRegistry : IRegistryView
{
    private readonly Dictionary<(RegistryRoot, string), List<string>> _children = [];

    public FakeRegistry Key(RegistryRoot root, string subKey)
    {
        var parts = subKey.Split('\\');
        for (var i = 1; i < parts.Length; i++)
        {
            var parent = string.Join('\\', parts.Take(i)).ToUpperInvariant();
            if (!_children.TryGetValue((root, parent), out var list))
            {
                list = [];
                _children[(root, parent)] = list;
            }

            if (!list.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
            {
                list.Add(parts[i]);
            }
        }

        return this;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path) =>
        _children.TryGetValue((root, path.ToUpperInvariant()), out var list) ? list : [];
}

internal static class Apps
{
    public static InstalledApp Make(
        string name,
        string? publisher = null,
        string? installLocation = null,
        string? displayIcon = null,
        string? id = null) => new()
        {
            Id = id ?? "HKLM64\\" + name,
            DisplayName = name,
            Publisher = publisher,
            InstallLocation = installLocation,
            DisplayIcon = displayIcon,
        };
}
