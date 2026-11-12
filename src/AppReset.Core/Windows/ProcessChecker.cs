using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AppReset.Core.Windows;

public sealed record RunningProcess(string Name, int Id);

/// <summary>Finds running processes started from the app's program folder.</summary>
[SupportedOSPlatform("windows")]
public static class ProcessChecker
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static IReadOnlyList<RunningProcess> FindRunning(InstalledApp app)
    {
        var folders = ProgramFolders(app).ToList();
        if (folders.Count == 0)
        {
            return [];
        }

        var running = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var path = TryGetImagePath(process.Id);
                if (path is not null && folders.Any(f => PathText.IsUnder(path, f)))
                {
                    running.Add(new RunningProcess(process.ProcessName, process.Id));
                }
            }
        }

        return running
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// The app's own program folders, leaving out anything so broad that unrelated programs
    /// would match (Windows, Program Files itself, a drive root, AppData itself).
    /// </summary>
    private static IEnumerable<string> ProgramFolders(InstalledApp app)
    {
        var tooBroad = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var candidates = new[] { app.InstallLocation, PathText.Parent(PathText.ExePath(app.DisplayIcon)) };
        foreach (var candidate in candidates)
        {
            var folder = PathText.Clean(candidate);
            if (folder.Length <= 3)
            {
                continue;
            }

            if (PathText.AreSame(folder, windows) || PathText.IsUnder(folder, windows))
            {
                continue;
            }

            if (tooBroad.Any(b => PathText.AreSame(folder, b)))
            {
                continue;
            }

            yield return folder;
        }
    }

    private static string? TryGetImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
