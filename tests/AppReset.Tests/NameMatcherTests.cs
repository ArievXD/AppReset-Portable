using AppReset.Core;

namespace AppReset.Tests;

public class NameMatcherTests
{
    [Theory]
    [InlineData("7-Zip 23.01 (x64)", "7 zip")]
    [InlineData("Mozilla Firefox (x64 en-US)", "mozilla firefox")]
    [InlineData("Notepad++ (64-bit x64)", "notepad")]
    [InlineData("VLC media player 3.0.21", "vlc media player")]
    [InlineData("Audacity® 3.6", "audacity")]
    [InlineData("Visual Studio Code v1.95.2", "visual studio code")]
    [InlineData("  ", "")]
    public void Normalize_drops_versions_architecture_and_symbols(string input, string expected)
    {
        Assert.Equal(expected, NameMatcher.Normalize(input));
    }

    [Theory]
    [InlineData("Mozilla Corporation", "mozilla")]
    [InlineData("JetBrains s.r.o.", "jetbrains")]
    [InlineData("NVIDIA Corporation", "nvidia")]
    [InlineData("Spotify AB", "spotify")]
    [InlineData("Igor Pavlov", "igorpavlov")]
    public void PublisherKey_drops_company_suffixes(string publisher, string expected)
    {
        Assert.Equal(expected, NameMatcher.PublisherKey(publisher));
    }

    [Fact]
    public void Publisher_folder_is_recognised_with_or_without_suffix()
    {
        Assert.True(NameMatcher.IsPublisherFolder("Mozilla", "Mozilla Corporation"));
        Assert.True(NameMatcher.IsPublisherFolder("NVIDIA Corporation", "NVIDIA Corporation"));
        Assert.False(NameMatcher.IsPublisherFolder("Firefox", "Mozilla Corporation"));
        Assert.False(NameMatcher.IsPublisherFolder("Anything", null));
    }

    [Fact]
    public void Name_keys_include_name_without_publisher_and_install_folder()
    {
        var app = Apps.Make(
            "Mozilla Firefox (x64 en-US)",
            "Mozilla",
            installLocation: @"C:\Program Files\Mozilla Firefox",
            displayIcon: @"C:\Program Files\Mozilla Firefox\firefox.exe,0");

        var keys = NameMatcher.NameKeys(app).Select(k => k.Key).ToList();

        Assert.Contains("mozillafirefox", keys);
        Assert.Contains("firefox", keys);
    }

    [Fact]
    public void Exact_name_match_is_exact()
    {
        var keys = NameMatcher.NameKeys(Apps.Make("7-Zip 23.01 (x64)", "Igor Pavlov"));
        var match = NameMatcher.Compare("7-Zip", keys);

        Assert.NotNull(match);
        Assert.Equal(MatchStrength.Exact, match.Strength);
        Assert.Equal(MatchReason.NameExact, match.Reason);
    }

    [Fact]
    public void Program_file_name_match_is_only_likely()
    {
        // VS Code keeps its settings in AppData\Roaming\Code, named after Code.exe.
        var app = Apps.Make(
            "Microsoft Visual Studio Code (User)",
            "Microsoft Corporation",
            displayIcon: @"C:\Users\Ana\AppData\Local\Programs\Microsoft VS Code\Code.exe");

        var match = NameMatcher.Compare("Code", NameMatcher.NameKeys(app));

        Assert.NotNull(match);
        Assert.Equal(MatchStrength.Likely, match.Strength);
        Assert.Equal(MatchReason.ExeName, match.Reason);
    }

    [Fact]
    public void Generic_program_file_names_are_ignored()
    {
        var app = Apps.Make("Some Tool", displayIcon: @"C:\Program Files\Some Tool\launcher.exe");
        Assert.DoesNotContain(NameMatcher.NameKeys(app), k => k.Key == "launcher");
    }

    [Theory]
    [InlineData("Temp")]
    [InlineData("Microsoft")]
    [InlineData("Programs")]
    [InlineData("ab")]
    public void Protected_or_tiny_names_never_match(string folder)
    {
        var app = Apps.Make("Temp Microsoft Programs ab");
        Assert.Null(NameMatcher.Compare(folder, NameMatcher.NameKeys(app)));
    }

    [Fact]
    public void Similar_and_contained_names_are_weaker()
    {
        var keys = NameMatcher.NameKeys(Apps.Make("Discord"));

        Assert.Equal(MatchStrength.Likely, NameMatcher.Compare("DiscordPTB", keys)!.Strength);
        Assert.Equal(MatchStrength.Possible, NameMatcher.Compare("BetterDiscordData", keys)!.Strength);
        Assert.Null(NameMatcher.Compare("Slack", keys));
    }
}
