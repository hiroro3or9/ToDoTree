using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>2 枚のカードを結ぶ線。</summary>
public sealed class EdgeViewModel(TodoEdge model, NodeViewModel from, NodeViewModel to)
{
    private Vec2[]? _routeInputs;
    private JunctionPoint[]? _waypointInputs;
    private (double Width, double Height, ConnectionSide From, ConnectionSide To) _routeStyle;
    private IReadOnlyList<Vec2>? _route;

    public IReadOnlyList<Vec2> GetRoute(IEnumerable<NodeViewModel> nodes)
    {
        var from = new Vec2(From.X, From.Y);
        var to = new Vec2(To.X, To.Y);
        var obstacles = nodes.Where(n => n.IsVisible && n.Id != From.Id && n.Id != To.Id)
            .Select(n => new Vec2(n.X, n.Y)).ToArray();
        Vec2[] inputs = [from, to, .. obstacles];
        var waypoints = Model.Waypoints.ToArray();
        var style = (NodeViewModel.CardWidth, NodeViewModel.CardHeight, Model.FromSide, Model.ToSide);
        if (_route is null || _routeInputs is null || !_routeInputs.SequenceEqual(inputs) || _routeStyle != style || _waypointInputs is null || !_waypointInputs.SequenceEqual(waypoints))
        {
            _route = EdgeRouting.Route(from, to, style.Item1, style.Item2, obstacles, Model.FromSide, Model.ToSide,
                waypoints.Select(p => p.ToVector()).ToArray(), waypoints.Select(p => p.IsSmooth).ToArray());
            _waypointInputs = waypoints;
            _routeInputs = inputs;
            _routeStyle = style;
        }
        return _route;
    }
    public TodoEdge Model { get; } = model;

    public NodeViewModel From { get; } = from;

    public NodeViewModel To { get; } = to;

    /// <summary>選択中のノードに繋がっている線は強調する。</summary>
    public bool IsHighlighted { get; set; }

    /// <summary>最長経路の上の線。</summary>
    public bool IsOnCriticalPath { get; set; }

    /// <summary>クリックで選ばれている線（Delete で外せる）。</summary>
    public bool IsSelected { get; set; }
    public int SelectedWaypointIndex { get; set; } = -1;

    /// <summary>先行が片付いている線は薄く描く（もう通過した道）。</summary>
    public bool IsSettled => From.Model.IsSettled;
}
