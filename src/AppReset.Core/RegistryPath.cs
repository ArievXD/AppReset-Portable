namespace AppReset.Core;

/// <summary>Full registry paths such as HKEY_CURRENT_USER\Software\7-Zip.</summary>
public static class RegistryPath
{
    public const string CurrentUserPrefix = "HKEY_CURRENT_USER";
    public const string LocalMachinePrefix = "HKEY_LOCAL_MACHINE";

    public static string Build(RegistryRoot root, string subKey) =>
        (root == RegistryRoot.CurrentUser ? CurrentUserPrefix : LocalMachinePrefix) + "\\" + subKey.Trim('\\');

    public static bool TryParse(string fullPath, out RegistryRoot root, out string subKey)
    {
        root = RegistryRoot.CurrentUser;
        subKey = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var p = fullPath.Trim().Replace('/', '\\');
        var slash = p.IndexOf('\\');
        if (slash <= 0 || slash == p.Length - 1)
        {
            return false;
        }

        var hive = p[..slash].ToUpperInvariant();
        switch (hive)
        {
            case CurrentUserPrefix:
            case "HKCU":
                root = RegistryRoot.CurrentUser;
                break;
            case LocalMachinePrefix:
            case "HKLM":
                root = RegistryRoot.LocalMachine;
                break;
            default:
                return false;
        }

        subKey = p[(slash + 1)..].Trim('\\');
        return subKey.Length > 0;
    }

    /// <summary>Short form for reg.exe, for example HKCU\Software\7-Zip.</summary>
    public static string ToRegExeForm(string fullPath)
    {
        if (!TryParse(fullPath, out var root, out var subKey))
        {
            throw new ArgumentException("Not a registry path: " + fullPath, nameof(fullPath));
        }

        return (root == RegistryRoot.CurrentUser ? "HKCU" : "HKLM") + "\\" + subKey;
    }
}
