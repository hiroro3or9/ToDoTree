using System.Windows;
using System.Windows.Media;
using ToDoTree.Core.Layout;

namespace ToDoTree.App.Controls;

/// <summary>ビューポート座標で描き、拡大率によらず線幅を保つ操作補助。</summary>
public sealed class AlignmentGuideLayer : FrameworkElement
{
    public static readonly DependencyProperty GuideBrushProperty = DependencyProperty.Register(
        nameof(GuideBrush), typeof(Brush), typeof(AlignmentGuideLayer),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush GuideBrush { get => (Brush)GetValue(GuideBrushProperty); set => SetValue(GuideBrushProperty, value); }

    private IReadOnlyList<SnapGuide> _guides = [];
    private double _zoom = 1, _panX, _panY;

    public void Update(IReadOnlyList<SnapGuide> guides, double zoom, double panX, double panY)
    {
        _guides = guides;
        _zoom = zoom;
        _panX = panX;
        _panY = panY;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var pen = new Pen(GuideBrush, 1) { DashStyle = DashStyles.Dash };
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        foreach (var guide in _guides)
        {
            var coordinate = guide.Coordinate * _zoom + (guide.IsVertical ? _panX : _panY);
            var from = guide.From * _zoom + (guide.IsVertical ? _panY : _panX) - 8;
            var to = guide.To * _zoom + (guide.IsVertical ? _panY : _panX) + 8;
            dc.DrawLine(pen, guide.IsVertical ? new(coordinate, from) : new(from, coordinate),
                guide.IsVertical ? new(coordinate, to) : new(to, coordinate));
        }
        dc.Pop();
    }
}
