using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ToDoTree.Core.Models;
using ToDoTree.Core.Graph;

namespace ToDoTree.App.Controls;

/// <summary>保存済みの標本だけを描く。元のグラフやレイアウトを変更しない。</summary>
public sealed class SpecimenGraph : FrameworkElement
{
    public static readonly DependencyProperty SpecimenProperty = DependencyProperty.Register(nameof(Specimen),
        typeof(AchievementSpecimen), typeof(SpecimenGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public AchievementSpecimen? Specimen
    {
        get => (AchievementSpecimen?)GetValue(SpecimenProperty);
        set => SetValue(SpecimenProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Specimen is not { Nodes.Count: > 0 } specimen) return;
        Brush Brush(string key) => (Brush)FindResource(key);
        const double width = 150, height = 48;
        var layout = new TodoGraph(new TodoProject { Nodes = [.. specimen.Nodes.Select(n => n.Clone())],
            Edges = [.. specimen.Edges.Select(e => e.Clone())] });
        var ranks = new Dictionary<Guid, int>();
        foreach (var node in layout.TopologicalOrder() ?? layout.Nodes)
            ranks[node.Id] = layout.ParentsOf(node.Id).Select(p => ranks.GetValueOrDefault(p.Id) + 1).DefaultIfEmpty(0).Max();
        foreach (var group in layout.Nodes.GroupBy(n => ranks[n.Id]))
        {
            var row = 0;
            foreach (var node in group.OrderBy(n => n.Y).ThenBy(n => n.X))
            { node.X = group.Key * (width + 28); node.Y = row++ * (height + 24); }
        }
        var minX = 0d; var minY = 0d;
        var graphWidth = layout.Nodes.Max(n => n.X) + width;
        var graphHeight = layout.Nodes.Max(n => n.Y) + height;
        var scale = Math.Min(Math.Max(1, ActualWidth - 24) / graphWidth, Math.Max(1, ActualHeight - 24) / graphHeight);
        scale = Math.Min(scale, 1.5);
        var x = (ActualWidth - graphWidth * scale) / 2; var y = (ActualHeight - graphHeight * scale) / 2;
        dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new ScaleTransform(scale, scale));
        var nodes = layout.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in specimen.Edges)
        {
            if (!nodes.TryGetValue(edge.FromId, out var from) || !nodes.TryGetValue(edge.ToId, out var to)) continue;
            var skipped = specimen.SkippedNodeIds.Contains(from.Id) || specimen.SkippedNodeIds.Contains(to.Id)
                || (from.IsChoice && from.SelectedChoiceEdgeId != edge.Id);
            var pen = new Pen(Brush(skipped ? "Brush.TextFaint" : "Brush.Accent"), 2);
            if (skipped) pen.DashStyle = DashStyles.Dash;
            var a = new Point(from.X - minX + width / 2, from.Y - minY + height / 2);
            var b = new Point(to.X - minX + width / 2, to.Y - minY + height / 2);
            dc.DrawLine(pen, a, b);
        }
        foreach (var node in layout.Nodes)
        {
            var skipped = specimen.SkippedNodeIds.Contains(node.Id);
            dc.PushOpacity(skipped ? 0.48 : 1);
            var bounds = new Rect(node.X - minX, node.Y - minY, width, height);
            dc.DrawRoundedRectangle(Brush(node.Id == specimen.GoalId ? "Brush.AccentSoft" : "Brush.Surface"),
                new Pen(Brush(node.Id == specimen.GoalId ? "Brush.Accent" : "Brush.BorderStrong"), node.Id == specimen.GoalId ? 2.5 : 1), bounds, 8, 8);
            var label = new FormattedText(node.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Yu Gothic UI"), 13, Brush("Brush.Text"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = width - 16, MaxTextHeight = height - 10, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(label, new Point(bounds.X + 8, bounds.Y + 7)); dc.Pop();
        }
        dc.Pop(); dc.Pop();
    }
}
