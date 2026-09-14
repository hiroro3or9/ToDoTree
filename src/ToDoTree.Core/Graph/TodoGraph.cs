using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>
/// <see cref="TodoProject"/> に隣接インデックスを被せた操作用ラッパー。
/// 循環を作る操作は必ず拒否するので、中身は常に DAG に保たれる。
/// </summary>
public sealed class TodoGraph
{
    private readonly Dictionary<Guid, TodoNode> _nodes = [];
    private readonly Dictionary<Guid, List<TodoEdge>> _outgoing = [];
    private readonly Dictionary<Guid, List<TodoEdge>> _incoming = [];

    public TodoGraph(TodoProject project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Rebuild();
    }

    public TodoProject Project { get; }
    public Guid? NewNodeParentId { get; set; }

    public IReadOnlyList<TodoNode> Nodes => Project.Nodes;

    private readonly List<TodoEdge> _effectiveEdges = [];

    public IReadOnlyList<TodoEdge> Edges => _effectiveEdges;

    public int NodeCount => Project.Nodes.Count;

    public IReadOnlyDictionary<Guid, BranchState> BranchStates { get; private set; } = new Dictionary<Guid, BranchState>();
    public BranchState BranchStateOf(Guid id)
    {
        var state = BranchStates.GetValueOrDefault(id, BranchState.Active);
        foreach (var parent in TaskAncestors(id))
        {
            var inherited = BranchStates.GetValueOrDefault(parent.Id, BranchState.Active);
            if (inherited == BranchState.Skipped) return BranchState.Skipped;
            if (inherited == BranchState.Pending && state == BranchState.Active) state = BranchState.Pending;
        }
        return state;
    }
    public bool DependenciesSettled(Guid id, Guid? completing = null) => IncomingOf(id)
        .All(e => ChoiceService.EdgeState(this, e) == BranchState.Skipped
            || (ChoiceService.EdgeState(this, e) == BranchState.Active && (e.FromId == completing || Find(e.FromId)!.IsSettled)));

    /// <summary>プロジェクトを直接いじった後にインデックスを張り直す。</summary>
    public void Rebuild()
    {
        _nodes.Clear();
        _outgoing.Clear();
        _incoming.Clear();

        foreach (var node in Project.Nodes)
        {
            _nodes[node.Id] = node;
            _outgoing[node.Id] = [];
            _incoming[node.Id] = [];
        }

        // 壊れた辺（存在しないノードを指す辺）はここで落とす。
        var endpoints = _nodes.Keys.Concat(Project.Blocks.Select(b => b.Id)).ToHashSet();
        Project.Edges.RemoveAll(e => !endpoints.Contains(e.FromId) || !endpoints.Contains(e.ToId));
        foreach (var node in Project.Nodes.Where(n => n.SelectedChoiceEdgeId is { } id && !Project.Edges.Any(e => e.Id == id && e.FromId == n.Id)))
            node.SelectedChoiceEdgeId = null;
        _effectiveEdges.Clear();
        _effectiveEdges.AddRange(BlockConnections.Expand(Project));

        foreach (var edge in _effectiveEdges)
        {
            _outgoing[edge.FromId].Add(edge);
            _incoming[edge.ToId].Add(edge);
        }
        BranchStates = ChoiceService.Evaluate(this);
        TaskHierarchy.Synchronize(this);
    }

    public TodoNode? Find(Guid id) => _nodes.TryGetValue(id, out var node) ? node : null;

    public bool Contains(Guid id) => _nodes.ContainsKey(id);

    public IEnumerable<TodoNode> TaskAncestors(Guid id)
    {
        if (Find(id)?.ParentTaskId is null) yield break;
        var seen = new HashSet<Guid> { id };
        while (Find(id)?.ParentTaskId is { } parent && seen.Add(parent) && Find(parent) is { } node)
        {
            yield return node;
            id = parent;
        }
    }

    public IReadOnlyList<TodoEdge> OutgoingOf(Guid id) =>
        _outgoing.TryGetValue(id, out var list) ? list : Array.Empty<TodoEdge>();

    public IReadOnlyList<TodoEdge> IncomingOf(Guid id) =>
        _incoming.TryGetValue(id, out var list) ? list : Array.Empty<TodoEdge>();

    /// <summary>先行（このステップの前に終わっている必要があるもの）。</summary>
    public IEnumerable<TodoNode> ParentsOf(Guid id) =>
        IncomingOf(id).Select(e => _nodes[e.FromId]).DistinctBy(n => n.Id);

    /// <summary>後続（このステップが終わると進めるもの）。</summary>
    public IEnumerable<TodoNode> ChildrenOf(Guid id) =>
        OutgoingOf(id).Select(e => _nodes[e.ToId]).DistinctBy(n => n.Id);

    /// <summary>先行を持たないノード（出発点）。</summary>
    public IEnumerable<TodoNode> Roots() => Project.Nodes.Where(n => IncomingOf(n.Id).Count == 0);

    /// <summary>後続を持たないノード（終端）。</summary>
    public IEnumerable<TodoNode> Leaves() => Project.Nodes.Where(n => OutgoingOf(n.Id).Count == 0);

    public TodoNode AddNode(TodoNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_nodes.ContainsKey(node.Id))
        {
            throw new InvalidOperationException($"ID {node.Id} のステップはすでに存在します。");
        }

        node.ParentTaskId ??= NewNodeParentId;
        Project.Nodes.Add(node);
        _nodes[node.Id] = node;
        _outgoing[node.Id] = [];
        _incoming[node.Id] = [];
        return node;
    }

    /// <summary>ノードと、それに繋がる辺をまとめて削除する。</summary>
    public bool RemoveNode(Guid id)
    {
        if (!_nodes.TryGetValue(id, out var node))
        {
            return false;
        }

        var removing = TaskHierarchy.IncludeDescendants(Project, [id]);
        Project.Edges.RemoveAll(e => removing.Contains(e.FromId) || removing.Contains(e.ToId));
        Project.Nodes.RemoveAll(n => removing.Contains(n.Id));
        if (Project.Bookmark is { } bookmark && removing.Contains(bookmark.NodeId)) Project.Bookmark = null;
        BlockService.Remove(Project, removing.ToArray());
        Rebuild();
        return true;
    }

    /// <summary>
    /// ノードを削除しつつ、その先行と後続を直接繋ぎ直す（鎖の途中を抜いても列が切れない）。
    /// </summary>
    public bool RemoveNodeAndBridge(Guid id)
    {
        if (!_nodes.ContainsKey(id))
        {
            return false;
        }

        var parents = Project.Edges.Where(e => e.ToId == id).Select(e => e.FromId).ToList();
        var children = Project.Edges.Where(e => e.FromId == id).Select(e => e.ToId).ToList();

        RemoveNode(id);

        foreach (var parent in parents)
        {
            foreach (var child in children)
            {
                if (CanConnect(parent, child).IsOk())
                {
                    Connect(parent, child);
                }
            }
        }

        return true;
    }

    public ConnectionCheck CanConnect(Guid fromId, Guid toId)
    {
        if (fromId == toId)
        {
            return ConnectionCheck.SameNode;
        }

        if (BlockConnections.Members(Project, fromId).Count == 0 || BlockConnections.Members(Project, toId).Count == 0)
        {
            return ConnectionCheck.NodeNotFound;
        }

        if (BlockConnections.Members(Project, fromId).Concat(BlockConnections.Members(Project, toId))
            .Select(id => Find(id)!.ParentTaskId).Distinct().Count() > 1)
            return ConnectionCheck.DifferentTaskScope;

        if (Project.Edges.Any(e => e.FromId == fromId && e.ToId == toId))
        {
            return ConnectionCheck.Duplicate;
        }

        // to から辿って from に着くなら、繋いだ瞬間に循環する。
        if (BlockConnections.Members(Project, fromId).Any(from =>
            BlockConnections.Members(Project, toId).Any(to => CanReach(to, from))))
        {
            return ConnectionCheck.WouldCreateCycle;
        }

        return ConnectionCheck.Ok;
    }

    public TodoEdge? Connect(Guid fromId, Guid toId, string? label = null)
    {
        if (!CanConnect(fromId, toId).IsOk())
        {
            return null;
        }

        var edge = new TodoEdge { FromId = fromId, ToId = toId, Label = label };
        Project.Edges.Add(edge);
        Rebuild();
        return edge;
    }

    public bool Disconnect(Guid edgeId)
    {
        var edge = Project.Edges.FirstOrDefault(e => e.Id == edgeId);
        return edge is not null && RemoveEdgeCore(edge);
    }

    public bool Disconnect(Guid fromId, Guid toId)
    {
        var edge = Project.Edges.FirstOrDefault(e => e.FromId == fromId && e.ToId == toId);
        return edge is not null && RemoveEdgeCore(edge);
    }

    /// <summary><paramref name="fromId"/> から <paramref name="targetId"/> に到達できるか。</summary>
    public bool CanReach(Guid fromId, Guid targetId)
    {
        if (fromId == targetId)
        {
            return true;
        }

        var seen = new HashSet<Guid> { fromId };
        var stack = new Stack<Guid>();
        stack.Push(fromId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var edge in OutgoingOf(current))
            {
                if (edge.ToId == targetId)
                {
                    return true;
                }

                if (seen.Add(edge.ToId))
                {
                    stack.Push(edge.ToId);
                }
            }
        }

        return false;
    }

    private bool RemoveEdgeCore(TodoEdge edge)
    {
        var removed = Project.Edges.RemoveAll(e => e.Id == edge.Id) > 0;
        foreach (var node in Project.Nodes.Where(n => n.SelectedChoiceEdgeId == edge.Id)) node.SelectedChoiceEdgeId = null;
        if (removed) Rebuild();
        return removed;
    }
}
