using AppReset.Core;
using static AppReset.Tests.TestRoots;

namespace AppReset.Tests;

public class SafetyRulesTests
{
    private static readonly InstalledApp Discord = Apps.Make(
        "Discord",
        "Discord Inc.",
        installLocation: Local + @"\Discord",
        displayIcon: Local + @"\Discord\app.ico");

    [Theory]
    [InlineData(Roaming + @"\7-Zip")]
    [InlineData(Roaming + @"\Mozilla\Firefox")]
    [InlineData(Local + @"\Microsoft\Teams")]
    [InlineData(LocalLow + @"\Unity")]
    [InlineData(ProgramData + @"\SomeApp")]
    [InlineData(Roaming + @"\discord")]
    public void Allows_app_folders_one_or_two_levels_deep(string path)
    {
        Assert.True(SafetyRules.IsFolderAllowed(path, Roots, Discord));
    }

    [Theory]
    [InlineData(Roaming)]
    [InlineData(Local)]
    [InlineData(@"C:\Users\Ana\AppData")]
    [InlineData(@"C:\Users\Ana")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files\Mozilla Firefox")]
    [InlineData(Roaming + @"\A\B\C")]
    [InlineData(Roaming + @"\Microsoft")]
    [InlineData(Local + @"\Temp\SomeApp")]
    [InlineData(Local + @"\Programs\SomeApp")]
    [InlineData(Local + @"\Packages\SomeApp")]
    [InlineData(Local + @"\AppReset")]
    [InlineData(Local + @"\AppReset\Backups")]
    [InlineData(Roaming + @"\..\Local\SomeApp")]
    [InlineData(Local + @"\Discord")]
    [InlineData("")]
    public void Refuses_everything_else(string path)
    {
        Assert.False(SafetyRules.IsFolderAllowed(path, Roots, Discord));
    }

    [Fact]
    public void Install_folder_inside_app_data_is_protected_but_settings_next_to_it_are_not()
    {
        Assert.False(SafetyRules.IsFolderAllowed(Local + @"\Discord", Roots, Discord));
        Assert.True(SafetyRules.IsFolderAllowed(Roaming + @"\discord", Roots, Discord));
    }

    [Fact]
    public void Restore_without_an_app_still_applies_the_location_rules()
    {
        Assert.True(SafetyRules.IsFolderAllowed(Roaming + @"\7-Zip", Roots, app: null));
        Assert.False(SafetyRules.IsFolderAllowed(@"C:\Windows\Temp", Roots, app: null));
    }

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software\7-Zip")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Mozilla\Firefox")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Office")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\VideoLAN\VLC")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\SomeApp")]
    [InlineData(@"HKCU\Software\SomeApp")]
    public void Allows_app_registry_keys(string path)
    {
        Assert.True(SafetyRules.IsRegistryKeyAllowed(path));
    }

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Classes\SomeApp")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Policies\SomeApp")]
    [InlineData(@"HKEY_CURRENT_USER\Software\A\B\C")]
    [InlineData(@"HKEY_CURRENT_USER\System\CurrentControlSet")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\SomeApp")]
    [InlineData(@"HKEY_USERS\S-1-5-18\Software\SomeApp")]
    [InlineData(@"HKEY_CURRENT_USER\Software\..\SomeApp")]
    [InlineData("not a key")]
    public void Refuses_system_registry_keys(string path)
    {
        Assert.False(SafetyRules.IsRegistryKeyAllowed(path));
    }
}
