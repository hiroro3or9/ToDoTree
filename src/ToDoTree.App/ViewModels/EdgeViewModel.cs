using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>2 枚のカードを結ぶ線。</summary>
public sealed class EdgeViewModel(TodoEdge model, NodeViewModel from, NodeViewModel to, MainViewModel? owner = null)
{
    private Vec2[]? _routeInputs;
    private JunctionPoint[]? _waypointInputs;
    private (double Width, double Height, ConnectionSide From, ConnectionSide To) _routeStyle;
    private IReadOnlyList<Vec2>? _route;

    public IReadOnlyList<Vec2> GetRoute(IEnumerable<NodeViewModel> nodes)
    {
        if (owner is not null && (owner.DisplayEndpoint(Model.FromId).IsBlock || owner.DisplayEndpoint(Model.ToId).IsBlock))
        {
            var source = owner.DisplayEndpoint(Model.FromId);
            var target = owner.DisplayEndpoint(Model.ToId);
            var boxes = nodes.Where(n => n.IsVisible && n.Id != Model.FromId && n.Id != Model.ToId)
                .Where(n => owner.BlockOf(n.Id)?.Id != source.Id && owner.BlockOf(n.Id)?.Id != target.Id)
                .Select(n => new BlockBounds(n.X, n.Y, NodeViewModel.CardWidth, NodeViewModel.CardHeight))
                .Concat(owner.Blocks.Where(b => b.IsVisible && b.IsCollapsed && b.Id != source.Id && b.Id != target.Id).Select(b => b.Bounds)).ToArray();
            return EdgeRouting.RouteRects(source.Bounds, target.Bounds, boxes, Model.FromSide, Model.ToSide,
                IsAggregated ? [] : [.. Model.Waypoints.Select(p => p.ToVector())],
                IsAggregated ? [] : [.. Model.Waypoints.Select(p => p.IsSmooth)]);
        }
        var from = new Vec2(From.X, From.Y);
        var to = new Vec2(To.X, To.Y);
        var obstacles = nodes.Where(n => n.IsVisible && n.Id != From.Id && n.Id != To.Id)
            .Select(n => new Vec2(n.X, n.Y)).ToArray();
        Vec2[] inputs = [from, to, .. obstacles];
        var waypoints = Model.Waypoints.ToArray();
        var style = (NodeViewModel.CardWidth, NodeViewModel.CardHeight, Model.FromSide, Model.ToSide);
        if (_route is null || _routeInputs is null || !_routeInputs.SequenceEqual(inputs) || _routeStyle != style || _waypointInputs is null || !_waypointInputs.SequenceEqual(waypoints))
        {
            _route = EdgeRouting.Route(from, to, style.CardWidth, style.CardHeight, obstacles, Model.FromSide, Model.ToSide,
                [.. waypoints.Select(p => p.ToVector())], [.. waypoints.Select(p => p.IsSmooth)]);
            _waypointInputs = waypoints;
            _routeInputs = inputs;
            _routeStyle = style;
        }
        return _route;
    }

    /// <summary>
    /// 次に描くときに経路を計算し直す。
    /// カードがまとまって動いたあとは、その線に繋がっていない辺も障害物の並びが変わっている。
    /// </summary>
    public void InvalidateRoute()
    {
        _route = null;
        _routeInputs = null;
        _waypointInputs = null;
    }

    public TodoEdge Model { get; } = model;

    public NodeViewModel From => owner?.EndpointNode(Model.FromId) ?? from;

    public NodeViewModel To => owner?.EndpointNode(Model.ToId) ?? to;

    public bool IsBlockConnection => owner?.Graph.Project.Blocks.Any(b => b.Id == Model.FromId || b.Id == Model.ToId) == true;
    public bool IsAggregated => owner is not null &&
        (owner.DisplayEndpoint(Model.FromId).Id != Model.FromId || owner.DisplayEndpoint(Model.ToId).Id != Model.ToId);
    public bool IsVisible => owner is null ? From.IsVisible && To.IsVisible :
        owner.DisplayEndpoint(Model.FromId) is { Visible: true } a &&
        owner.DisplayEndpoint(Model.ToId) is { Visible: true } b && a.Id != b.Id;
    public string ConnectionDescription => IsBlockConnection ? "ブロック全体の依存関係" : IsAggregated ? "内部ステップへの個別接続" : "ステップ間の依存関係";

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
