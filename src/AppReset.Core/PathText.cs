namespace AppReset.Core;

/// <summary>
/// Windows path handling done on plain strings, so the same rules behave identically
/// on Windows and in tests on other systems.
/// </summary>
public static class PathText
{
    /// <summary>Backslashes only, no trailing separator, no surrounding quotes or spaces.</summary>
    public static string Clean(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var p = path.Trim().Trim('"').Trim().Replace('/', '\\');
        while (p.Length > 3 && p.EndsWith('\\'))
        {
            p = p[..^1];
        }

        return p;
    }

    public static string Join(string parent, string child) => Clean(parent) + "\\" + child.Trim('\\', '/');

    public static bool AreSame(string a, string b) =>
        string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="parent"/>.</summary>
    public static bool IsUnder(string path, string parent)
    {
        var p = Clean(path);
        var root = Clean(parent);
        if (p.Length == 0 || root.Length == 0)
        {
            return false;
        }

        return p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the two paths are the same or one contains the other.</summary>
    public static bool Overlaps(string a, string b) => AreSame(a, b) || IsUnder(a, b) || IsUnder(b, a);

    /// <summary>Number of folder levels between <paramref name="parent"/> and <paramref name="path"/>.</summary>
    public static int DepthBelow(string path, string parent)
    {
        if (!IsUnder(path, parent))
        {
            return 0;
        }

        var rest = Clean(path)[(Clean(parent).Length + 1)..];
        return rest.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Rejects "." and ".." segments so a path can never climb out of its base folder.</summary>
    public static bool HasRelativeSegments(string path) =>
        Clean(path).Split('\\').Any(s => s is "." or "..");

    public static string LastSegment(string? path)
    {
        var p = Clean(path);
        var i = p.LastIndexOf('\\');
        return i >= 0 ? p[(i + 1)..] : p;
    }

    public static string? Parent(string? path)
    {
        var p = Clean(path);
        var i = p.LastIndexOf('\\');
        if (i <= 0)
        {
            return null;
        }

        // Keep "C:\" as a root rather than "C:".
        return i == 2 && p[1] == ':' ? p[..3] : p[..i];
    }

    /// <summary>
    /// The executable path inside a DisplayIcon or UninstallString value, without
    /// quotes, icon index (",0") or arguments.
    /// </summary>
    public static string? ExePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim();
        string path;
        if (v.StartsWith('"'))
        {
            var end = v.IndexOf('"', 1);
            path = end > 1 ? v[1..end] : v.Trim('"');
        }
        else
        {
            var exeEnd = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exeEnd > 0 ? v[..(exeEnd + 4)] : v.Split(',')[0];
        }

        path = Clean(path);
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    public static string? ExeNameWithoutExtension(string? value)
    {
        var exe = ExePath(value);
        if (exe is null)
        {
            return null;
        }

        var name = LastSegment(exe);
        return name[..^4];
    }
}
