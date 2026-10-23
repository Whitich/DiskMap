using System.Windows;

namespace DiskMap.Controls;

public static class TreemapLayout
{
    public static Rect[] Squarify(IReadOnlyList<double> values, Rect bounds)
    {
        int count = values.Count;
        var result = new Rect[count];
        if (count == 0 || bounds.Width <= 0 || bounds.Height <= 0) return result;

        double total = 0;
        for (int i = 0; i < count; i++) total += Math.Max(values[i], 0);
        if (total <= 0) total = count;

        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (a, b) => values[b].CompareTo(values[a]));

        var areas = new double[count];
        double scale = bounds.Width * bounds.Height / total;
        for (int i = 0; i < count; i++) areas[i] = Math.Max(values[i], 0) * scale;

        double x = bounds.X;
        double y = bounds.Y;
        double width = bounds.Width;
        double height = bounds.Height;
        var row = new List<int>();
        int index = 0;

        while (index < count)
        {
            int next = order[index];
            double side = Math.Min(width, height);
            if (row.Count == 0 || Worst(row, areas, side) >= WorstWith(row, next, areas, side))
            {
                row.Add(next);
                index++;
            }
            else
            {
                LayoutRow(row, areas, ref x, ref y, ref width, ref height, result);
                row.Clear();
            }
        }

        if (row.Count > 0)
        {
            LayoutRow(row, areas, ref x, ref y, ref width, ref height, result);
        }

        return result;
    }

    private static double Worst(List<int> row, double[] areas, double side)
    {
        if (row.Count == 0) return double.PositiveInfinity;
        return WorstCore(row, -1, areas, side);
    }

    private static double WorstWith(List<int> row, int extra, double[] areas, double side)
        => WorstCore(row, extra, areas, side);

    private static double WorstCore(List<int> row, int extra, double[] areas, double side)
    {
        double sum = 0;
        double max = 0;
        double min = double.MaxValue;

        foreach (int index in row)
        {
            double area = Math.Max(areas[index], 0);
            sum += area;
            if (area > max) max = area;
            if (area < min) min = area;
        }

        if (extra >= 0)
        {
            double area = Math.Max(areas[extra], 0);
            sum += area;
            if (area > max) max = area;
            if (area < min) min = area;
        }

        if (sum <= 0 || side <= 0) return double.PositiveInfinity;

        double sideSquared = side * side;
        double sumSquared = sum * sum;
        double ratio1 = max <= 0 ? double.PositiveInfinity : sideSquared * max / sumSquared;
        double ratio2 = min <= 0 ? double.PositiveInfinity : sumSquared / (sideSquared * min);
        return Math.Max(ratio1, ratio2);
    }

    private static void LayoutRow(
        List<int> row,
        double[] areas,
        ref double x,
        ref double y,
        ref double width,
        ref double height,
        Rect[] result)
    {
        double sum = 0;
        foreach (int index in row) sum += Math.Max(areas[index], 0);

        if (sum <= 0)
        {
            foreach (int index in row) result[index] = new Rect(x, y, 0, 0);
            return;
        }

        if (width <= height)
        {
            double rowWidth = sum / height;
            if (rowWidth <= 0 || height <= 0) return;
            double top = y;
            foreach (int index in row)
            {
                double itemHeight = Math.Max(areas[index], 0) / rowWidth;
                result[index] = new Rect(x, top, rowWidth, itemHeight);
                top += itemHeight;
            }
            x += rowWidth;
            width = Math.Max(0, width - rowWidth);
        }
        else
        {
            double rowHeight = sum / width;
            if (rowHeight <= 0 || width <= 0) return;
            double left = x;
            foreach (int index in row)
            {
                double itemWidth = Math.Max(areas[index], 0) / rowHeight;
                result[index] = new Rect(left, y, itemWidth, rowHeight);
                left += itemWidth;
            }
            y += rowHeight;
            height = Math.Max(0, height - rowHeight);
        }
    }
}
