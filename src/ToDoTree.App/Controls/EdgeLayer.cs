using System.Collections;
using System.Windows;
using System.Windows.Media;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Controls;

/// <summary>
/// ステップ同士を結ぶ線をまとめて 1 枚に描く層。
/// 線ごとに要素を作らないので、ノードが増えても軽い。
/// 形の計算は <see cref="CurveGeometry"/>（Core 側・テスト済み）と共有している。
/// ペンはテーマから作り、配色か表示モードが変わったら作り直す。
/// </summary>
public sealed partial class EdgeLayer : FrameworkElement
{
    public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(
        nameof(Edges),
        typeof(IEnumerable),
        typeof(EdgeLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NodesProperty = DependencyProperty.Register(
        nameof(Nodes), typeof(IEnumerable<NodeViewModel>), typeof(EdgeLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<NodeViewModel>? Nodes
    {
        get => (IEnumerable<NodeViewModel>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    private Geometry? _previewLoopGeometry;

    private int _paletteGeneration = -1;
    private int _styleGeneration = -1;

    private double _arrowSize = 9;
    private double _arrowHalf = 4.2;
    private double _normalThickness = 1.8;
    private double _settledThickness = 1.6;

    /// <summary>個別色の線。色ごとに 1 度だけ作り、配色か表示モードが変わったら丸ごと捨てる。</summary>
    private readonly Dictionary<string, (Pen Pen, Pen Settled, Brush Arrow, Brush SettledArrow)> _colored = [];

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

    /// <summary>先行が片付いた線を薄くする割合。</summary>
    private const double SettledOpacity = 0.45;

    private (Vec2 Control1, Vec2 Control2)? _previewControls;
    private IReadOnlyList<Vec2>? _previewRoute;
    private ConnectionSide _previewSide;
    private Point? _previewFrom;
    private Point? _previewTo;
    private Rect? _marquee;

    public EdgeLayer()
    {
        IsHitTestVisible = false;
        Unloaded += (_, _) => ClearCompletionEffects();
    }

    public IEnumerable? Edges
    {
        get => (IEnumerable?)GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    /// <summary>接続中のガイド線。null を渡すと消える。</summary>
    public void SetPreview(Point? from, Point? to, ConnectionSide side = ConnectionSide.Auto)
    {
        _previewLoopGeometry = null;
        _previewRoute = null;
        _previewControls = null;
        _previewSide = side;
        _previewFrom = from;
        _previewTo = to;
        InvalidateVisual();
    }

    public void SetPreviewLoop(Point nodeOrigin)
    {
        SetPreview(null, null);
        var loop = new GeometryGroup();
        loop.Children.Add(RepeatLoopVisuals.Path);
        loop.Children.Add(RepeatLoopVisuals.Arrow);
        loop.Transform = new TranslateTransform(
            nodeOrigin.X + (NodeMetrics.Width - RepeatLoopVisuals.Width) / 2,
            nodeOrigin.Y - RepeatLoopVisuals.Height);
        loop.Freeze();
        _previewLoopGeometry = loop;
        InvalidateVisual();
    }

    public void SetPreviewCurve((Vec2 Start, Vec2 End, Vec2 Control1, Vec2 Control2) curve)
    {
        _previewLoopGeometry = null;
        _previewRoute = null;
        _previewFrom = ToPoint(curve.Start);
        _previewTo = ToPoint(curve.End);
        _previewControls = (curve.Control1, curve.Control2);
        InvalidateVisual();
    }

    public void SetPreviewRoute(IReadOnlyList<Vec2> route)
    {
        _previewLoopGeometry = null;
        _previewRoute = route;
        _previewFrom = ToPoint(route[0]);
        _previewTo = ToPoint(route[^1]);
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
                if (item is EdgeViewModel edge && edge.IsVisible)
                {
                    DrawEdge(drawingContext, edge);
                }
            }
        }

        DrawCompletionEffects(drawingContext);
        if (_previewLoopGeometry is not null)
            drawingContext.DrawGeometry(null, _previewPen, _previewLoopGeometry);

        if (_previewFrom is { } previewFrom && _previewTo is { } previewTo)
        {
            var (control1, control2) = CurveGeometry.ControlPoints(ToVec(previewFrom), ToVec(previewTo));
            control1 = CurveGeometry.SourceControlPoint(ToVec(previewFrom), ToVec(previewTo), control1, _previewSide);
            if (_previewControls is { } controls) (control1, control2) = controls;
            drawingContext.DrawGeometry(
                null,
                _previewPen,
                _previewRoute is { } route ? BuildRoute(route) : BuildCurve(previewFrom, ToPoint(control1), ToPoint(control2), previewTo));
        }

        if (_marquee is { } marquee)
        {
            drawingContext.DrawRoundedRectangle(_marqueeFill, _marqueePen, marquee, 4, 4);
        }
    }

    /// <summary>テーマか表示モードが変わっていたら、ペンを作り直す。</summary>
    private void EnsurePalette()
    {
        if (_paletteGeneration == ThemeManager.Generation
            && _styleGeneration == NodeMetrics.Generation
            && _normalPen is not null)
        {
            return;
        }

        _paletteGeneration = ThemeManager.Generation;
        _styleGeneration = NodeMetrics.Generation;

        // ミニマル表示では箱が小さいので、同じ太さだと線ばかりが目立つ。
        var minimal = NodeMetrics.IsMinimal;
        _arrowSize = minimal ? 6.5 : 9;
        _arrowHalf = minimal ? 3.2 : 4.2;
        _normalThickness = minimal ? 1.2 : 1.8;
        _settledThickness = minimal ? 1.1 : 1.6;
        _colored.Clear();

        _normalPen = CreatePen("Edge.Normal", _normalThickness);
        _settledPen = CreatePen("Edge.Settled", _settledThickness);
        _highlightPen = CreatePen("Edge.Highlight", minimal ? 1.8 : 2.6);
        _criticalPen = CreatePen("Edge.Critical", minimal ? 2.2 : 3.2);
        _selectedPen = CreatePen("Edge.Selected", minimal ? 2.8 : 4);
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
        var route = edge.GetRoute(Nodes ?? []);

        // 個別色は「普段の見え方」だけを変える。選択・最長経路・強調は状態色が勝つ。
        var (basePen, baseArrow) = BaseOf(edge);

        var pen = edge.IsSelected ? _selectedPen
            : edge.IsOnCriticalPath ? _criticalPen
            : edge.IsHighlighted ? _highlightPen
            : basePen;

        var arrow = edge.IsSelected ? _selectedArrow
            : edge.IsOnCriticalPath ? _criticalArrow
            : edge.IsHighlighted ? _highlightArrow
            : baseArrow;

        if (edge.IsAggregated)
        {
            pen = pen.Clone();
            pen.DashStyle = DashStyles.Dash;
            pen.Freeze();
        }
        var tip = ToPoint(route[^1]);
        drawingContext.DrawGeometry(null, pen, BuildRoute(route));
        DrawArrowHead(drawingContext, ToPoint(route[^2]), tip, arrow, _arrowSize, _arrowHalf);
        for (var i = 0; !edge.IsAggregated && i < edge.Model.Waypoints.Count; i++)
        {
            var selected = edge.IsSelected && edge.SelectedWaypointIndex == i;
            drawingContext.DrawEllipse(ThemeManager.BrushOf(selected ? "Brush.Accent" : "Brush.Surface"),
                edge.IsSelected ? _selectedPen : basePen, ToPoint(edge.Model.Waypoints[i].ToVector()),
                selected ? 6 : 5, selected ? 6 : 5);
        }
    }

    /// <summary>状態色が付いていないときの見え方。個別色があればそれを使う。</summary>
    private (Pen Pen, Brush Arrow) BaseOf(EdgeViewModel edge)
    {
        if (ColorPalette.Effective(edge.Model.ColorId) is not { } id)
        {
            return edge.IsSettled ? (_settledPen, _settledArrow) : (_normalPen, _normalArrow);
        }

        if (!_colored.TryGetValue(id, out var set))
        {
            var arrow = ColorPalette.BrushOf("Edge.Normal", id);

            // 先行が片付いた線は、色を保ったまま薄くする（既定色の Edge.Settled にあたる見え方）。
            var settledArrow = ColorPalette.Fade("Edge.Normal", id, SettledOpacity);
            set = (CreatePen(arrow, _normalThickness), CreatePen(settledArrow, _settledThickness), arrow, settledArrow);
            _colored[id] = set;
        }

        return edge.IsSettled ? (set.Settled, set.SettledArrow) : (set.Pen, set.Arrow);
    }

    private static StreamGeometry BuildRoute(IReadOnlyList<Vec2> route)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(route[0]), false, false);
            context.PolyLineTo([.. route.Skip(1).Select(ToPoint)], true, false);
        }
        geometry.Freeze();
        return geometry;
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

    private static void DrawArrowHead(
        DrawingContext drawingContext,
        Point from,
        Point tip,
        Brush brush,
        double size,
        double half)
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

    private static Pen CreatePen(string key, double thickness) =>
        CreatePen(ThemeManager.BrushOf(key), thickness);

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
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
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        pen.Freeze();
        return pen;
    }
}
