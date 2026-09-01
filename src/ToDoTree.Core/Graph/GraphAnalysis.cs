using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>グラフから読み取れること（着手可能・進捗・クリティカルパスなど）。</summary>
public static class GraphAnalysis
{
    /// <summary>見積もりが無いステップを何分とみなすか。</summary>
    public const int DefaultEstimateMinutes = 30;

    /// <summary>トポロジカル順（Kahn 法）。循環があれば null。</summary>
    public static IReadOnlyList<TodoNode>? TopologicalOrder(this TodoGraph graph)
    {
        var indegree = graph.Nodes.ToDictionary(n => n.Id, n => graph.IncomingOf(n.Id).Count);
        var queue = new Queue<TodoNode>(graph.Nodes.Where(n => indegree[n.Id] == 0));
        var result = new List<TodoNode>(graph.NodeCount);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);

            foreach (var edge in graph.OutgoingOf(node.Id))
            {
                if (--indegree[edge.ToId] == 0)
                {
                    var next = graph.Find(edge.ToId);
                    if (next is not null)
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return result.Count == graph.NodeCount ? result : null;
    }

    public static bool HasCycle(this TodoGraph graph) => graph.TopologicalOrder() is null;

    /// <summary>そのステップにいま着手できるか（先行がすべて片付いているか）。</summary>
    public static Readiness ReadinessOf(this TodoGraph graph, TodoNode node) => node.Status switch
    {
        NodeStatus.Done => Readiness.Done,
        NodeStatus.Cancelled => Readiness.Cancelled,
        NodeStatus.InProgress => Readiness.InProgress,
        _ => graph.ParentsOf(node.Id).All(p => p.IsSettled) ? Readiness.Ready : Readiness.Blocked,
    };

    /// <summary>いま着手できる（＝次にやるべき）ステップ。</summary>
    public static IEnumerable<TodoNode> ReadyNodes(this TodoGraph graph) =>
        graph.Nodes.Where(n => graph.ReadinessOf(n) == Readiness.Ready);

    /// <summary>上流（このステップより前にあるすべて）。</summary>
    public static IReadOnlySet<Guid> Ancestors(this TodoGraph graph, Guid id) =>
        Traverse(id, current => graph.IncomingOf(current).Select(e => e.FromId));

    /// <summary>下流（このステップより後にあるすべて）。</summary>
    public static IReadOnlySet<Guid> Descendants(this TodoGraph graph, Guid id) =>
        Traverse(id, current => graph.OutgoingOf(current).Select(e => e.ToId));

    public static ProgressSummary Progress(this TodoGraph graph)
    {
        if (graph.NodeCount == 0)
        {
            return ProgressSummary.Empty;
        }

        int done = 0, inProgress = 0, ready = 0, blocked = 0, cancelled = 0, overdue = 0;
        int estimatedTotal = 0, estimatedDone = 0;

        foreach (var node in graph.Nodes)
        {
            if (node.IsOverdue)
            {
                overdue++;
            }

            switch (graph.ReadinessOf(node))
            {
                case Readiness.Done:
                    done++;
                    break;
                case Readiness.Cancelled:
                    cancelled++;
                    break;
                case Readiness.InProgress:
                    inProgress++;
                    break;
                case Readiness.Ready:
                    ready++;
                    break;
                default:
                    blocked++;
                    break;
            }

            if (node.Status == NodeStatus.Cancelled)
            {
                continue;
            }

            var estimate = node.EstimateMinutes ?? DefaultEstimateMinutes;
            estimatedTotal += estimate;
            if (node.Status == NodeStatus.Done)
            {
                estimatedDone += estimate;
            }
        }

        var total = graph.NodeCount - cancelled;
        return new ProgressSummary(total, done, inProgress, ready, blocked, cancelled, overdue, estimatedTotal, estimatedDone);
    }

    /// <summary>
    /// 見積もり時間で重み付けした最長経路（＝一番時間がかかる鎖）。ここが遅れると全体が遅れる。
    /// </summary>
    public static IReadOnlyList<TodoNode> CriticalPath(this TodoGraph graph)
    {
        var order = graph.TopologicalOrder();
        if (order is null || order.Count == 0)
        {
            return [];
        }

        var best = new Dictionary<Guid, int>(order.Count);
        var previous = new Dictionary<Guid, Guid?>(order.Count);

        foreach (var node in order)
        {
            var weight = node.EstimateMinutes ?? DefaultEstimateMinutes;
            var bestParent = (Guid?)null;
            var bestParentCost = 0;

            foreach (var edge in graph.IncomingOf(node.Id))
            {
                if (best.TryGetValue(edge.FromId, out var cost) && cost > bestParentCost)
                {
                    bestParentCost = cost;
                    bestParent = edge.FromId;
                }
                else if (bestParent is null && best.TryGetValue(edge.FromId, out int value))
                {
                    bestParent = edge.FromId;
                    bestParentCost = value;
                }
            }

            best[node.Id] = bestParentCost + weight;
            previous[node.Id] = bestParent;
        }

        var endId = best.OrderByDescending(kv => kv.Value).First().Key;
        var path = new List<TodoNode>();
        Guid? cursor = endId;

        while (cursor is { } id)
        {
            var node = graph.Find(id);
            if (node is null)
            {
                break;
            }

            path.Add(node);
            cursor = previous.TryGetValue(id, out var parent) ? parent : null;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// そのステップを片付けると、新しく着手できるようになる後続。
    /// 「これを終わらせると何個が動き出すか」＝詰まりの解消度になる。
    /// </summary>
    public static IReadOnlyList<TodoNode> NodesUnlockedBy(this TodoGraph graph, Guid id)
    {
        var unlocked = new List<TodoNode>();

        foreach (var child in graph.ChildrenOf(id))
        {
            if (child.IsSettled)
            {
                continue;
            }

            // 自分以外の先行がすべて片付いているなら、自分が終われば動き出す。
            if (graph.ParentsOf(child.Id).All(p => p.Id == id || p.IsSettled))
            {
                unlocked.Add(child);
            }
        }

        return unlocked;
    }

    private static HashSet<Guid> Traverse(Guid start, Func<Guid, IEnumerable<Guid>> next)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            foreach (var id in next(stack.Pop()))
            {
                if (visited.Add(id))
                {
                    stack.Push(id);
                }
            }
        }

        visited.Remove(start);
        return visited;
    }
}
