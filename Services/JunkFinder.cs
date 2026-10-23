using System.IO;
using DiskMap.Models;

namespace DiskMap.Services;

public static class JunkFinder
{
    private const long MinFileSize = 1L << 20;
    private const int MaxItems = 2000;

    private static readonly HashSet<string> StrongJunkDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__", ".vs", ".idea", ".pytest_cache", ".mypy_cache",
        ".ruff_cache", ".gradle", ".next", ".nuxt", ".angular", ".parcel-cache",
        ".terraform", "bower_components", ".packages", ".nuget", ".cache"
    };

    private static readonly HashSet<string> BuildJunkDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "target", "build", "dist", "out", "debug", "release", "x64", "x86", ".build"
    };

    private static readonly HashSet<string> JunkFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".dmp", ".old", ".bak", ".part", ".crdownload"
    };

    private static readonly HashSet<string> ProjectFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "cargo.toml", "go.mod", "pom.xml", "build.gradle", "build.gradle.kts",
        "cmakelists.txt", "requirements.txt", "pyproject.toml", "makefile", "composer.json"
    };

    private static readonly HashSet<string> ProjectFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sln", ".csproj", ".fsproj", ".vbproj", ".vcxproj", ".slnx"
    };

    public static List<JunkItem> Find(FileNode root)
    {
        var items = new List<JunkItem>();
        var stack = new Stack<FileNode>();
        var projectCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        stack.Push(root);

        while (stack.Count > 0 && items.Count < MaxItems)
        {
            var node = stack.Pop();

            if (!node.IsDirectory)
            {
                if (node.Size >= MinFileSize && JunkFileExtensions.Contains(node.Extension))
                {
                    items.Add(new JunkItem
                    {
                        Node = node,
                        ReasonKey = "L.Junk.Reason.Temp",
                        IsChecked = false,
                    });
                }
                continue;
            }

            if (!ReferenceEquals(node, root) && IsJunkDirectory(node, projectCache))
            {
                items.Add(new JunkItem
                {
                    Node = node,
                    ReasonKey = "L.Junk.Reason.Dev",
                    IsChecked = false,
                });
                continue;
            }

            if (!ReferenceEquals(node, root) && node.Children.Count == 0)
            {
                items.Add(new JunkItem
                {
                    Node = node,
                    ReasonKey = "L.Junk.Reason.Empty",
                    IsChecked = false,
                });
                continue;
            }

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        return items.OrderByDescending(item => item.Size).ToList();
    }

    private static bool IsJunkDirectory(FileNode node, Dictionary<string, bool> projectCache)
    {
        if (StrongJunkDirs.Contains(node.Name)) return true;

        if (BuildJunkDirs.Contains(node.Name) && node.Parent is not null)
        {
            return IsProjectDirectory(node.Parent.FullPath, projectCache);
        }

        return false;
    }

    private static bool IsProjectDirectory(string directory, Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(directory, out var cached)) return cached;

        bool result = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (ProjectFileNames.Contains(name) || ProjectFileExtensions.Contains(Path.GetExtension(file)))
                {
                    result = true;
                    break;
                }
            }
        }
        catch
        {
            // erişilemeyen klasörleri yoksay
        }

        cache[directory] = result;
        return result;
    }
}
