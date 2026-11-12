using System.Text.RegularExpressions;

namespace AppReset.Core;

/// <summary>Turns app, publisher and folder names into comparable keys.</summary>
public static partial class NameMatcher
{
    /// <summary>Words dropped from publisher names so "Mozilla Corporation" and "Mozilla" compare equal.</summary>
    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "llc", "ltd", "limited", "corp", "corporation", "co", "company",
        "gmbh", "ag", "sa", "sas", "srl", "sro", "bv", "nv", "ab", "as", "oy", "pty", "plc",
        "kg", "spa", "software", "technologies", "technology", "studios", "games", "the",
    };

    /// <summary>Program file names too generic to say anything about the app.</summary>
    private static readonly HashSet<string> GenericExeNames = new(StringComparer.Ordinal)
    {
        "app", "application", "main", "launcher", "setup", "install", "installer", "uninstall",
        "uninst", "unins000", "update", "updater", "helper", "service", "start", "run", "client",
        "msiexec", "rundll32", "explorer", "cmd", "powershell",
    };

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex BracketedText();

    [GeneratedRegex(@"\b(?:64|32)[\s-]?bit\b")]
    private static partial Regex BitnessText();

    [GeneratedRegex(@"\bv?\d+(?:\.\d+)+[a-z0-9]*\b")]
    private static partial Regex VersionText();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonWordChars();

    private static readonly HashSet<string> ArchitectureWords = new(StringComparer.Ordinal)
    {
        "x64", "x86", "amd64", "arm64", "win64", "win32",
    };

    /// <summary>
    /// Lower case words without punctuation, versions, architecture tags or bracketed text.
    /// "7-Zip 23.01 (x64)" becomes "7 zip".
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var t = text.ToLowerInvariant();
        t = BracketedText().Replace(t, " ");
        t = BitnessText().Replace(t, " ");
        t = VersionText().Replace(t, " ");
        t = t.Replace("™", "").Replace("®", "").Replace("©", "");

        var words = NonWordChars()
            .Split(t)
            .Where(w => w.Length > 0 && !ArchitectureWords.Contains(w));

        return string.Join(' ', words);
    }

    /// <summary>Normalized text with the spaces removed. "Notepad++" becomes "notepad".</summary>
    public static string Compact(string? text) => Normalize(text).Replace(" ", "");

    /// <summary>Compact publisher name without company suffixes, or null when nothing useful is left.</summary>
    public static string? PublisherKey(string? publisher)
    {
        var words = PublisherWords(publisher);
        var key = string.Concat(words);
        return key.Length >= 3 ? key : null;
    }

    /// <summary>Folder names that would be the publisher's own folder.</summary>
    public static IReadOnlyList<string> PublisherFolderKeys(string? publisher)
    {
        var words = PublisherWords(publisher);
        var keys = new List<string>();
        var full = string.Concat(words);
        if (full.Length >= 3)
        {
            keys.Add(full);
        }

        if (words.Count > 1 && words[0].Length >= 4 && !keys.Contains(words[0]))
        {
            keys.Add(words[0]);
        }

        return keys;
    }

    /// <summary>
    /// True when a folder or key name is the publisher's own folder, for example
    /// "NVIDIA Corporation" or "Mozilla" for publisher "Mozilla Corporation".
    /// </summary>
    public static bool IsPublisherFolder(string folderName, string? publisher)
    {
        var keys = PublisherFolderKeys(publisher);
        if (keys.Count == 0)
        {
            return false;
        }

        var asPublisher = PublisherKey(folderName);
        return keys.Contains(Compact(folderName)) || (asPublisher is not null && keys.Contains(asPublisher));
    }

    private static List<string> PublisherWords(string? publisher) =>
        Normalize(publisher)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !CompanySuffixes.Contains(w))
            .ToList();

    /// <summary>All the names an app's data folders are likely to use.</summary>
    public static IReadOnlyList<NameKey> NameKeys(InstalledApp app)
    {
        var keys = new List<NameKey>();

        void Add(string? raw, NameKeySource source)
        {
            var key = Compact(raw);
            if (key.Length < 3 || SafetyRules.IsProtectedName(key) || keys.Any(k => k.Key == key))
            {
                return;
            }

            keys.Add(new NameKey(key, source));
        }

        Add(app.DisplayName, NameKeySource.DisplayName);

        // "Mozilla Firefox" by "Mozilla" also gives "Firefox".
        var name = Normalize(app.DisplayName);
        foreach (var publisherKey in PublisherFolderKeys(app.Publisher))
        {
            var withoutPublisher = RemoveLeadingWords(name, publisherKey);
            if (withoutPublisher is not null)
            {
                Add(withoutPublisher, NameKeySource.DisplayName);
            }
        }

        var installFolder = PathText.LastSegment(app.InstallLocation);
        var installKey = Compact(installFolder);
        if (!PublisherFolderKeys(app.Publisher).Contains(installKey))
        {
            Add(installFolder, NameKeySource.InstallFolder);
        }

        var exe = PathText.ExeNameWithoutExtension(app.DisplayIcon);
        if (exe is not null && !GenericExeNames.Contains(Compact(exe)))
        {
            Add(exe, NameKeySource.ExeName);
        }

        return keys;
    }

    /// <summary>
    /// If the normalized name starts with the words of <paramref name="compactPrefix"/>,
    /// return what is left after them.
    /// </summary>
    private static string? RemoveLeadingWords(string normalizedName, string compactPrefix)
    {
        var words = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var built = string.Empty;
        for (var i = 0; i < words.Length - 1; i++)
        {
            built += words[i];
            if (built == compactPrefix)
            {
                return string.Join(' ', words.Skip(i + 1));
            }

            if (built.Length >= compactPrefix.Length)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>Compare a folder or key name with the app's name keys.</summary>
    public static NameMatch? Compare(string candidateName, IReadOnlyList<NameKey> keys)
    {
        var c = Compact(candidateName);
        if (c.Length < 3 || SafetyRules.IsProtectedName(c))
        {
            return null;
        }

        var exact = keys.FirstOrDefault(k => k.Key == c);
        if (exact is not null)
        {
            return exact.Source == NameKeySource.ExeName
                ? new NameMatch(MatchStrength.Likely, MatchReason.ExeName)
                : new NameMatch(MatchStrength.Exact, MatchReason.NameExact);
        }

        foreach (var k in keys)
        {
            if (k.Key.Length >= 4 && c.StartsWith(k.Key, StringComparison.Ordinal))
            {
                return new NameMatch(MatchStrength.Likely, MatchReason.NameSimilar);
            }

            if (c.Length >= 5 && k.Key.StartsWith(c, StringComparison.Ordinal))
            {
                return new NameMatch(MatchStrength.Likely, MatchReason.NameSimilar);
            }
        }

        foreach (var k in keys)
        {
            if (k.Key.Length >= 5 && c.Contains(k.Key, StringComparison.Ordinal))
            {
                return new NameMatch(MatchStrength.Possible, MatchReason.NameContains);
            }
        }

        return null;
    }
}

public enum NameKeySource
{
    DisplayName,
    InstallFolder,
    ExeName,
}

public sealed record NameKey(string Key, NameKeySource Source);

public sealed record NameMatch(MatchStrength Strength, MatchReason Reason);
