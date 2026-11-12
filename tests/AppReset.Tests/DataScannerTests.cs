using AppReset.Core;
using static AppReset.Tests.TestRoots;

namespace AppReset.Tests;

public class DataScannerTests
{
    private static IReadOnlyList<DataItem> Scan(
        InstalledApp app,
        FakeFileSystem fs,
        FakeRegistry? reg = null,
        params InstalledApp[] others)
    {
        var scanner = new DataScanner(Roots, fs, reg ?? new FakeRegistry());
        return scanner.Scan(app, [app, .. others]);
    }

    [Fact]
    public void Firefox_with_another_Mozilla_app_only_gets_its_own_folders()
    {
        var firefox = Apps.Make(
            "Mozilla Firefox (x64 en-US)",
            "Mozilla",
            installLocation: @"C:\Program Files\Mozilla Firefox",
            displayIcon: @"C:\Program Files\Mozilla Firefox\firefox.exe,0");
        var thunderbird = Apps.Make("Mozilla Thunderbird", "Mozilla");

        var fs = new FakeFileSystem()
            .Folder(Roaming + @"\Mozilla\Firefox")
            .Folder(Roaming + @"\Mozilla\Extensions")
            .Folder(Roaming + @"\Thunderbird")
            .Folder(Local + @"\Mozilla\Firefox")
            .Folder(Local + @"\Temp\firefox")
            .Folder(Roaming + @"\Microsoft\Windows");
        var reg = new FakeRegistry()
            .Key(RegistryRoot.CurrentUser, @"Software\Mozilla\Firefox")
            .Key(RegistryRoot.CurrentUser, @"Software\Mozilla\Thunderbird")
            .Key(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows");

        var items = Scan(firefox, fs, reg, thunderbird);
        var paths = items.Select(i => i.Path).ToList();

        Assert.Equal(
            new[]
            {
                Roaming + @"\Mozilla\Firefox",
                Local + @"\Mozilla\Firefox",
                @"HKEY_CURRENT_USER\Software\Mozilla\Firefox",
            },
            paths);
        Assert.All(items, i => Assert.Equal(MatchReason.InsidePublisherFolder, i.Reason));
        Assert.All(items, i => Assert.True(i.SelectedByDefault));
    }

    [Fact]
    public void Shared_publisher_folder_is_never_offered_whole()
    {
        var firefox = Apps.Make("Mozilla Firefox", "Mozilla");
        var thunderbird = Apps.Make("Mozilla Thunderbird", "Mozilla");
        var fs = new FakeFileSystem().Folder(Roaming + @"\Mozilla\Profiles");

        Assert.Empty(Scan(firefox, fs, null, thunderbird));
    }

    [Fact]
    public void Single_app_publisher_folder_is_offered_but_not_ticked()
    {
        var app = Apps.Make("Krita (x64)", "Krita Foundation");
        var fs = new FakeFileSystem().Folder(Roaming + @"\Krita Foundation\stuff");

        var item = Assert.Single(Scan(app, fs));
        Assert.Equal(Roaming + @"\Krita Foundation", item.Path);
        Assert.Equal(MatchReason.PublisherFolder, item.Reason);
        Assert.False(item.SelectedByDefault);
    }

    [Fact]
    public void App_named_like_its_publisher_is_an_exact_match()
    {
        var spotify = Apps.Make("Spotify", "Spotify AB");
        var fs = new FakeFileSystem().Folder(Roaming + @"\Spotify");

        var item = Assert.Single(Scan(spotify, fs));
        Assert.Equal(MatchStrength.Exact, item.Strength);
        Assert.True(item.SelectedByDefault);
    }

    [Fact]
    public void Discord_settings_are_found_but_its_install_folder_is_left_alone()
    {
        var discord = Apps.Make(
            "Discord",
            "Discord Inc.",
            installLocation: Local + @"\Discord",
            displayIcon: Local + @"\Discord\app.ico");
        var fs = new FakeFileSystem()
            .Folder(Roaming + @"\discord")
            .Folder(Local + @"\Discord\app-1.0.9");

        var item = Assert.Single(Scan(discord, fs));
        Assert.Equal(Roaming + @"\discord", item.Path);
    }

    [Fact]
    public void Machine_wide_data_is_found_but_never_ticked()
    {
        var app = Apps.Make("VideoLAN VLC", "VideoLAN");
        var fs = new FakeFileSystem().Folder(ProgramData + @"\VLC");
        var reg = new FakeRegistry().Key(RegistryRoot.LocalMachine, @"SOFTWARE\VideoLAN\VLC");

        var items = Scan(app, fs, reg);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.RequiresAdmin));
        Assert.All(items, i => Assert.False(i.SelectedByDefault));
    }

    [Fact]
    public void Folder_links_are_skipped()
    {
        var app = Apps.Make("7-Zip", "Igor Pavlov");
        var fs = new FakeFileSystem().Folder(Roaming + @"\7-Zip", isLink: true);

        Assert.Empty(Scan(app, fs));
    }

    [Fact]
    public void Microsoft_app_data_is_found_inside_the_Microsoft_folder_only()
    {
        var teams = Apps.Make("Microsoft Teams", "Microsoft Corporation");
        var office = Apps.Make("Microsoft Office", "Microsoft Corporation");
        var fs = new FakeFileSystem()
            .Folder(Roaming + @"\Microsoft\Teams")
            .Folder(Roaming + @"\Microsoft\Windows")
            .Folder(Roaming + @"\Microsoft\Office");

        var item = Assert.Single(Scan(teams, fs, null, office));
        Assert.Equal(Roaming + @"\Microsoft\Teams", item.Path);
    }

    [Fact]
    public void Weak_matches_are_listed_unticked()
    {
        var app = Apps.Make("Discord");
        var fs = new FakeFileSystem().Folder(Roaming + @"\BetterDiscordData");

        var item = Assert.Single(Scan(app, fs));
        Assert.Equal(MatchStrength.Possible, item.Strength);
        Assert.False(item.SelectedByDefault);
    }
}
