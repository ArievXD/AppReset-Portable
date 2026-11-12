using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AppReset.Core.Windows;

/// <summary>The few low-level operations that change the disk or the registry.</summary>
[SupportedOSPlatform("windows")]
internal static class FileAndRegistryOps
{
    /// <summary>
    /// Copies a folder. Throws on the first file that cannot be read (for example because the
    /// app still has it open), so a backup is either complete or reported as failed.
    /// </summary>
    public static void CopyDirectory(string source, string target, CancellationToken cancellationToken)
    {
        var sourceInfo = new DirectoryInfo(source);
        Directory.CreateDirectory(target);
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

        foreach (var file in sourceInfo.EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.CopyTo(Path.Combine(target, file.Name), overwrite: true);
        }

        foreach (var dir in sourceInfo.EnumerateDirectories("*", options))
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ItemException(FailureKind.ContainsLink);
            }

            CopyDirectory(dir.FullName, Path.Combine(target, dir.Name), cancellationToken);
        }
    }

    /// <summary>Deletes a folder, clearing read-only flags first. Never follows links.</summary>
    public static void DeleteDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            return;
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ItemException(FailureKind.ContainsLink);
        }

        ClearReadOnly(info);
        info.Delete(recursive: true);
    }

    private static void ClearReadOnly(DirectoryInfo dir)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        foreach (var file in dir.EnumerateFiles("*", options))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        foreach (var sub in dir.EnumerateDirectories("*", options))
        {
            if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                ClearReadOnly(sub);
            }
        }

        if (dir.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            dir.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

    public static bool RegistryKeyExists(string fullPath)
    {
        if (!RegistryPath.TryParse(fullPath, out var root, out var subKey))
        {
            return false;
        }

        using var baseKey = RegistryKey.OpenBaseKey(WindowsRegistry.Hive(root), RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        return key is not null;
    }

    public static void DeleteRegistryKey(string fullPath)
    {
        if (!RegistryPath.TryParse(fullPath, out var root, out var subKey))
        {
            throw new ArgumentException("Not a registry path: " + fullPath, nameof(fullPath));
        }

        using var baseKey = RegistryKey.OpenBaseKey(WindowsRegistry.Hive(root), RegistryView.Registry64);
        baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    /// <summary>Saves a key and everything under it to a .reg file with Windows' own reg.exe.</summary>
    public static void ExportRegistryKey(string fullPath, string regFile) =>
        RunReg("export", RegistryPath.ToRegExeForm(fullPath), regFile, "/y", "/reg:64");

    /// <summary>Loads a .reg file made by ExportRegistryKey back into the registry.</summary>
    public static void ImportRegistryFile(string regFile) =>
        RunReg("import", regFile, "/reg:64");

    private static void RunReg(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new IOException("reg.exe could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        output.Wait();

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? output.Result : error;
            throw new IOException(message.Trim());
        }
    }
}
