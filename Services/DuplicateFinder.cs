using System.IO;
using DiskMap.Models;
using DiskMap.Util;

namespace DiskMap.Services;

public static class DuplicateFinder
{
    private const long MinSize = 1024 * 1024;
    private const int QuickHashBytes = 64 * 1024;

    public static Task<List<DuplicateGroup>> FindAsync(
        IReadOnlyList<FileNode> files,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Find(files, progress, cancellationToken), cancellationToken);

    private static List<DuplicateGroup> Find(
        IReadOnlyList<FileNode> files,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = files
            .Where(file => file.Size >= MinSize)
            .GroupBy(file => file.Size)
            .Where(group => group.Count() > 1)
            .Select(group => group.ToList())
            .ToList();

        var groups = new List<DuplicateGroup>();
        int done = 0;

        foreach (var bySize in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"{Loc.T("L.Dup.Scanning")} {done}/{candidates.Count}");

            var quickBuckets = new Dictionary<string, List<FileNode>>(StringComparer.Ordinal);
            foreach (var file in bySize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = TryHash(file.FullPath, QuickHashBytes, cancellationToken);
                if (hash is null) continue;

                if (!quickBuckets.TryGetValue(hash, out var bucket))
                {
                    bucket = new List<FileNode>();
                    quickBuckets[hash] = bucket;
                }
                bucket.Add(file);
            }

            foreach (var quickBucket in quickBuckets.Values.Where(bucket => bucket.Count > 1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullBuckets = new Dictionary<string, List<FileNode>>(StringComparer.Ordinal);
                foreach (var file in quickBucket)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hash = TryHash(file.FullPath, 0, cancellationToken);
                    if (hash is null) continue;

                    if (!fullBuckets.TryGetValue(hash, out var bucket))
                    {
                        bucket = new List<FileNode>();
                        fullBuckets[hash] = bucket;
                    }
                    bucket.Add(file);
                }

                foreach (var match in fullBuckets.Values.Where(bucket => bucket.Count > 1))
                {
                    var ordered = match
                        .OrderBy(file => file.LastWriteTime == default ? DateTime.MaxValue : file.LastWriteTime)
                        .ThenBy(file => file.CreationTime == default ? DateTime.MaxValue : file.CreationTime)
                        .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var duplicates = new List<DuplicateFile>();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        duplicates.Add(new DuplicateFile { Node = ordered[i], IsChecked = false });
                    }

                    groups.Add(new DuplicateGroup
                    {
                        ItemSize = bySize[0].Size,
                        Files = duplicates,
                    });
                }
            }

            done++;
        }

        return groups.OrderByDescending(group => group.ReclaimableBytes).ToList();
    }

    private static string? TryHash(string path, int limit, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.SequentialScan);
            using var sha = System.Security.Cryptography.SHA256.Create();

            if (limit > 0)
            {
                var length = (int)Math.Min(limit, stream.Length);
                var buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int chunk = stream.Read(buffer, read, length - read);
                    if (chunk <= 0) break;
                    read += chunk;
                }
                return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
            }

            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
