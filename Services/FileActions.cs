using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using DiskMap.Util;
using Microsoft.Win32;

namespace DiskMap.Services;

public static class FileActions
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    public static void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show(Loc.T("L.File.Missing", path), "DiskMap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("L.File.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void RevealInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
            else
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("L.Explorer.OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void CopyPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
        }
        catch
        {
            // pano meşgulse yoksay
        }
    }

    public static (int succeeded, int failed) DeleteToRecycleBin(IEnumerable<string> paths)
    {
        var list = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0) return (0, 0);
        if (GetRecycleBlockReason(list) is not null) return (0, list.Count);

        try
        {
            var from = string.Join('\0', list) + "\0\0";
            var operation = new ShFileOpStruct
            {
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOERRORUI,
            };

            int code = SHFileOperation(ref operation);
            int failed = 0;
            foreach (var path in list)
            {
                if (File.Exists(path) || Directory.Exists(path)) failed++;
            }

            return (list.Count - failed, failed);
        }
        catch
        {
            return (0, list.Count);
        }
    }

    public static string? GetRecycleBlockReason(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string? root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return full;

                if (RecycleBinDisabledByPolicy()) return root;

                var drive = new DriveInfo(root);
                if (!drive.IsReady) return root;
                if (drive.DriveType is DriveType.Network or DriveType.CDRom or DriveType.NoRootDirectory)
                {
                    return root;
                }

                if (!Directory.Exists(Path.Combine(root, "$Recycle.Bin"))) return root;
                if (NukeOnDeleteEnabled(root)) return root;
            }
            catch
            {
                return path;
            }
        }

        return null;
    }

    private static bool RecycleBinDisabledByPolicy()
    {
        try
        {
            using var user = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            if (IsTrue(user?.GetValue("NoRecycleFiles"))) return true;

            using var machine = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            if (IsTrue(machine?.GetValue("NoRecycleFiles"))) return true;
        }
        catch
        {
            // yoksay
        }

        return false;
    }

    private static bool NukeOnDeleteEnabled(string root)
    {
        try
        {
            var volumeName = new StringBuilder(1024);
            if (!GetVolumeNameForVolumeMountPoint(root, volumeName, volumeName.Capacity))
            {
                return true;
            }

            string volume = volumeName.ToString();
            const string prefix = @"\\?\Volume";
            if (!volume.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

            volume = volume[prefix.Length..].TrimEnd('\\');
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{volume}");
            return IsTrue(key?.GetValue("NukeOnDelete"));
        }
        catch
        {
            return true;
        }
    }

    private static bool IsTrue(object? value) => value switch
    {
        int number => number != 0,
        long number => number != 0,
        string text => text == "1",
        _ => false,
    };

    public static bool IsGone(string path) => !File.Exists(path) && !Directory.Exists(path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint, StringBuilder volumeName, int bufferLength);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;

        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref ShFileOpStruct lpFileOp);
}
