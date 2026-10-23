using System.Windows.Media;

namespace DiskMap.Util;

public static class ExtensionPalette
{
    private static readonly Dictionary<string, Color> Categories = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Color[] Fallback =
    {
        Color.FromRgb(0x4C, 0x8D, 0xFF),
        Color.FromRgb(0x59, 0xB3, 0x6C),
        Color.FromRgb(0xD6, 0x9A, 0x45),
        Color.FromRgb(0xB0, 0x69, 0xD6),
        Color.FromRgb(0x3F, 0xA7, 0xA0),
        Color.FromRgb(0xD9, 0x61, 0x5B),
        Color.FromRgb(0xC9, 0xA2, 0x27),
        Color.FromRgb(0x7C, 0x88, 0xB8),
        Color.FromRgb(0xC0, 0x5C, 0x9E),
        Color.FromRgb(0x52, 0xA3, 0xC9),
    };

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Color NoExtensionColor = Color.FromRgb(0x58, 0x63, 0x78);

    static ExtensionPalette()
    {
        Add(Color.FromRgb(0xD9, 0x61, 0x5B),
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts");

        Add(Color.FromRgb(0x59, 0xB3, 0x6C),
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff",
            ".raw", ".cr2", ".nef", ".heic", ".psd");

        Add(Color.FromRgb(0xB0, 0x69, 0xD6),
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus", ".mid", ".midi");

        Add(Color.FromRgb(0xD6, 0x9A, 0x45),
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".zst");

        Add(Color.FromRgb(0x4C, 0x8D, 0xFF),
            ".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".kt", ".cpp", ".cc", ".c", ".h",
            ".hpp", ".go", ".rs", ".rb", ".php", ".html", ".htm", ".css", ".scss", ".json", ".xml",
            ".yml", ".yaml", ".sql", ".sh", ".ps1", ".bat", ".cmd", ".lua", ".swift", ".dart");

        Add(Color.FromRgb(0x3F, 0xA7, 0xA0),
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".rtf",
            ".odt", ".csv", ".epub", ".mobi");

        Add(Color.FromRgb(0x7C, 0x88, 0xB8),
            ".exe", ".dll", ".msi", ".sys", ".mui", ".pdb", ".lib", ".obj", ".ocx", ".drv", ".cpl");

        Add(Color.FromRgb(0xC0, 0x5C, 0x9E),
            ".vhd", ".vhdx", ".vmdk", ".dmg", ".img", ".wim", ".esd", ".vdi");

        Add(Color.FromRgb(0xC9, 0xA2, 0x27),
            ".ttf", ".otf", ".woff", ".woff2", ".eot");

        Add(Color.FromRgb(0x52, 0xA3, 0xC9),
            ".db", ".sqlite", ".mdb", ".accdb", ".dat", ".bin", ".pak", ".asset", ".bundle");
    }

    private static void Add(Color color, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            Categories[extension] = color;
        }
    }

    public static Color ColorFor(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return NoExtensionColor;
        if (Categories.TryGetValue(extension, out var color)) return color;

        int hash = 17;
        foreach (char ch in extension.ToLowerInvariant())
        {
            hash = unchecked(hash * 31 + ch);
        }
        return Fallback[Math.Abs(hash) % Fallback.Length];
    }

    public static SolidColorBrush BrushFor(string extension)
    {
        var key = extension ?? string.Empty;
        lock (BrushCache)
        {
            if (BrushCache.TryGetValue(key, out var brush)) return brush;
            var created = new SolidColorBrush(ColorFor(key));
            created.Freeze();
            BrushCache[key] = created;
            return created;
        }
    }
}
