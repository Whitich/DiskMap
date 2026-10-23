using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DiskMap.Controls;
using DiskMap.Models;
using DiskMap.Services;
using DiskMap.Util;
using Microsoft.Win32;

namespace DiskMap;

public partial class MainWindow : Window
{
    private readonly Scanner _scanner = new();
    private readonly ObservableCollection<DuplicateGroup> _dupGroups = new();
    private readonly ObservableCollection<JunkItem> _junkItems = new();

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _dupCts;
    private ScanResult? _scan;
    private FileNode? _viewRoot;
    private FileNode[] _allFiles = Array.Empty<FileNode>();
    private FileNode? _ctxNode;
    private string? _typeFilter;
    private bool _scanning;
    private bool _suppressTypeSelection;
    private bool _stale;
    private bool _suppressDriveSelection;
    private bool _suppressLanguageChange;
    private bool _suppressColorMode;

    private string? _statusOverrideKey;
    private object?[] _statusOverrideArgs = Array.Empty<object?>();
    private string _dupStatusKey = "L.Dup.NotScanned";
    private object?[] _dupStatusArgs = Array.Empty<object?>();
    private string _junkStatusKey = "L.Junk.NotScanned";
    private object?[] _junkStatusArgs = Array.Empty<object?>();
    private string _searchStatusKey = "L.Search.Ready";
    private object?[] _searchStatusArgs = Array.Empty<object?>();

    public MainWindow()
    {
        InitializeComponent();

        DuplicateList.ItemsSource = _dupGroups;
        JunkGrid.ItemsSource = _junkItems;
        RefreshColumnHeaders();

        BuildSearchOptions();
        BuildSearchExtensions();
        LoadDrives();
        SetupLanguageSelector();

        _suppressColorMode = true;
        ColorModeCombo.SelectedIndex = 0;
        _suppressColorMode = false;
        ApplyColorMode();

        Treemap.ContextMenuRequested += Treemap_ContextMenuRequested;
        LargestGrid.ContextMenuOpening += FileGrid_ContextMenuOpening;
        SearchGrid.ContextMenuOpening += FileGrid_ContextMenuOpening;
        BuildGridContextMenus();

        Loc.LanguageChanged += Loc_LanguageChanged;

        var args = Environment.GetCommandLineArgs();
        string? initial = null;
        if (args.Length > 1 && Directory.Exists(args[1]))
        {
            initial = args[1];
        }
        else if (!string.IsNullOrWhiteSpace(AppSettings.LastPath) && Directory.Exists(AppSettings.LastPath))
        {
            initial = AppSettings.LastPath;
        }

        PathBox.Text = initial ?? (DriveCombo.SelectedItem as DriveItem)?.Root ?? "C:\\";

        Loaded += async (_, _) =>
        {
            if (args.Length > 1 && Directory.Exists(args[1]))
            {
                await StartScanAsync();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
        }
        catch
        {
            // eski Windows sürümlerinde yoksay
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ================== Tarama ==================

    private void LoadDrives()
    {
        string? selectedRoot = (DriveCombo.SelectedItem as DriveItem)?.Root;
        var items = new List<DriveItem>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    string label = Loc.T("L.Drive.Label",
                        drive.Name.TrimEnd('\\'),
                        Fmt.Size(drive.AvailableFreeSpace),
                        Fmt.Size(drive.TotalSize));
                    items.Add(new DriveItem(drive.RootDirectory.FullName, label));
                }
                catch
                {
                    // erişilemeyen sürücüyü yoksay
                }
            }
        }
        catch
        {
            // sürücü listesi alınamadı
        }

        _suppressDriveSelection = true;
        DriveCombo.ItemsSource = items;
        if (items.Count > 0)
        {
            DriveCombo.SelectedItem = items.FirstOrDefault(item =>
                string.Equals(item.Root, selectedRoot, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        }
        _suppressDriveSelection = false;
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDriveSelection) return;
        if (DriveCombo.SelectedItem is DriveItem item)
        {
            PathBox.Text = item.Root;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("L.Browse.Tooltip"),
            Multiselect = false,
        };

        if (Directory.Exists(PathBox.Text))
        {
            dialog.InitialDirectory = PathBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
        }
    }

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = StartScanAsync();
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await StartScanAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        ScanOverlayText.Text = Loc.T("L.Status.Canceling");
        SetStatusOverride("L.Status.Canceling");
    }

    private async Task StartScanAsync()
    {
        if (_scanning) return;

        string path = (PathBox.Text ?? string.Empty).Trim().Trim('"');
        if (!Directory.Exists(path))
        {
            MessageBox.Show(this, Loc.T("L.Msg.InvalidPath"), "DiskMap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scanning = true;
        _scanCts = new CancellationTokenSource();
        SetScanningUi(true);
        ScanOverlayText.Text = Loc.T("L.Status.Preparing");
        SetStatusOverride("L.Status.Scanning");

        var progress = new Progress<ScanProgress>(scanProgress =>
        {
            ScanOverlayText.Text = Loc.T("L.Status.Progress",
                Fmt.Count(scanProgress.Files),
                Fmt.Size(scanProgress.Bytes),
                Shorten(scanProgress.CurrentPath));
        });

        try
        {
            var result = await _scanner.ScanAsync(path, progress, _scanCts.Token);

            _scan = result;
            _viewRoot = result.Root;
            _stale = false;
            AppSettings.SaveLastPath(path);

            _allFiles = result.Root.EnumerateFiles().ToArray();
            Array.Sort(_allFiles, (a, b) => b.Size.CompareTo(a.Size));

            _dupGroups.Clear();
            _junkItems.Clear();
            _dupStatusKey = "L.Dup.NotScanned";
            _dupStatusArgs = Array.Empty<object?>();
            _junkStatusKey = "L.Junk.NotScanned";
            _junkStatusArgs = Array.Empty<object?>();
            UpdateDupSummary();
            UpdateJunkSummary();

            Treemap.SetRoot(result.Root);
            BuildBreadcrumb(result.Root);
            _typeFilter = null;
            BuildTypes();
            BuildSearchExtensions();
            _suppressTypeSelection = true;
            TypesList.SelectedItem = null;
            _suppressTypeSelection = false;
            ShowLargest(null);

            SearchGrid.ItemsSource = null;
            SetSearchStatus("L.Search.Ready");
            HoverText.Text = string.Empty;

            ClearStatusOverride();
            RefreshStatusText();
        }
        catch (OperationCanceledException)
        {
            SetStatusOverride("L.Status.Canceled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("L.Msg.ScanError", ex.Message),
                "DiskMap", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearStatusOverride();
            RefreshStatusText();
        }
        finally
        {
            _scanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
            SetScanningUi(false);
        }
    }

    private void SetScanningUi(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        BrowseButton.IsEnabled = !scanning;
        PathBox.IsEnabled = !scanning;
        DriveCombo.IsEnabled = !scanning;
        CancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanOverlay.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        if (!scanning) ScanOverlayText.Text = string.Empty;
    }

    // ================== Görünüm / liste doldurma ==================

    private void BuildBreadcrumb(FileNode node)
    {
        BreadcrumbPanel.Children.Clear();

        var chain = new List<FileNode>();
        for (var current = node; current is not null; current = current.Parent)
        {
            chain.Add(current);
        }
        chain.Reverse();

        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = "›",
                    Foreground = (Brush)FindResource("MutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0),
                });
            }

            var target = chain[i];
            var button = new Button
            {
                Content = string.IsNullOrEmpty(target.Name) ? target.FullPath : target.Name,
                Style = (Style)FindResource("SubtleButton"),
                Height = 26,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0),
            };
            button.Click += (_, _) => EnterNode(target, force: true);
            BreadcrumbPanel.Children.Add(button);
        }
    }

    private void EnterNode(FileNode node, bool force = false)
    {
        if (!node.IsDirectory) return;
        if (!force && ReferenceEquals(_viewRoot, node)) return;

        _viewRoot = node;
        Treemap.SetRoot(node);
        BuildBreadcrumb(node);
        HoverText.Text = string.Empty;
    }

    private void BuildTypes()
    {
        if (_scan is null)
        {
            TypesList.ItemsSource = null;
            TypesHeader.Text = Loc.T("L.Types.Waiting");
            return;
        }

        long total = _allFiles.Sum(file => file.Size);
        var groups = _allFiles
            .GroupBy(file => file.Extension)
            .Select(group => new { Ext = group.Key, Size = group.Sum(file => file.Size), Count = group.Count() })
            .OrderByDescending(group => group.Size)
            .ToList();

        var rows = new List<TypeRow>();
        foreach (var group in groups.Take(28))
        {
            rows.Add(new TypeRow
            {
                Extension = group.Ext,
                Label = string.IsNullOrEmpty(group.Ext)
                    ? Loc.T("L.Types.NoExtension")
                    : group.Ext.TrimStart('.').ToUpperInvariant(),
                Size = group.Size,
                Count = group.Count,
                Percent = total > 0 ? group.Size * 100.0 / total : 0,
                Brush = ExtensionPalette.BrushFor(group.Ext),
            });
        }

        if (groups.Count > 28)
        {
            var rest = groups.Skip(28).ToList();
            long restSize = rest.Sum(group => group.Size);
            rows.Add(new TypeRow
            {
                Extension = string.Empty,
                Label = Loc.T("L.Types.Other"),
                IsOther = true,
                Size = restSize,
                Count = rest.Sum(group => group.Count),
                Percent = total > 0 ? restSize * 100.0 / total : 0,
                Brush = ExtensionPalette.BrushFor("__other"),
            });
        }

        _suppressTypeSelection = true;
        TypesList.ItemsSource = rows;
        TypesList.SelectedItem = null;
        _suppressTypeSelection = false;

        TypesHeader.Text = Loc.T("L.Types.Header", Fmt.Count(_scan.TotalFiles), Fmt.Size(_scan.TotalBytes));
    }

    private void BuildSearchOptions()
    {
        long minSize = (SearchSize.SelectedItem as SizeOption)?.Min ?? 0;
        var sizes = new[]
        {
            new SizeOption(0, Loc.T("L.Search.AllSizes")),
            new SizeOption(10L << 20, Loc.T("L.Search.Size10MB")),
            new SizeOption(100L << 20, Loc.T("L.Search.Size100MB")),
            new SizeOption(1L << 30, Loc.T("L.Search.Size1GB")),
            new SizeOption(10L << 30, Loc.T("L.Search.Size10GB")),
        };
        SearchSize.ItemsSource = sizes;
        SearchSize.SelectedItem = sizes.FirstOrDefault(option => option.Min == minSize) ?? sizes[0];

        int days = (SearchAge.SelectedItem as AgeOption)?.Days ?? 0;
        var ages = new[]
        {
            new AgeOption(0, Loc.T("L.Search.AllTime")),
            new AgeOption(180, Loc.T("L.Search.Older6m")),
            new AgeOption(365, Loc.T("L.Search.Older1y")),
            new AgeOption(730, Loc.T("L.Search.Older2y")),
        };
        SearchAge.ItemsSource = ages;
        SearchAge.SelectedItem = ages.FirstOrDefault(option => option.Days == days) ?? ages[0];
    }

    private void BuildSearchExtensions()
    {
        string selected = (SearchExt.SelectedItem as ExtOption)?.Ext ?? string.Empty;

        var extensions = _allFiles
            .GroupBy(file => file.Extension)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .OrderByDescending(group => group.Sum(file => file.Size))
            .Select(group => group.Key)
            .Take(80)
            .ToList();

        var options = new List<ExtOption> { new(string.Empty, Loc.T("L.Search.AllTypes")) };
        options.AddRange(extensions.Select(ext => new ExtOption(ext, ext.TrimStart('.').ToUpperInvariant())));
        SearchExt.ItemsSource = options;
        SearchExt.SelectedItem = options.FirstOrDefault(option =>
            string.Equals(option.Ext, selected, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }

    private void ShowLargest(string? extensionFilter)
    {
        if (_scan is null)
        {
            LargestGrid.ItemsSource = null;
            LargestHeader.Text = Loc.T("L.Largest.Waiting");
            ClearFilterButton.Visibility = Visibility.Collapsed;
            return;
        }

        IEnumerable<FileNode> query = _allFiles;
        if (!string.IsNullOrEmpty(extensionFilter))
        {
            query = query.Where(file => string.Equals(file.Extension, extensionFilter, StringComparison.OrdinalIgnoreCase));
        }

        LargestGrid.ItemsSource = query.Take(500).Select(file => new FileRow(file)).ToList();
        LargestHeader.Text = string.IsNullOrEmpty(extensionFilter)
            ? Loc.T("L.Largest.All")
            : Loc.T("L.Largest.Filtered", extensionFilter.TrimStart('.').ToUpperInvariant());
        ClearFilterButton.Visibility = string.IsNullOrEmpty(extensionFilter) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TypesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTypeSelection || _scan is null) return;
        if (TypesList.SelectedItem is not TypeRow row) return;

        if (row.IsOther || string.IsNullOrEmpty(row.Extension))
        {
            _suppressTypeSelection = true;
            TypesList.SelectedItem = null;
            _suppressTypeSelection = false;
            return;
        }

        _typeFilter = row.Extension;
        ShowLargest(_typeFilter);
        MainTabs.SelectedIndex = 1;
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        _typeFilter = null;
        _suppressTypeSelection = true;
        TypesList.SelectedItem = null;
        _suppressTypeSelection = false;
        ShowLargest(null);
    }

    // ================== Treemap ==================

    private void Treemap_NodeActivated(object? sender, FileNode node)
    {
        if (node.IsDirectory)
        {
            EnterNode(node);
        }
        else
        {
            FileActions.Open(node.FullPath);
        }
    }

    private void Treemap_HoverChanged(object? sender, FileNode? node)
    {
        if (node is null || _viewRoot is null)
        {
            HoverText.Text = string.Empty;
            return;
        }

        double percent = _viewRoot.Size > 0 ? node.Size * 100.0 / _viewRoot.Size : 0;
        HoverText.Text = $"{node.Name}  ·  {Fmt.Size(node.Size)}  ·  {Fmt.Percent(percent)}  ·  {node.FullPath}";
    }

    private void ColorModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressColorMode) return;
        ApplyColorMode();
    }

    private void ApplyColorMode()
    {
        string? tag = (ColorModeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var mode = tag switch
        {
            "Age" => TreemapColorMode.Age,
            "Size" => TreemapColorMode.Size,
            _ => TreemapColorMode.Type,
        };

        Treemap.SetColorMode(mode);
        AgeLegend.Visibility = mode == TreemapColorMode.Age ? Visibility.Visible : Visibility.Collapsed;
        SizeLegend.Visibility = mode == TreemapColorMode.Size ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================== Kopyalar ==================

    private void SetDupStatus(string key, params object?[] args)
    {
        _dupStatusKey = key;
        _dupStatusArgs = args;
        DupStatus.Text = Loc.T(key, args);
    }

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null)
        {
            MessageBox.Show(this, Loc.T("L.Msg.ScanFirst"), "DiskMap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_dupCts is not null) return;

        _dupCts = new CancellationTokenSource();
        FindDuplicatesButton.IsEnabled = false;
        StopDuplicatesButton.Visibility = Visibility.Visible;
        DupProgress.Visibility = Visibility.Visible;
        DuplicateList.IsEnabled = false;
        SetDupStatus("L.Dup.Scanning");

        var progress = new Progress<string>(text => DupStatus.Text = text);

        try
        {
            var groups = await DuplicateFinder.FindAsync(_allFiles, progress, _dupCts.Token);

            _dupGroups.Clear();
            foreach (var group in groups.Take(400))
            {
                foreach (var file in group.Files)
                {
                    file.PropertyChanged += DuplicateFile_PropertyChanged;
                }
                _dupGroups.Add(group);
            }

            if (groups.Count == 0)
            {
                SetDupStatus("L.Dup.NotFound");
            }
            else
            {
                SetDupStatus("L.Dup.Summary",
                    Fmt.Count(groups.Count),
                    Fmt.Size(groups.Sum(group => group.ReclaimableBytes)));
            }
            UpdateDupSummary();
        }
        catch (OperationCanceledException)
        {
            SetDupStatus("L.Dup.Stopped");
        }
        catch (Exception ex)
        {
            SetDupStatus("L.Dup.Error", ex.Message);
        }
        finally
        {
            DuplicateList.IsEnabled = true;
            FindDuplicatesButton.IsEnabled = true;
            StopDuplicatesButton.Visibility = Visibility.Collapsed;
            DupProgress.Visibility = Visibility.Collapsed;
            _dupCts.Dispose();
            _dupCts = null;
        }
    }

    private void StopDuplicates_Click(object sender, RoutedEventArgs e) => _dupCts?.Cancel();

    private void DuplicateFile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateFile.IsChecked)) UpdateDupSummary();
    }

    private void UpdateDupSummary()
    {
        long selected = _dupGroups
            .SelectMany(group => group.Files)
            .Where(file => file.IsChecked)
            .Sum(file => file.Node.Size);

        DupTotal.Text = _dupGroups.Count == 0
            ? "—"
            : Loc.T("L.Dup.Selected", Fmt.Size(selected));
        DeleteDupButton.IsEnabled = selected > 0;
        SelectDupExtrasButton.IsEnabled = _dupGroups.Count > 0;
    }

    private void SelectDupExtras_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _dupGroups)
        {
            for (int i = 0; i < group.Files.Count; i++)
            {
                group.Files[i].IsChecked = i > 0;
            }
        }
        UpdateDupSummary();
    }

    private void DeleteDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var selectedFiles = _dupGroups.SelectMany(group => group.Files).Where(file => file.IsChecked).ToList();
        if (selectedFiles.Count == 0) return;

        long size = selectedFiles.Sum(file => file.Node.Size);
        var paths = selectedFiles.Select(file => file.Path).ToList();
        var blockedRoot = FileActions.GetRecycleBlockReason(paths);
        if (blockedRoot is not null)
        {
            ShowRecycleUnavailable(blockedRoot);
            return;
        }

        bool confirmed = ConfirmWindow.Show(this,
            Loc.T("L.Dup.Confirm", Fmt.Count(selectedFiles.Count), Fmt.Size(size)),
            Loc.T("L.Confirm.Move"));
        if (!confirmed) return;

        var (ok, failed) = FileActions.DeleteToRecycleBin(paths);
        var gone = paths.Where(FileActions.IsGone).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in _dupGroups.ToList())
        {
            group.Files.RemoveAll(file => gone.Contains(file.Path));
            if (group.Files.Count < 2) _dupGroups.Remove(group);
        }

        RemoveFromModel(gone);
        UpdateDupSummary();
        SetDupStatus(failed > 0 ? "L.Dup.MovedFailed" : "L.Dup.Moved",
            Fmt.Count(ok), Fmt.Count(failed));
        StatusStale();
    }

    // ================== Temizlik ==================

    private void SetJunkStatus(string key, params object?[] args)
    {
        _junkStatusKey = key;
        _junkStatusArgs = args;
        JunkStatus.Text = Loc.T(key, args);
    }

    private async void ScanJunk_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null)
        {
            MessageBox.Show(this, Loc.T("L.Msg.ScanFirst"), "DiskMap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ScanJunkButton.IsEnabled = false;
        SetJunkStatus("L.Junk.Scanning");

        try
        {
            var items = await Task.Run(() => JunkFinder.Find(_scan.Root));

            _junkItems.Clear();
            foreach (var item in items)
            {
                item.PropertyChanged += JunkItem_PropertyChanged;
                _junkItems.Add(item);
            }

            if (items.Count == 0)
            {
                SetJunkStatus("L.Junk.NotFound");
            }
            else
            {
                SetJunkStatus("L.Junk.Summary",
                    Fmt.Count(items.Count),
                    Fmt.Size(items.Sum(item => item.Size)));
            }
            UpdateJunkSummary();
        }
        catch (Exception ex)
        {
            SetJunkStatus("L.Junk.Error", ex.Message);
        }
        finally
        {
            ScanJunkButton.IsEnabled = true;
        }
    }

    private void JunkSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _junkItems) item.IsChecked = true;
        UpdateJunkSummary();
    }

    private void JunkClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _junkItems) item.IsChecked = false;
        UpdateJunkSummary();
    }

    private void JunkItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JunkItem.IsChecked)) UpdateJunkSummary();
    }

    private void UpdateJunkSummary()
    {
        long selected = _junkItems.Where(item => item.IsChecked).Sum(item => item.Size);
        JunkTotalText.Text = _junkItems.Count == 0
            ? "—"
            : Loc.T("L.Junk.Selected", Fmt.Size(selected));
        DeleteJunkButton.IsEnabled = selected > 0;
    }

    private void DeleteJunk_Click(object sender, RoutedEventArgs e)
    {
        var selected = _junkItems.Where(item => item.IsChecked).ToList();
        if (selected.Count == 0) return;

        long size = selected.Sum(item => item.Size);
        var paths = selected.Select(item => item.Path).ToList();
        var blockedRoot = FileActions.GetRecycleBlockReason(paths);
        if (blockedRoot is not null)
        {
            ShowRecycleUnavailable(blockedRoot);
            return;
        }

        bool confirmed = ConfirmWindow.Show(this,
            Loc.T("L.Junk.Confirm", Fmt.Count(selected.Count), Fmt.Size(size)),
            Loc.T("L.Confirm.Move"));
        if (!confirmed) return;

        var (ok, failed) = FileActions.DeleteToRecycleBin(paths);
        var gone = paths.Where(FileActions.IsGone).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _junkItems.ToList())
        {
            if (gone.Contains(item.Path)) _junkItems.Remove(item);
        }

        RemoveFromModel(gone);
        UpdateJunkSummary();
        SetJunkStatus(failed > 0 ? "L.Junk.MovedFailed" : "L.Junk.Moved",
            Fmt.Count(ok), Fmt.Count(failed));
        StatusStale();
    }

    // ================== Arama ==================

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RunSearch();
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        if (_scan is null)
        {
            MessageBox.Show(this, Loc.T("L.Msg.ScanFirst"), "DiskMap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string name = (SearchName.Text ?? string.Empty).Trim();
        string extension = (SearchExt.SelectedItem as ExtOption)?.Ext ?? string.Empty;
        long minSize = (SearchSize.SelectedItem as SizeOption)?.Min ?? 0;
        int days = (SearchAge.SelectedItem as AgeOption)?.Days ?? 0;
        DateTime cutoff = days > 0 ? DateTime.Now.AddDays(-days) : default;

        var results = new List<FileRow>();
        foreach (var file in _allFiles)
        {
            if (minSize > 0 && file.Size < minSize) continue;
            if (extension.Length > 0 && !string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (cutoff != default && (file.LastWriteTime == default || file.LastWriteTime >= cutoff)) continue;
            if (name.Length > 0 && file.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;

            results.Add(new FileRow(file));
            if (results.Count >= 2000) break;
        }

        SearchGrid.ItemsSource = results;
        SetSearchStatus(results.Count >= 2000 ? "L.Search.First2000" : "L.Search.Results", Fmt.Count(results.Count));
    }

    private void SetSearchStatus(string key, params object?[] args)
    {
        _searchStatusKey = key;
        _searchStatusArgs = args;
        SearchCount.Text = Loc.T(key, args);
    }

    // ================== Rapor ==================

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null)
        {
            MessageBox.Show(this, Loc.T("L.Msg.ScanFirst"), "DiskMap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("L.Report.SaveTitle"),
            Filter = "Markdown (*.md)|*.md|CSV (*.csv)|*.csv",
            FileName = $"diskmap-{DateTime.Now:yyyyMMdd-HHmm}.md",
            DefaultExt = ".md",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        var types = _allFiles
            .GroupBy(file => file.Extension)
            .Select(group => new TypeSummary(
                group.Key,
                string.IsNullOrEmpty(group.Key)
                    ? Loc.T("L.Types.NoExtension")
                    : group.Key.TrimStart('.').ToUpperInvariant(),
                group.Sum(file => file.Size),
                group.Count()))
            .OrderByDescending(type => type.Size)
            .ToList();

        try
        {
            bool csv = dialog.FilterIndex == 2 ||
                       dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

            if (csv) ReportExporter.WriteCsv(dialog.FileName, _scan, _allFiles);
            else ReportExporter.WriteMarkdown(dialog.FileName, _scan, types, _allFiles);

            MessageBox.Show(this, Loc.T("L.Report.Saved", dialog.FileName),
                "DiskMap", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("L.Report.WriteError", ex.Message),
                "DiskMap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================== Bağlam menüsü / yardımcılar ==================

    private void FileGrid_ContextMenuOpening(object? sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        _ctxNode = (grid.SelectedItem as FileRow)?.Node;
        if (_ctxNode is null) e.Handled = true;
    }

    private void BuildGridContextMenus()
    {
        LargestGrid.ContextMenu = CreateFileMenu();
        SearchGrid.ContextMenu = CreateFileMenu();
    }

    private ContextMenu CreateFileMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem(Loc.T("L.Menu.Open"), () =>
        {
            if (_ctxNode is not null) FileActions.Open(_ctxNode.FullPath);
        }));
        menu.Items.Add(CreateMenuItem(Loc.T("L.Menu.Reveal"), () =>
        {
            if (_ctxNode is not null) FileActions.RevealInExplorer(_ctxNode.FullPath);
        }));
        menu.Items.Add(CreateMenuItem(Loc.T("L.Menu.CopyPath"), () =>
        {
            if (_ctxNode is not null) FileActions.CopyPath(_ctxNode.FullPath);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(Loc.T("L.Menu.MoveToRecycle"), DeleteContextNode,
            (Brush)FindResource("DangerBrush")));
        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Action action, Brush? foreground = null)
    {
        var item = new MenuItem { Header = header };
        if (foreground is not null) item.Foreground = foreground;
        item.Click += (_, _) => action();
        return item;
    }

    private void Treemap_ContextMenuRequested(object? sender, FileNode node)
    {
        _ctxNode = node;
        var menu = CreateFileMenu();
        menu.PlacementTarget = Treemap;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void DeleteContextNode()
    {
        if (_ctxNode is null) return;
        var node = _ctxNode;

        if (_scan is not null)
        {
            var rootPath = _scan.Root.FullPath.TrimEnd('\\');
            var nodePath = node.FullPath.TrimEnd('\\');
            if (nodePath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                rootPath.StartsWith(nodePath + "\\", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, Loc.T("L.Msg.RootProtected"),
                    "DiskMap", MessageBoxButton.OK, MessageBoxImage.Information);
                _ctxNode = null;
                return;
            }
        }

        var blockedRoot = FileActions.GetRecycleBlockReason(new[] { node.FullPath });
        if (blockedRoot is not null)
        {
            ShowRecycleUnavailable(blockedRoot);
            _ctxNode = null;
            return;
        }

        bool confirmed = ConfirmWindow.Show(this,
            Loc.T("L.Confirm.ItemMessage", node.FullPath),
            Loc.T("L.Confirm.Move"));
        if (!confirmed) return;

        var (ok, failed) = FileActions.DeleteToRecycleBin(new[] { node.FullPath });
        if (ok > 0)
        {
            RemoveFromModel(new[] { node.FullPath });
            _stale = true;
        }

        SetStatusOverride(failed > 0 ? "L.Item.MoveFailed" : "L.Item.Moved");
        _ctxNode = null;
    }

    private void ShowRecycleUnavailable(string root)
    {
        MessageBox.Show(this,
            Loc.T("L.Msg.RecycleUnavailable", root),
            Loc.T("L.Msg.RecycleUnavailable.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RemoveFromModel(IEnumerable<string> deletedPaths)
    {
        var paths = deletedPaths.Where(path => !string.IsNullOrEmpty(path)).ToList();
        if (paths.Count == 0) return;

        _allFiles = _allFiles
            .Where(file => !paths.Any(path =>
                file.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                file.FullPath.StartsWith(path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        ShowLargest(_typeFilter);
        BuildTypes();
        BuildSearchExtensions();
    }

    private void StatusStale()
    {
        _stale = true;
        if (_statusOverrideKey is null) RefreshStatusText();
    }

    private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<DataGridRow>(source) is null) return;

        if (sender is DataGrid grid && grid.SelectedItem is FileRow row)
        {
            FileActions.Open(row.Node.FullPath);
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string Shorten(string path)
    {
        try
        {
            var trimmed = path.TrimEnd('\\');
            var parent = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(parent)) return path;

            var grandParent = Directory.GetParent(trimmed);
            if (grandParent is null) return path;

            return Path.Combine(grandParent.Name, parent);
        }
        catch
        {
            return path;
        }
    }

    private void SetupLanguageSelector()
    {
        _suppressLanguageChange = true;
        LanguageCombo.ItemsSource = Loc.Languages;
        LanguageCombo.SelectedItem = Loc.Languages.FirstOrDefault(choice =>
            string.Equals(choice.Code, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase)) ?? Loc.Languages[0];
        _suppressLanguageChange = false;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;
        if (LanguageCombo.SelectedItem is not LanguageChoice choice) return;
        if (string.Equals(choice.Code, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase)) return;
        Loc.SetLanguage(choice.Code);
    }

    private void Loc_LanguageChanged(object? sender, EventArgs e)
    {
        _suppressLanguageChange = true;
        LanguageCombo.SelectedItem = Loc.Languages.FirstOrDefault(choice =>
            string.Equals(choice.Code, Loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase)) ?? Loc.Languages[0];
        _suppressLanguageChange = false;
        RefreshLocalizedText();
    }

    private void RefreshColumnHeaders()
    {
        LargestGrid.Columns[0].Header = Loc.T("L.Column.File");
        LargestGrid.Columns[1].Header = Loc.T("L.Column.Size");
        LargestGrid.Columns[2].Header = Loc.T("L.Column.Type");

        JunkGrid.Columns[1].Header = Loc.T("L.Column.Item");
        JunkGrid.Columns[2].Header = Loc.T("L.Column.Reason");
        JunkGrid.Columns[3].Header = Loc.T("L.Column.Size");

        SearchGrid.Columns[0].Header = Loc.T("L.Column.File");
        SearchGrid.Columns[1].Header = Loc.T("L.Column.Size");
    }

    private void RefreshLocalizedText()
    {
        LoadDrives();
        BuildSearchOptions();
        BuildSearchExtensions();
        RefreshColumnHeaders();
        BuildGridContextMenus();

        if (_scan is null)
        {
            TypesList.ItemsSource = null;
            TypesHeader.Text = Loc.T("L.Types.Waiting");
            ShowLargest(null);
        }
        else
        {
            BuildTypes();
            ShowLargest(_typeFilter);
        }

        foreach (var group in _dupGroups) group.RefreshLocalized();
        foreach (var item in _junkItems) item.RefreshLocalized();

        DupStatus.Text = Loc.T(_dupStatusKey, _dupStatusArgs);
        JunkStatus.Text = Loc.T(_junkStatusKey, _junkStatusArgs);
        SearchCount.Text = Loc.T(_searchStatusKey, _searchStatusArgs);
        UpdateDupSummary();
        UpdateJunkSummary();
        RefreshStatusText();

        if (_scanning) ScanOverlayText.Text = Loc.T("L.Status.Scanning");
        Treemap.InvalidateVisual();
    }

    private void SetStatusOverride(string key, params object?[] args)
    {
        _statusOverrideKey = key;
        _statusOverrideArgs = args;
        StatusText.Text = Loc.T(key, args);
    }

    private void ClearStatusOverride()
    {
        _statusOverrideKey = null;
        _statusOverrideArgs = Array.Empty<object?>();
    }

    private void RefreshStatusText()
    {
        if (_statusOverrideKey is not null)
        {
            StatusText.Text = Loc.T(_statusOverrideKey, _statusOverrideArgs);
            return;
        }

        if (_scan is null)
        {
            StatusText.Text = Loc.T("L.Status.Ready");
            return;
        }

        string text = Loc.T("L.Status.Summary",
            Fmt.Size(_scan.TotalBytes),
            Fmt.Count(_scan.TotalFiles),
            Fmt.Count(Math.Max(0, _scan.TotalDirectories - 1)),
            _scan.Elapsed.TotalSeconds);

        if (_scan.Skipped > 0)
        {
            text += Loc.T("L.Status.Skipped", Fmt.Count(_scan.Skipped));
        }

        if (_stale)
        {
            text += Loc.T("L.Status.Stale");
        }

        StatusText.Text = text;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        Loc.LanguageChanged -= Loc_LanguageChanged;
        _scanCts?.Cancel();
        _dupCts?.Cancel();
    }
}
