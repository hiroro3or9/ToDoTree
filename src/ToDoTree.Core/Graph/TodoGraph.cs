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

    public IReadOnlyList<TodoNode> Nodes => Project.Nodes;

    public IReadOnlyList<TodoEdge> Edges => Project.Edges;

    public int NodeCount => Project.Nodes.Count;

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
        Project.Edges.RemoveAll(e => !_nodes.ContainsKey(e.FromId) || !_nodes.ContainsKey(e.ToId));

        foreach (var edge in Project.Edges)
        {
            _outgoing[edge.FromId].Add(edge);
            _incoming[edge.ToId].Add(edge);
        }
    }

    public TodoNode? Find(Guid id) => _nodes.TryGetValue(id, out var node) ? node : null;

    public bool Contains(Guid id) => _nodes.ContainsKey(id);

    public IReadOnlyList<TodoEdge> OutgoingOf(Guid id) =>
        _outgoing.TryGetValue(id, out var list) ? list : Array.Empty<TodoEdge>();

    public IReadOnlyList<TodoEdge> IncomingOf(Guid id) =>
        _incoming.TryGetValue(id, out var list) ? list : Array.Empty<TodoEdge>();

    /// <summary>先行（このステップの前に終わっている必要があるもの）。</summary>
    public IEnumerable<TodoNode> ParentsOf(Guid id) =>
        IncomingOf(id).Select(e => _nodes[e.FromId]);

    /// <summary>後続（このステップが終わると進めるもの）。</summary>
    public IEnumerable<TodoNode> ChildrenOf(Guid id) =>
        OutgoingOf(id).Select(e => _nodes[e.ToId]);

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

        foreach (var edge in OutgoingOf(id).Concat(IncomingOf(id)).ToList())
        {
            RemoveEdgeCore(edge);
        }

        Project.Nodes.Remove(node);
        _nodes.Remove(id);
        _outgoing.Remove(id);
        _incoming.Remove(id);
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

        var parents = ParentsOf(id).Select(n => n.Id).ToList();
        var children = ChildrenOf(id).Select(n => n.Id).ToList();

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

        if (!_nodes.ContainsKey(fromId) || !_nodes.ContainsKey(toId))
        {
            return ConnectionCheck.NodeNotFound;
        }

        if (OutgoingOf(fromId).Any(e => e.ToId == toId))
        {
            return ConnectionCheck.Duplicate;
        }

        // to から辿って from に着くなら、繋いだ瞬間に循環する。
        if (CanReach(toId, fromId))
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
        _outgoing[fromId].Add(edge);
        _incoming[toId].Add(edge);
        return edge;
    }

    public bool Disconnect(Guid edgeId)
    {
        var edge = Project.Edges.FirstOrDefault(e => e.Id == edgeId);
        return edge is not null && RemoveEdgeCore(edge);
    }

    public bool Disconnect(Guid fromId, Guid toId)
    {
        var edge = OutgoingOf(fromId).FirstOrDefault(e => e.ToId == toId);
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
        var removed = Project.Edges.Remove(edge);
        if (_outgoing.TryGetValue(edge.FromId, out var outList))
        {
            outList.Remove(edge);
        }

        if (_incoming.TryGetValue(edge.ToId, out var inList))
        {
            inList.Remove(edge);
        }

        return removed;
    }
}
