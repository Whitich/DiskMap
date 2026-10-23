using DiskMap.Util;

namespace DiskMap.Models;

public sealed class FileRow
{
    public FileRow(FileNode node) => Node = node;

    public FileNode Node { get; }
    public string Name => Node.Name;
    public long Size => Node.Size;
    public string SizeText => Fmt.Size(Node.Size);
    public string TypeLabel => Node.TypeLabel;
    public string ModifiedText => Fmt.Date(Node.LastWriteTime);
    public string Folder => Node.Parent?.FullPath ?? string.Empty;
    public string Path => Node.FullPath;
}

public sealed class TypeRow
{
    public required string Extension { get; init; }
    public required string Label { get; init; }
    public bool IsOther { get; init; }
    public required long Size { get; init; }
    public required int Count { get; init; }
    public required double Percent { get; init; }
    public required System.Windows.Media.SolidColorBrush Brush { get; init; }

    public string SizeText => Fmt.Size(Size);
    public string CountText => Fmt.Count(Count);
}

public sealed record TypeSummary(string Extension, string Label, long Size, int Count);

public sealed record ExtOption(string Ext, string Label)
{
    public override string ToString() => Label;
}

public sealed record SizeOption(long Min, string Label)
{
    public override string ToString() => Label;
}

public sealed record AgeOption(int Days, string Label)
{
    public override string ToString() => Label;
}

public sealed record DriveItem(string Root, string Label)
{
    public override string ToString() => Label;
}
