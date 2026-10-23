using System.Globalization;
using System.Windows;

namespace DiskMap.Util;

public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}

public static class Loc
{
    public const string English = "en";
    public const string Turkish = "tr";

    private static readonly LanguageChoice[] Choices =
    {
        new(English, "English"),
        new(Turkish, "Türkçe"),
    };

    private static Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private static ResourceDictionary? _appDictionary;

    public static string CurrentLanguage { get; private set; } = English;
    public static IReadOnlyList<LanguageChoice> Languages => Choices;

    public static event EventHandler? LanguageChanged;

    public static void Initialize() => SetLanguage(AppSettings.Language, persist: false);

    public static void SetLanguage(string? language, bool persist = true)
    {
        string normalized = Normalize(language);
        var culture = CultureInfo.GetCultureInfo(normalized == Turkish ? "tr-TR" : "en-US");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var dictionary = TryLoad(normalized);
        if (dictionary is null && normalized != English)
        {
            normalized = English;
            dictionary = TryLoad(English);
        }

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dictionary is not null)
        {
            foreach (var keyObject in dictionary.Keys)
            {
                string? key = keyObject?.ToString();
                if (string.IsNullOrEmpty(key)) continue;
                if (dictionary[key] is string value)
                {
                    strings[key] = value;
                }
            }
        }

        _strings = strings;

        var app = Application.Current;
        if (app is not null)
        {
            var merged = app.Resources.MergedDictionaries;
            if (_appDictionary is not null)
            {
                merged.Remove(_appDictionary);
            }

            if (dictionary is not null)
            {
                merged.Add(dictionary);
                _appDictionary = dictionary;
            }
            else
            {
                _appDictionary = null;
            }
        }

        if (persist)
        {
            AppSettings.SaveLanguage(normalized);
        }

        bool changed = !string.Equals(CurrentLanguage, normalized, StringComparison.Ordinal);
        CurrentLanguage = normalized;
        if (changed || persist)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string T(string key)
    {
        var strings = _strings;
        return strings.TryGetValue(key, out var value) ? value : key;
    }

    public static string T(string key, params object?[] args)
    {
        string format = T(key);
        return args.Length == 0
            ? format
            : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    public static string Normalize(string? language) =>
        string.Equals(language, Turkish, StringComparison.OrdinalIgnoreCase) ? Turkish : English;

    private static ResourceDictionary? TryLoad(string language)
    {
        try
        {
            return new ResourceDictionary
            {
                Source = new Uri($"Localization/Strings.{language}.xaml", UriKind.Relative),
            };
        }
        catch
        {
            return null;
        }
    }
}
