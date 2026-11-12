namespace AppReset.Core;

/// <summary>A program listed under Windows "Installed apps" (the Uninstall registry keys).</summary>
public sealed record InstalledApp
{
    /// <summary>Stable id: scope, registry view and uninstall key name.</summary>
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? InstallLocation { get; init; }
    public string? DisplayIcon { get; init; }
    public string? UninstallString { get; init; }
    public AppScope Scope { get; init; }
}

public enum AppScope
{
    /// <summary>Installed for every user (HKLM).</summary>
    Machine,
    /// <summary>Installed for the current user only (HKCU).</summary>
    User,
}

/// <summary>Where a piece of app data lives.</summary>
public enum DataLocation
{
    RoamingAppData,
    LocalAppData,
    LocalLowAppData,
    ProgramData,
    UserRegistry,
    MachineRegistry,
}

public enum ItemKind
{
    Folder,
    RegistryKey,
}

/// <summary>How sure the scanner is that an item belongs to the app.</summary>
public enum MatchStrength
{
    Possible = 1,
    Likely = 2,
    Exact = 3,
}

/// <summary>Why an item was matched. The UI turns this into a sentence.</summary>
public enum MatchReason
{
    /// <summary>Name equals the app name or its install folder name.</summary>
    NameExact,
    /// <summary>Name equals the program file name (for example Code.exe).</summary>
    ExeName,
    /// <summary>Name starts with the app name, or the other way round.</summary>
    NameSimilar,
    /// <summary>Name contains the app name.</summary>
    NameContains,
    /// <summary>Inside the publisher folder, and the name matches the app.</summary>
    InsidePublisherFolder,
    /// <summary>The whole publisher folder. No other app from this publisher is installed.</summary>
    PublisherFolder,
}

/// <summary>A folder or registry key the scanner thinks belongs to the app.</summary>
public sealed record DataItem
{
    public required ItemKind Kind { get; init; }
    public required DataLocation Location { get; init; }

    /// <summary>
    /// Folder: full path. Registry: full key path starting with
    /// HKEY_CURRENT_USER\ or HKEY_LOCAL_MACHINE\.
    /// </summary>
    public required string Path { get; init; }

    public required MatchStrength Strength { get; init; }
    public required MatchReason Reason { get; init; }

    /// <summary>Folder size in bytes. Null for registry keys or when it could not be measured.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>ProgramData and HKLM usually need administrator rights to change.</summary>
    public bool RequiresAdmin => Location is DataLocation.ProgramData or DataLocation.MachineRegistry;

    /// <summary>
    /// Shared by every user on the PC. HKLM keys are often written by the installer,
    /// so removing them can break the program.
    /// </summary>
    public bool IsMachineWide => RequiresAdmin;

    /// <summary>
    /// Only exact matches in the current user's own data are ticked by default.
    /// Everything else waits for the user to decide.
    /// </summary>
    public bool SelectedByDefault =>
        Strength == MatchStrength.Exact && !IsMachineWide;
}

/// <summary>Base folders the scanner looks in.</summary>
public sealed record ScanRoots(
    string RoamingAppData,
    string LocalAppData,
    string? LocalLowAppData,
    string ProgramData,
    string OwnDataFolder);

public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
}

/// <summary>Read-only view of folders, so the scanner can be tested without a real disk.</summary>
public interface IFileSystemView
{
    /// <summary>Names (not paths) of the sub folders. Empty when the folder is missing or unreadable.</summary>
    IReadOnlyList<string> GetDirectoryNames(string path);

    /// <summary>True when the folder is a junction or symbolic link.</summary>
    bool IsReparsePoint(string path);

    /// <summary>Total size of the files inside, or null when it could not be measured.</summary>
    long? GetDirectorySize(string path, CancellationToken cancellationToken);
}

/// <summary>Read-only view of the registry, so the scanner can be tested without Windows.</summary>
public interface IRegistryView
{
    /// <summary>Sub key names. Empty when the key is missing or unreadable.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path);
}
