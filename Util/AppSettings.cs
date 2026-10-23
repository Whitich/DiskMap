using System.IO;

namespace DiskMap.Util;

public static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMap");
    private static readonly string SettingsFile = Path.Combine(Dir, "settings.txt");
    private static readonly string LanguageFile = Path.Combine(Dir, "language.txt");

    public static string LastPath { get; private set; } = string.Empty;
    public static string Language { get; private set; } = Loc.English;

    static AppSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                LastPath = File.ReadAllText(SettingsFile).Trim();
            }
        }
        catch
        {
            // yoksay
        }

        try
        {
            if (File.Exists(LanguageFile))
            {
                Language = Loc.Normalize(File.ReadAllText(LanguageFile).Trim());
            }
        }
        catch
        {
            // yoksay
        }
    }

    public static void SaveLastPath(string path)
    {
        LastPath = path;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsFile, path);
        }
        catch
        {
            // yoksay
        }
    }

    public static void SaveLanguage(string language)
    {
        Language = Loc.Normalize(language);
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(LanguageFile, Language);
        }
        catch
        {
            // yoksay
        }
    }
}
