using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace AppReset.Core.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemView : IFileSystemView
{
    public IReadOnlyList<string> GetDirectoryNames(string path)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
            };
            return new DirectoryInfo(path).EnumerateDirectories("*", options).Select(d => d.Name).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }

    public bool IsReparsePoint(string path)
    {
        try
        {
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Unreadable: treat it as unsafe so it is left alone.
            return true;
        }
    }

    public long? GetDirectorySize(string path, CancellationToken cancellationToken)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            long total = 0;
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += file.Length;
            }

            return total;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryView : IRegistryView
{
    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string path)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(WindowsRegistry.Hive(root), RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: false);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsRegistry
{
    public static RegistryHive Hive(RegistryRoot root) =>
        root == RegistryRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
}
