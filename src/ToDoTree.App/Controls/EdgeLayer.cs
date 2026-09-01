using System.Collections;
using System.Windows;
using System.Windows.Media;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Layout;

namespace ToDoTree.App.Controls;

/// <summary>
/// ステップ同士を結ぶ線をまとめて 1 枚に描く層。
/// 線ごとに要素を作らないので、ノードが増えても軽い。
/// 形の計算は <see cref="CurveGeometry"/>（Core 側・テスト済み）と共有している。
/// ペンはテーマから作り、配色が変わったら作り直す。
/// </summary>
public sealed class EdgeLayer : FrameworkElement
{
    public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(
        nameof(Edges),
        typeof(IEnumerable),
        typeof(EdgeLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private int _paletteGeneration = -1;

    private Pen _normalPen = null!;
    private Pen _settledPen = null!;
    private Pen _highlightPen = null!;
    private Pen _criticalPen = null!;
    private Pen _selectedPen = null!;
    private Pen _previewPen = null!;
    private Pen _marqueePen = null!;
    private Brush _marqueeFill = null!;
    private Brush _normalArrow = null!;
    private Brush _settledArrow = null!;
    private Brush _highlightArrow = null!;
    private Brush _criticalArrow = null!;
    private Brush _selectedArrow = null!;

    private Point? _previewFrom;
    private Point? _previewTo;
    private Rect? _marquee;

    public EdgeLayer()
    {
        IsHitTestVisible = false;
    }

    public IEnumerable? Edges
    {
        get => (IEnumerable?)GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    /// <summary>接続中のガイド線。null を渡すと消える。</summary>
    public void SetPreview(Point? from, Point? to)
    {
        _previewFrom = from;
        _previewTo = to;
        InvalidateVisual();
    }

    /// <summary>矩形選択の枠。null を渡すと消える。</summary>
    public void SetMarquee(Rect? rect)
    {
        _marquee = rect;
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        EnsurePalette();

        if (Edges is not null)
        {
            foreach (var item in Edges)
            {
                if (item is EdgeViewModel edge && edge.From.IsVisible && edge.To.IsVisible)
                {
                    DrawEdge(drawingContext, edge);
                }
            }
        }

        if (_previewFrom is { } previewFrom && _previewTo is { } previewTo)
        {
            var (control1, control2) = CurveGeometry.ControlPoints(ToVec(previewFrom), ToVec(previewTo));
            drawingContext.DrawGeometry(
                null,
                _previewPen,
                BuildCurve(previewFrom, ToPoint(control1), ToPoint(control2), previewTo));
        }

        if (_marquee is { } marquee)
        {
            drawingContext.DrawRoundedRectangle(_marqueeFill, _marqueePen, marquee, 4, 4);
        }
    }

    /// <summary>テーマが変わっていたらペンを作り直す。</summary>
    private void EnsurePalette()
    {
        if (_paletteGeneration == ThemeManager.Generation && _normalPen is not null)
        {
            return;
        }

        _paletteGeneration = ThemeManager.Generation;

        _normalPen = CreatePen("Edge.Normal", 1.8);
        _settledPen = CreatePen("Edge.Settled", 1.6);
        _highlightPen = CreatePen("Edge.Highlight", 2.6);
        _criticalPen = CreatePen("Edge.Critical", 3.2);
        _selectedPen = CreatePen("Edge.Selected", 4);
        _previewPen = CreateDashedPen("Edge.Preview");
        _marqueePen = CreatePen("Marquee.Stroke", 1);

        _marqueeFill = ThemeManager.BrushOf("Marquee.Fill");
        _normalArrow = ThemeManager.BrushOf("Edge.Normal");
        _settledArrow = ThemeManager.BrushOf("Edge.Settled");
        _highlightArrow = ThemeManager.BrushOf("Edge.Highlight");
        _criticalArrow = ThemeManager.BrushOf("Edge.Critical");
        _selectedArrow = ThemeManager.BrushOf("Edge.Selected");
    }

    private void DrawEdge(DrawingContext drawingContext, EdgeViewModel edge)
    {
        var (start, end) = CurveGeometry.Anchors(
            new Vec2(edge.From.X, edge.From.Y),
            new Vec2(edge.To.X, edge.To.Y),
            NodeViewModel.CardWidth,
            NodeViewModel.CardHeight);

        var (control1, control2) = CurveGeometry.ControlPoints(start, end);

        var pen = edge.IsSelected ? _selectedPen
            : edge.IsOnCriticalPath ? _criticalPen
            : edge.IsHighlighted ? _highlightPen
            : edge.IsSettled ? _settledPen : _normalPen;

        var arrow = edge.IsSelected ? _selectedArrow
            : edge.IsOnCriticalPath ? _criticalArrow
            : edge.IsHighlighted ? _highlightArrow
            : edge.IsSettled ? _settledArrow : _normalArrow;

        var tip = ToPoint(end);
        drawingContext.DrawGeometry(null, pen, BuildCurve(ToPoint(start), ToPoint(control1), ToPoint(control2), tip));
        DrawArrowHead(drawingContext, ToPoint(control2), tip, arrow);
    }

    private static StreamGeometry BuildCurve(Point start, Point control1, Point control2, Point end)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.BezierTo(control1, control2, end, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawArrowHead(DrawingContext drawingContext, Point from, Point tip, Brush brush)
    {
        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        const double size = 9;
        const double half = 4.2;

        var baseX = tip.X - (ux * size);
        var baseY = tip.Y - (uy * size);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(tip, isFilled: true, isClosed: true);
            context.LineTo(new Point(baseX - (uy * half), baseY + (ux * half)), false, false);
            context.LineTo(new Point(baseX + (uy * half), baseY - (ux * half)), false, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
    }

    private static Vec2 ToVec(Point point) => new(point.X, point.Y);

    private static Point ToPoint(Vec2 vector) => new(vector.X, vector.Y);

    private static Pen CreatePen(string key, double thickness)
    {
        var pen = new Pen(ThemeManager.BrushOf(key), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        pen.Freeze();
        return pen;
    }

    private static Pen CreateDashedPen(string key)
    {
        var pen = new Pen(ThemeManager.BrushOf(key), 2)
        {
            DashStyle = new DashStyle([4, 3], 0),
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        pen.Freeze();
        return pen;
    }
}
