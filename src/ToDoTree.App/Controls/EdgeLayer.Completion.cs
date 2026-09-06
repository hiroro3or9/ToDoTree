using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Controls;

public sealed partial class EdgeLayer
{
    private CompletionImpact? _hoverImpact;
    private readonly List<(CompletionImpact Impact, long Start)> _completionPulses = [];
    private DispatcherTimer? _effectTimer;

    public void SetCompletionPreview(CompletionImpact? impact)
    {
        _hoverImpact = impact;
        InvalidateVisual();
    }

    public void PlayCompletion(CompletionImpact impact)
    {
        if (!IsLoaded) return;
        _hoverImpact = null;
        _completionPulses.Add((impact, Stopwatch.GetTimestamp()));
        if (_completionPulses.Count > 12) _completionPulses.RemoveAt(0);
        if (_effectTimer is null)
        {
            _effectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _effectTimer.Tick += (_, _) =>
            {
                _completionPulses.RemoveAll(p => Stopwatch.GetElapsedTime(p.Start).TotalSeconds >= 1.5);
                if (_completionPulses.Count == 0) _effectTimer.Stop();
                InvalidateVisual();
            };
        }
        _effectTimer.Start();
        InvalidateVisual();
    }

    public void ClearCompletionEffects()
    {
        _hoverImpact = null;
        _completionPulses.Clear();
        _effectTimer?.Stop();
        InvalidateVisual();
    }

    private void DrawCompletionEffects(DrawingContext dc)
    {
        var nodes = Nodes?.ToArray() ?? [];
        var edges = Edges?.OfType<EdgeViewModel>().ToArray() ?? [];
        var brush = ThemeManager.BrushOf("Brush.Accent");
        var pen = new Pen(brush, NodeMetrics.IsMinimal ? 2 : 3);
        if (_hoverImpact is { } hover)
        {
            dc.PushOpacity(0.8);
            foreach (var edge in edges.Where(e => e.From.IsVisible && e.To.IsVisible
                && hover.Sources.Contains(e.From.Id) && hover.Unlocked.Contains(e.To.Id)))
                dc.DrawGeometry(null, pen, BuildRoute(edge.GetRoute(nodes)));
            foreach (var node in nodes.Where(n => n.IsVisible && hover.Unlocked.Contains(n.Id)))
                DrawRing(dc, node, pen, 4);
            dc.Pop();
        }

        foreach (var (impact, start) in _completionPulses)
        {
            // Undo、削除、プロジェクト読み込み後の古い演出は描かない。
            if (!impact.Sources.All(id => nodes.Any(n => n.Id == id && n.Status == NodeStatus.Done))) continue;
            var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            var moving = SystemParameters.ClientAreaAnimation && seconds < 0.7;
            if (moving)
            {
                foreach (var edge in edges.Where(e => e.From.IsVisible && e.To.IsVisible
                    && impact.Sources.Contains(e.From.Id) && impact.Unlocked.Contains(e.To.Id)))
                {
                    var point = PointAlong(edge.GetRoute(nodes), seconds / 0.7);
                    dc.PushOpacity(0.2);
                    dc.DrawEllipse(brush, null, point, 10, 10);
                    dc.Pop();
                    dc.DrawEllipse(brush, null, point, 4, 4);
                }
            }
            var glow = SystemParameters.ClientAreaAnimation ? Math.Clamp((seconds - 0.55) / 0.2, 0, 1) : 1;
            glow *= Math.Clamp((1.5 - seconds) / 0.5, 0, 1);
            dc.PushOpacity(glow);
            foreach (var node in nodes.Where(n => n.IsVisible && impact.Unlocked.Contains(n.Id)
                && n.Status == NodeStatus.NotStarted))
                DrawRing(dc, node, pen, 5);
            dc.Pop();
        }
    }

    private static void DrawRing(DrawingContext dc, NodeViewModel node, Pen pen, double padding)
    {
        dc.DrawRoundedRectangle(null, pen,
            new Rect(node.X - padding, node.Y - padding,
                NodeViewModel.CardWidth + padding * 2, NodeViewModel.CardHeight + padding * 2),
            NodeMetrics.IsMinimal ? 16 : 18, NodeMetrics.IsMinimal ? 16 : 18);
    }

    private static Point PointAlong(IReadOnlyList<Vec2> route, double fraction)
    {
        var total = 0d;
        for (var i = 1; i < route.Count; i++) total += (ToPoint(route[i]) - ToPoint(route[i - 1])).Length;
        var remaining = total * Math.Clamp(fraction, 0, 1);
        for (var i = 1; i < route.Count; i++)
        {
            var from = ToPoint(route[i - 1]);
            var delta = ToPoint(route[i]) - from;
            if (remaining <= delta.Length && delta.Length > 0) return from + delta * (remaining / delta.Length);
            remaining -= delta.Length;
        }
        return ToPoint(route[^1]);
    }
}
