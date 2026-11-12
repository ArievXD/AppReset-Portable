using System.Globalization;
using System.Windows;

namespace AppReset.App.Infrastructure;

/// <summary>
/// UI text lives in Strings/Strings.{en,id}.xaml. XAML reads it with DynamicResource, so a
/// language switch updates the window at once; code reads it through this class.
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string Indonesian = "id";

    private const string StringsFolder = "Strings/";

    public static string Language { get; private set; } = English;

    /// <summary>Culture used for dates and numbers, following the chosen language.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static string DefaultLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Indonesian ? Indonesian : English;

    public static void SetLanguage(string language)
    {
        language = language == Indonesian ? Indonesian : English;
        var merged = Application.Current.Resources.MergedDictionaries;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AppReset;component/{StringsFolder}Strings.{language}.xaml"),
        };

        var existing = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains(StringsFolder) == true);
        if (existing is null)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[merged.IndexOf(existing)] = dictionary;
        }

        Language = language;
        Culture = CultureInfo.GetCultureInfo(language == Indonesian ? "id-ID" : "en-US");
    }

    public static string Get(string key) => Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    public static string Size(long? bytes)
    {
        if (bytes is null)
        {
            return Get("Item.SizeUnknown");
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(Culture, "{0} {1}", bytes.Value, units[0])
            : string.Format(Culture, "{0:0.#} {1}", value, units[unit]);
    }

    public static string Date(DateTimeOffset date) => date.LocalDateTime.ToString("f", Culture);
}
