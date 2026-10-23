using DiskMap.Util;

namespace DiskMap.Models;

public sealed class FileNode
{
    private string? _dominantExtension;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; set; }
    public int FileCount { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime CreationTime { get; set; }
    public string Extension { get; init; } = string.Empty;
    public FileNode? Parent { get; init; }
    public List<FileNode> Children { get; } = new();

    public bool IsFile => !IsDirectory;

    public string TypeLabel => IsDirectory
        ? Loc.T("L.Common.Folder")
        : Extension.Length > 1 ? Extension[1..].ToUpperInvariant() : Loc.T("L.Common.File");

    public string DominantExtension()
    {
        if (!IsDirectory) return Extension;
        if (_dominantExtension is not null) return _dominantExtension;

        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFiles())
        {
            var ext = file.Extension;
            totals[ext] = totals.TryGetValue(ext, out var current) ? current + file.Size : file.Size;
        }

        _dominantExtension = totals.Count == 0
            ? string.Empty
            : totals.OrderByDescending(pair => pair.Value).First().Key;
        return _dominantExtension;
    }

    public IEnumerable<FileNode> EnumerateFiles()
    {
        var stack = new Stack<FileNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.IsDirectory)
            {
                for (int i = node.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(node.Children[i]);
                }
            }
            else
            {
                yield return node;
            }
        }
    }

    public IEnumerable<FileNode> Ancestors()
    {
        var node = Parent;
        while (node is not null)
        {
            yield return node;
            node = node.Parent;
        }
    }
}
