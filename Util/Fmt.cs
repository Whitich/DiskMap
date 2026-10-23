using System.Globalization;

namespace DiskMap.Util;

public static class Fmt
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Size(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString("N0", CultureInfo.CurrentCulture)} B"
            : $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {Units[unit]}";
    }

    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Date(DateTime value) => value == default
        ? "-"
        : value.ToString("g", CultureInfo.CurrentCulture);

    public static string Percent(double value) => value.ToString("0.#", CultureInfo.CurrentCulture) + "%";
}
