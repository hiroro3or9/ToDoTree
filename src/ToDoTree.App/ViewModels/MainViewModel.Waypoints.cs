using System.Windows.Input;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    private ICommand? _toggleWaypointSmoothCommand;
    public bool IsSelectedWaypointSmooth => SelectedEdge is { } edge
        && SelectedWaypointIndex >= 0 && SelectedWaypointIndex < edge.Model.Waypoints.Count
        && edge.Model.Waypoints[SelectedWaypointIndex].IsSmooth;

    public ICommand ToggleWaypointSmoothCommand => _toggleWaypointSmoothCommand ??= new RelayCommand(() =>
    {
        if (SelectedEdge is not { } edge || SelectedWaypointIndex < 0 || SelectedWaypointIndex >= edge.Model.Waypoints.Count) return;
        PushUndo();
        var point = edge.Model.Waypoints[SelectedWaypointIndex];
        edge.Model.Waypoints[SelectedWaypointIndex] = point with { IsSmooth = !point.IsSmooth };
        MarkDirty();
        OnPropertyChanged(nameof(IsSelectedWaypointSmooth));
        NotifyVisualsChanged();
        StatusMessage = point.IsSmooth ? "この通過点に角を付けました。" : "この通過点を滑らかにしました。";
    }, () => SelectedEdge is { } edge && SelectedWaypointIndex >= 0 && SelectedWaypointIndex < edge.Model.Waypoints.Count);

    public int SelectedWaypointIndex { get; private set; } = -1;
    private ICommand? _addWaypointCommand, _removeWaypointCommand, _clearWaypointsCommand;
    public ICommand AddWaypointCommand => _addWaypointCommand ??= new RelayCommand(
        () => AddWaypoint(new Vec2(_menuX, _menuY)), () => SelectedEdge is { IsAggregated: false });
    public ICommand RemoveWaypointCommand => _removeWaypointCommand ??= new RelayCommand(
        RemoveWaypoint, () => SelectedEdge is { } edge && SelectedWaypointIndex >= 0 && SelectedWaypointIndex < edge.Model.Waypoints.Count);
    public ICommand ClearWaypointsCommand => _clearWaypointsCommand ??= new RelayCommand(() =>
    {
        if (SelectedEdge is not { } edge) return;
        PushUndo();
        edge.Model.Waypoints.Clear();
        edge.SelectedWaypointIndex = -1;
        SelectedWaypointIndex = -1;
        MarkDirty();
        NotifyVisualsChanged();
    }, () => SelectedEdge?.Model.Waypoints.Count > 0);

    public (EdgeViewModel Edge, int Index)? FindWaypointAt(Vec2 point, double tolerance)
    {
        foreach (var edge in Edges.OrderByDescending(e => e.IsSelected))
        {
            if (!edge.IsVisible || edge.IsAggregated) continue;
            for (var i = 0; i < edge.Model.Waypoints.Count; i++)
                if ((edge.Model.Waypoints[i].ToVector() - point).Length <= tolerance) return (edge, i);
        }
        return null;
    }

    public void SelectWaypoint(EdgeViewModel edge, int index)
    {
        SelectEdge(edge);
        SelectedWaypointIndex = index;
        edge.SelectedWaypointIndex = index;
        OnPropertyChanged(nameof(IsSelectedWaypointSmooth));
        StatusMessage = "通過点を選びました。ドラッグで移動、Delete でこの点だけ削除できます。";
        NotifyVisualsChanged();
    }

    public void AddWaypoint(Vec2 point)
    {
        if (SelectedEdge is not { IsAggregated: false } edge) return;
        if (edge.Model.Waypoints.Any(p => (p.ToVector() - point).Length < 1)) return;
        var route = edge.GetRoute(Nodes);
        // クリックに最も近い線分を探し、既存通過点の順序を保って挿入する。
        var segment = Enumerable.Range(1, route.Count - 1)
            .MinBy(i => CurveGeometry.DistanceToSegment(point, route[i - 1], route[i]));
        var index = 0;
        for (var i = 0; i < segment && index < edge.Model.Waypoints.Count; i++)
            if ((route[i] - edge.Model.Waypoints[index].ToVector()).Length < 0.01) index++;
        PushUndo();
        edge.Model.Waypoints.Insert(index, new JunctionPoint(point.X, point.Y, IsSmooth: true));
        SelectWaypoint(edge, index);
        MarkDirty();
    }

    public void MoveWaypoint(Vec2 point)
    {
        if (SelectedEdge is not { } edge || SelectedWaypointIndex < 0 || SelectedWaypointIndex >= edge.Model.Waypoints.Count) return;
        edge.Model.Waypoints[SelectedWaypointIndex] = edge.Model.Waypoints[SelectedWaypointIndex] with { X = point.X, Y = point.Y };
        MarkDirty();
        NotifyVisualsChanged();
    }

    public void RemoveWaypoint()
    {
        if (SelectedEdge is not { } edge || SelectedWaypointIndex < 0 || SelectedWaypointIndex >= edge.Model.Waypoints.Count) return;
        PushUndo();
        edge.Model.Waypoints.RemoveAt(SelectedWaypointIndex);
        edge.SelectedWaypointIndex = -1;
        SelectedWaypointIndex = -1;
        MarkDirty();
        NotifyVisualsChanged();
        StatusMessage = "通過点を削除しました。Ctrl+Z で戻せます。";
    }
}
