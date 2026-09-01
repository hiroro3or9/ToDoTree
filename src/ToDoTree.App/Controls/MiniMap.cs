using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App.Controls;

/// <summary>全体のどこを見ているかを示す小さな地図。クリックでそこへ飛ぶ。</summary>
public sealed class MiniMap : FrameworkElement
{
    private const double Padding = 6;

    private static readonly Brush Surface = FrozenBrush("#FFFFFFFF");
    private static readonly Pen ViewportPen = CreatePen("#FF3B82F6", 1.4);
    private static readonly Brush ViewportFill = FrozenBrush("#143B82F6");

    private IReadOnlyList<NodeViewModel> _nodes = [];
    private Rect _viewport;
    private Rect _bounds = new(0, 0, 1, 1);
    private double _scale = 1;

    /// <summary>地図の上でクリックされた場所（キャンバス上の座標）。</summary>
    public event EventHandler<Point>? Navigate;

    public void Update(IReadOnlyList<NodeViewModel> nodes, Rect viewport)
    {
        _nodes = nodes;
        _viewport = viewport;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4)
        {
            return;
        }

        // クリックを受け取るために、まず全面を塗る。
        drawingContext.DrawRectangle(Surface, null, new Rect(0, 0, width, height));

        _bounds = ComputeBounds();
        _scale = Math.Min(
            (width - (Padding * 2)) / Math.Max(1, _bounds.Width),
            (height - (Padding * 2)) / Math.Max(1, _bounds.Height));

        foreach (var node in _nodes)
        {
            var rect = ToMap(new Rect(node.X, node.Y, NodeViewModel.CardWidth, NodeViewModel.CardHeight));
            drawingContext.DrawRectangle(
                node.StatusBrush,
                null,
                new Rect(rect.X, rect.Y, Math.Max(2, rect.Width), Math.Max(2, rect.Height)));
        }

        var view = ToMap(_viewport);
        drawingContext.DrawRectangle(ViewportFill, ViewportPen, view);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_scale <= 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        Navigate?.Invoke(this, new Point(
            ((point.X - Padding) / _scale) + _bounds.X,
            ((point.Y - Padding) / _scale) + _bounds.Y));

        e.Handled = true;
    }

    private Rect ComputeBounds()
    {
        if (_nodes.Count == 0)
        {
            return _viewport.Width > 0 ? _viewport : new Rect(0, 0, 1, 1);
        }

        var minX = _nodes.Min(n => n.X);
        var minY = _nodes.Min(n => n.Y);
        var maxX = _nodes.Max(n => n.X) + NodeViewModel.CardWidth;
        var maxY = _nodes.Max(n => n.Y) + NodeViewModel.CardHeight;

        var bounds = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));

        if (_viewport.Width > 0 && _viewport.Height > 0)
        {
            bounds.Union(_viewport);
        }

        return bounds;
    }

    private Rect ToMap(Rect world) => new(
        ((world.X - _bounds.X) * _scale) + Padding,
        ((world.Y - _bounds.Y) * _scale) + Padding,
        Math.Max(1, world.Width * _scale),
        Math.Max(1, world.Height * _scale));

    private static Pen CreatePen(string hex, double thickness)
    {
        var pen = new Pen(FrozenBrush(hex), thickness);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush FrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
