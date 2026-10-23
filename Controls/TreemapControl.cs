using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskMap.Models;
using DiskMap.Util;

namespace DiskMap.Controls;

public enum TreemapColorMode
{
    Type,
    Age,
    Size,
}

public sealed class TreemapControl : FrameworkElement
{
    private sealed record Tile(Rect Rect, FileNode Node);
    private sealed record DrawTile(Rect Rect, FileNode Node, int Depth);

    private const int MaxDrawTiles = 3500;

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x12)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)));
    private static readonly Brush SubLabelBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xF2, 0xF6, 0xFF)));
    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x00, 0x00, 0x00)));
    private static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush PlaceholderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5D, 0x6A, 0x80)));
    private static readonly Pen SelectedPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF))), 2));
    private static readonly Pen HoverPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF))), 1.4));
    private static readonly Pen TileBorderPen = Freeze(new Pen(BackgroundBrush, 1));

    private readonly List<Tile> _tiles = new();
    private readonly List<DrawTile> _drawTiles = new();

    public TreemapControl()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetShowDuration(this, 30000);
    }

    public FileNode? Root { get; private set; }
    public FileNode? SelectedNode { get; private set; }
    public FileNode? HoveredNode { get; private set; }
    public TreemapColorMode ColorMode { get; private set; } = TreemapColorMode.Type;

    public event EventHandler? SelectionChanged;
    public event EventHandler<FileNode>? NodeActivated;
    public event EventHandler<FileNode>? ContextMenuRequested;
    public event EventHandler<FileNode?>? HoverChanged;

    public void SetRoot(FileNode? root)
    {
        Root = root;
        SelectedNode = null;
        HoveredNode = null;
        ToolTip = null;
        Rebuild();
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetColorMode(TreemapColorMode mode)
    {
        if (ColorMode == mode) return;
        ColorMode = mode;
        InvalidateVisual();
    }

    public void Select(FileNode? node)
    {
        if (ReferenceEquals(SelectedNode, node)) return;
        SelectedNode = node;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        _tiles.Clear();
        _drawTiles.Clear();
        if (Root is null || ActualWidth < 4 || ActualHeight < 4) return;

        var items = Root.Children
            .Where(child => child.Size > 0)
            .OrderByDescending(child => child.Size)
            .ToList();
        if (items.Count == 0) return;

        var values = items.Select(item => (double)item.Size).ToArray();
        var rects = TreemapLayout.Squarify(values, new Rect(0, 0, ActualWidth, ActualHeight));

        for (int i = 0; i < items.Count; i++)
        {
            if (rects[i].Width < 1 || rects[i].Height < 1) continue;
            _tiles.Add(new Tile(rects[i], items[i]));
            AddDrawTiles(items[i], rects[i], 0);
        }
    }

    private void AddDrawTiles(FileNode node, Rect rect, int depth)
    {
        if (_drawTiles.Count >= MaxDrawTiles) return;
        _drawTiles.Add(new DrawTile(rect, node, depth));

        if (!node.IsDirectory || depth >= 2) return;

        int limit = depth == 0 ? 60 : 28;
        var children = node.Children
            .Where(child => child.Size > 0)
            .OrderByDescending(child => child.Size)
            .Take(limit)
            .ToList();
        if (children.Count < 2) return;

        double gap = depth == 0 ? 3.0 : 2.0;
        var inner = new Rect(rect.X + gap, rect.Y + gap,
            Math.Max(0, rect.Width - gap * 2), Math.Max(0, rect.Height - gap * 2));
        if (inner.Width < 14 || inner.Height < 14) return;

        var values = children.Select(child => (double)child.Size).ToArray();
        var rects = TreemapLayout.Squarify(values, inner);

        for (int i = 0; i < children.Count; i++)
        {
            if (_drawTiles.Count >= MaxDrawTiles) return;
            if (rects[i].Width < 2 || rects[i].Height < 2) continue;
            AddDrawTiles(children[i], rects[i], depth + 1);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (Root is null)
        {
            DrawCentered(drawingContext, Loc.T("L.Treemap.Prompt"));
            return;
        }

        if (_tiles.Count == 0)
        {
            DrawCentered(drawingContext, Loc.T("L.Treemap.Empty"));
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var drawTile in _drawTiles)
        {
            var rect = Inset(drawTile.Rect);
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            Brush brush = ColorMode switch
            {
                TreemapColorMode.Age => TreemapPalette.BrushForAge(drawTile.Node.LastWriteTime),
                TreemapColorMode.Size => TreemapPalette.BrushForSize(drawTile.Node.Size, Root.Size),
                _ => ExtensionPalette.BrushFor(drawTile.Node.IsDirectory
                    ? drawTile.Node.DominantExtension()
                    : drawTile.Node.Extension),
            };
            drawingContext.DrawRectangle(brush, TileBorderPen, rect);
        }

        foreach (var tile in _tiles)
        {
            var rect = Inset(tile.Rect);
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            if (ReferenceEquals(tile.Node, HoveredNode))
            {
                drawingContext.DrawRectangle(HoverBrush, null, rect);
                drawingContext.DrawRectangle(null, HoverPen, rect);
            }

            if (ReferenceEquals(tile.Node, SelectedNode))
            {
                drawingContext.DrawRectangle(null, SelectedPen, rect);
            }

            if (rect.Width >= 56 && rect.Height >= 18)
            {
                DrawShadowed(drawingContext, tile.Node.Name, 12, LabelBrush, pixelsPerDip, rect.Width - 12,
                    new Point(rect.X + 6, rect.Y + 4));

                if (rect.Height >= 42)
                {
                    var sizeText = MakeText(Fmt.Size(tile.Node.Size), 10.5, SubLabelBrush, pixelsPerDip, rect.Width - 12);
                    drawingContext.DrawText(sizeText, new Point(rect.X + 6, rect.Y + 22));
                }
            }
        }
    }

    private void DrawCentered(DrawingContext drawingContext, string text)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = MakeText(text, 13, PlaceholderBrush, pixelsPerDip, Math.Max(10, ActualWidth - 40));
        formatted.TextAlignment = TextAlignment.Center;
        drawingContext.DrawText(formatted, new Point(0, Math.Max(0, (ActualHeight - formatted.Height) / 2)));
    }

    private static void DrawShadowed(
        DrawingContext drawingContext,
        string text,
        double size,
        Brush brush,
        double pixelsPerDip,
        double maxWidth,
        Point origin)
    {
        var shadow = MakeText(text, size, ShadowBrush, pixelsPerDip, maxWidth);
        drawingContext.DrawText(shadow, new Point(origin.X + 1, origin.Y + 1));
        drawingContext.DrawText(MakeText(text, size, brush, pixelsPerDip, maxWidth), origin);
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double pixelsPerDip, double maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip)
        {
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1,
            MaxTextWidth = Math.Max(1, maxWidth),
        };
        return formatted;
    }

    private static Rect Inset(Rect rect) => new(
        rect.X + 0.75,
        rect.Y + 0.75,
        Math.Max(0, rect.Width - 1.5),
        Math.Max(0, rect.Height - 1.5));

    private FileNode? HitTest(Point point)
    {
        foreach (var tile in _tiles)
        {
            if (tile.Rect.Contains(point)) return tile.Node;
        }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var node = HitTest(point);

        if (!ReferenceEquals(node, HoveredNode))
        {
            HoveredNode = node;
            Cursor = node is null ? Cursors.Arrow : Cursors.Hand;
            UpdateToolTip(node);
            HoverChanged?.Invoke(this, node);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (HoveredNode is not null)
        {
            HoveredNode = null;
            ToolTip = null;
            HoverChanged?.Invoke(this, null);
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var node = HitTest(e.GetPosition(this));
        Focus();

        if (e.ClickCount == 2 && node is not null)
        {
            NodeActivated?.Invoke(this, node);
            e.Handled = true;
            return;
        }

        Select(node);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var node = HitTest(e.GetPosition(this));
        if (node is null) return;

        Select(node);
        ContextMenuRequested?.Invoke(this, node);
        e.Handled = true;
    }

    private void UpdateToolTip(FileNode? node)
    {
        if (node is null || Root is null)
        {
            ToolTip = null;
            return;
        }

        double share = Root.Size > 0 ? node.Size * 100.0 / Root.Size : 0;
        ToolTip = $"{node.Name}\n{Fmt.Size(node.Size)}  ·  %{share:0.#}\n{node.FullPath}";
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
