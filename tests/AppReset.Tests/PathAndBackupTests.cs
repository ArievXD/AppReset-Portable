using AppReset.Core;

namespace AppReset.Tests;

public class PathAndBackupTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\",0", @"C:\Program Files\Mozilla Firefox\firefox.exe")]
    [InlineData(@"C:\Program Files\7-Zip\7zFM.exe", @"C:\Program Files\7-Zip\7zFM.exe")]
    [InlineData(@"C:\Program Files\VLC\vlc.exe,0", @"C:\Program Files\VLC\vlc.exe")]
    [InlineData("\"C:\\Program Files\\Tool\\unins000.exe\" /SILENT", @"C:\Program Files\Tool\unins000.exe")]
    [InlineData("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", "MsiExec.exe")]
    [InlineData(@"C:\Program Files\Tool\icon.ico", null)]
    [InlineData(null, null)]
    public void ExePath_extracts_the_program_file(string? value, string? expected)
    {
        Assert.Equal(expected, PathText.ExePath(value));
    }

    [Fact]
    public void Path_helpers_ignore_case_and_trailing_slashes()
    {
        Assert.True(PathText.AreSame(@"C:\Users\Ana\AppData\", "c:/users/ana/appdata"));
        Assert.True(PathText.IsUnder(@"C:\A\B", @"C:\a"));
        Assert.False(PathText.IsUnder(@"C:\AB", @"C:\A"));
        Assert.Equal(2, PathText.DepthBelow(@"C:\A\B\C", @"C:\A"));
        Assert.Equal(@"C:\", PathText.Parent(@"C:\A"));
    }

    [Fact]
    public void Backup_manifest_round_trips_through_json()
    {
        var manifest = new BackupManifest
        {
            AppName = "7-Zip",
            AppVersion = "23.01",
            CreatedAt = new DateTimeOffset(2026, 11, 12, 20, 30, 0, TimeSpan.FromHours(7)),
            Entries =
            [
                new BackupEntry
                {
                    Kind = ItemKind.RegistryKey,
                    Location = DataLocation.UserRegistry,
                    OriginalPath = @"HKEY_CURRENT_USER\Software\7-Zip",
                    StoredAs = "item-01.reg",
                },
            ],
        };

        var json = manifest.ToJson();
        var back = BackupManifest.FromJson(json);

        Assert.NotNull(back);
        Assert.Contains("\"RegistryKey\"", json);
        Assert.Equal(manifest.AppName, back.AppName);
        Assert.Equal(manifest.CreatedAt, back.CreatedAt);
        Assert.Equal(manifest.Entries[0], back.Entries[0]);
    }

    [Fact]
    public void Broken_manifest_is_ignored_not_thrown()
    {
        Assert.Null(BackupManifest.FromJson("{ not json"));
    }

    [Fact]
    public void Sharing_violation_is_reported_as_in_use()
    {
        var e = new IOException("busy", unchecked((int)0x80070020));
        var failure = ItemException.ToFailure("x", FailureStage.Backup, e);

        Assert.Equal(FailureKind.InUse, failure.Kind);
        Assert.Equal(FailureStage.Backup, failure.Stage);
    }

    [Fact]
    public void Registry_paths_convert_for_reg_exe()
    {
        Assert.Equal(@"HKCU\Software\7-Zip", RegistryPath.ToRegExeForm(@"HKEY_CURRENT_USER\Software\7-Zip"));
        Assert.Equal(@"HKLM\SOFTWARE\VLC", RegistryPath.ToRegExeForm(@"HKEY_LOCAL_MACHINE\SOFTWARE\VLC"));
    }
}
