using System.Globalization;
using System.IO;
using System.Text;
using DiskMap.Models;
using DiskMap.Util;

namespace DiskMap.Services;

public static class ReportExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static void WriteMarkdown(
        string path,
        ScanResult scan,
        IReadOnlyList<TypeSummary> types,
        IReadOnlyList<FileNode> largest)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("L.Report.Title"));
        sb.AppendLine();
        sb.AppendLine(Loc.T("L.Report.DateLine", Fmt.Date(DateTime.Now)));
        sb.AppendLine(Loc.T("L.Report.LocationLine", scan.Root.FullPath));
        sb.AppendLine(Loc.T("L.Report.TotalSizeLine", Fmt.Size(scan.TotalBytes)));
        sb.AppendLine(Loc.T("L.Report.FilesLine", Fmt.Count(scan.TotalFiles)));
        sb.AppendLine(Loc.T("L.Report.FoldersLine", Fmt.Count(Math.Max(0, scan.TotalDirectories - 1))));
        sb.AppendLine(Loc.T("L.Report.ElapsedLine", scan.Elapsed.TotalSeconds));
        if (scan.Skipped > 0)
        {
            sb.AppendLine(Loc.T("L.Report.SkippedLine", Fmt.Count(scan.Skipped)));
        }

        sb.AppendLine();
        sb.AppendLine(Loc.T("L.Report.TypeDistribution"));
        sb.AppendLine();
        sb.AppendLine(Loc.T("L.Report.TypeHeader"));
        sb.AppendLine("| --- | ---: | ---: | ---: |");

        long total = Math.Max(1, scan.TotalBytes);
        foreach (var type in types.Take(30))
        {
            double share = type.Size * 100.0 / total;
            sb.AppendLine($"| {Escape(type.Label)} | {Fmt.Size(type.Size)} | {Fmt.Count(type.Count)} | {Fmt.Percent(share)} |");
        }

        sb.AppendLine();
        sb.AppendLine(Loc.T("L.Report.Largest"));
        sb.AppendLine();
        sb.AppendLine(Loc.T("L.Report.FileHeader"));
        sb.AppendLine("| --- | ---: | --- | --- |");

        foreach (var file in largest.Take(100))
        {
            sb.AppendLine($"| {Escape(file.Name)} | {Fmt.Size(file.Size)} | {Fmt.Date(file.LastWriteTime)} | {Escape(file.Parent?.FullPath ?? string.Empty)} |");
        }

        File.WriteAllText(path, sb.ToString(), Utf8Bom);
    }

    public static void WriteCsv(string path, ScanResult scan, IReadOnlyList<FileNode> largest)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.T("L.Report.CsvHeader"));
        foreach (var file in largest.Take(5000))
        {
            sb.Append(Csv(file.Name)).Append(',')
              .Append(file.Size.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(Fmt.Size(file.Size))).Append(',')
              .Append(Csv(file.TypeLabel)).Append(',')
              .Append(Csv(Fmt.Date(file.LastWriteTime))).Append(',')
              .Append(Csv(file.Parent?.FullPath ?? string.Empty))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Utf8Bom);
    }

    private static string Escape(string value) => value.Replace("|", "\\|");

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
