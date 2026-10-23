using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using DiskMap.Models;

namespace DiskMap.Services;

public sealed record ScanProgress(
    long Files,
    long Directories,
    long Bytes,
    long Skipped,
    string CurrentPath,
    TimeSpan Elapsed);

public sealed record ScanResult(
    FileNode Root,
    long TotalFiles,
    long TotalDirectories,
    long TotalBytes,
    long Skipped,
    TimeSpan Elapsed);

public sealed class Scanner
{
    public Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        => Task.Run(() => Scan(rootPath, progress, cancellationToken), cancellationToken);

    private static ScanResult Scan(string rootPath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        rootPath = Path.GetFullPath(rootPath);
        var root = new FileNode
        {
            Name = DisplayName(rootPath),
            FullPath = rootPath,
            IsDirectory = true,
            LastWriteTime = SafeTime(new DirectoryInfo(rootPath)),
            CreationTime = SafeCreationTime(new DirectoryInfo(rootPath)),
        };

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        var queue = new ConcurrentQueue<(string Path, FileNode Node)>();
        queue.Enqueue((rootPath, root));

        long pending = 1;
        long active = 0;
        long files = 0;
        long directories = 1;
        long bytes = 0;
        long skipped = 0;
        long lastReport = -1000;

        var watch = Stopwatch.StartNew();
        var gate = new object();

        void Report(string current)
        {
            if (progress is null) return;
            var now = watch.ElapsedMilliseconds;
            if (now - Interlocked.Read(ref lastReport) < 120) return;
            lock (gate)
            {
                if (now - lastReport < 120) return;
                lastReport = now;
                progress.Report(new ScanProgress(
                    Interlocked.Read(ref files),
                    Interlocked.Read(ref directories),
                    Interlocked.Read(ref bytes),
                    Interlocked.Read(ref skipped),
                    current,
                    watch.Elapsed));
            }
        }

        void Process(string path, FileNode parent)
        {
            try
            {
                foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (info is DirectoryInfo directoryInfo)
                    {
                        var child = new FileNode
                        {
                            Name = directoryInfo.Name,
                            FullPath = directoryInfo.FullName,
                            IsDirectory = true,
                            LastWriteTime = SafeTime(directoryInfo),
                            CreationTime = SafeCreationTime(directoryInfo),
                            Parent = parent,
                        };
                        lock (parent.Children) parent.Children.Add(child);
                        Interlocked.Increment(ref directories);
                        Interlocked.Increment(ref pending);
                        queue.Enqueue((directoryInfo.FullName, child));
                    }
                    else
                    {
                        var fileInfo = (FileInfo)info;
                        long length = SafeLength(fileInfo);
                        var child = new FileNode
                        {
                            Name = fileInfo.Name,
                            FullPath = fileInfo.FullName,
                            IsDirectory = false,
                            Size = length,
                            LastWriteTime = SafeTime(fileInfo),
                            CreationTime = SafeCreationTime(fileInfo),
                            Extension = fileInfo.Extension.ToLowerInvariant(),
                            Parent = parent,
                            FileCount = 1,
                        };
                        lock (parent.Children) parent.Children.Add(child);
                        Interlocked.Increment(ref files);
                        Interlocked.Add(ref bytes, length);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                Interlocked.Increment(ref skipped);
            }

            Report(path);
        }

        void Worker()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (queue.TryDequeue(out var item))
                {
                    Interlocked.Decrement(ref pending);
                    Interlocked.Increment(ref active);
                    try
                    {
                        Process(item.Path, item.Node);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                }
                else
                {
                    if (Volatile.Read(ref pending) == 0 && Volatile.Read(ref active) == 0) return;
                    Thread.Sleep(2);
                }
            }
        }

        int workerCount = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var tasks = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            tasks[i] = Task.Run(Worker, cancellationToken);
        }

        try
        {
            Task.WaitAll(tasks, cancellationToken);
        }
        catch (AggregateException aggregate)
        {
            var canceled = aggregate.Flatten().InnerExceptions.OfType<OperationCanceledException>().FirstOrDefault();
            if (canceled is not null) throw canceled;
            var real = aggregate.Flatten().InnerExceptions.FirstOrDefault();
            if (real is not null) throw real;
            throw;
        }

        ComputeAggregates(root);
        watch.Stop();

        return new ScanResult(
            root,
            Interlocked.Read(ref files),
            Interlocked.Read(ref directories),
            Interlocked.Read(ref bytes),
            Interlocked.Read(ref skipped),
            watch.Elapsed);
    }

    private static void ComputeAggregates(FileNode root)
    {
        var stack = new Stack<(FileNode Node, bool Visited)>();
        stack.Push((root, false));

        while (stack.Count > 0)
        {
            var (node, visited) = stack.Pop();
            if (!visited)
            {
                stack.Push((node, true));
                foreach (var child in node.Children)
                {
                    stack.Push((child, false));
                }
                continue;
            }

            if (!node.IsDirectory) continue;

            long size = 0;
            int count = 0;
            foreach (var child in node.Children)
            {
                size += child.Size;
                count += child.FileCount;
            }
            node.Size = size;
            node.FileCount = count;
        }
    }

    private static string DisplayName(string path)
    {
        try
        {
            var name = new DirectoryInfo(path).Name;
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch
        {
            // yoksay
        }
        return path;
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch { return 0; }
    }

    private static DateTime SafeTime(FileSystemInfo info)
    {
        try { return info.LastWriteTime; }
        catch { return default; }
    }

    private static DateTime SafeCreationTime(FileSystemInfo info)
    {
        try { return info.CreationTime; }
        catch { return default; }
    }
}
