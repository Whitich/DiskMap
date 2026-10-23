using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskMap.Util;

namespace DiskMap.Models;

public sealed class DuplicateFile : INotifyPropertyChanged
{
    private bool _isChecked;

    public required FileNode Node { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public string Path => Node.FullPath;

    public string DisplayName =>
        $"{Node.Name}  ·  {Fmt.Size(Node.Size)}  ·  {Node.Parent?.FullPath ?? string.Empty}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DuplicateGroup : INotifyPropertyChanged
{
    public required long ItemSize { get; init; }
    public required List<DuplicateFile> Files { get; init; }

    public long ReclaimableBytes => ItemSize * Math.Max(0, Files.Count - 1);
    public string ReclaimableText => Loc.T("L.Dup.Reclaimable", Fmt.Size(ReclaimableBytes));
    public string HeaderText => Loc.T("L.Dup.Header", Files.Count, Fmt.Size(ItemSize));

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalized()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReclaimableText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderText)));
    }
}

public sealed class JunkItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public required FileNode Node { get; init; }
    public required string ReasonKey { get; init; }

    public string Reason => Loc.T(ReasonKey);

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public string Name => Node.Name;
    public long Size => Node.Size;
    public string SizeText => Fmt.Size(Node.Size);
    public string Kind => Node.IsDirectory ? Loc.T("L.Common.Folder") : Loc.T("L.Common.File");
    public string Folder => Node.Parent?.FullPath ?? string.Empty;
    public string Path => Node.FullPath;

    public void RefreshLocalized()
    {
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(Kind));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
