using System.Windows.Media;

namespace DiskMap.Util;

public static class TreemapPalette
{
    private static readonly SolidColorBrush RecentBrush = Freeze(Color.FromRgb(0x59, 0xB3, 0x6C));
    private static readonly SolidColorBrush SixMonthsBrush = Freeze(Color.FromRgb(0xC9, 0xA2, 0x27));
    private static readonly SolidColorBrush OneYearBrush = Freeze(Color.FromRgb(0xD9, 0x8A, 0x3D));
    private static readonly SolidColorBrush TwoYearsBrush = Freeze(Color.FromRgb(0xD9, 0x61, 0x5B));
    private static readonly SolidColorBrush UnknownAgeBrush = Freeze(Color.FromRgb(0x58, 0x63, 0x78));

    private static readonly SolidColorBrush[] SizeBrushes = BuildSizeBrushes(64);

    public static SolidColorBrush BrushForAge(DateTime timestamp)
    {
        if (timestamp == default) return UnknownAgeBrush;

        double days = (DateTime.Now - timestamp).TotalDays;
        if (days <= 180) return RecentBrush;
        if (days <= 365) return SixMonthsBrush;
        if (days <= 730) return OneYearBrush;
        return TwoYearsBrush;
    }

    public static SolidColorBrush BrushForSize(long size, long maxSize)
    {
        if (size <= 0 || maxSize <= 0) return SizeBrushes[0];

        double ratio = Math.Log10(size + 1) / Math.Log10(maxSize + 1);
        int index = (int)Math.Round(Math.Clamp(ratio, 0, 1) * (SizeBrushes.Length - 1));
        return SizeBrushes[index];
    }

    private static SolidColorBrush[] BuildSizeBrushes(int count)
    {
        var start = Color.FromRgb(0x33, 0x4C, 0x7D);
        var middle = Color.FromRgb(0x2F, 0x9E, 0x8F);
        var end = Color.FromRgb(0xD7, 0xA1, 0x3F);

        var brushes = new SolidColorBrush[count + 1];
        for (int i = 0; i < brushes.Length; i++)
        {
            double t = (double)i / count;
            Color color = t < 0.5
                ? Lerp(start, middle, t * 2)
                : Lerp(middle, end, (t - 0.5) * 2);
            brushes[i] = Freeze(color);
        }
        return brushes;
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
