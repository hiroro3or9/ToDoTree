using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>保存された接続の端点を、計算用のステップ集合へ解決する。</summary>
public static class BlockConnections
{
    public static IReadOnlyList<Guid> Members(TodoProject project, Guid endpoint) =>
        project.Blocks.FirstOrDefault(b => b.Id == endpoint)?.NodeIds
        ?? (project.Nodes.Any(n => n.Id == endpoint) ? [endpoint] : []);

    public static IEnumerable<TodoEdge> Expand(TodoProject project)
    {
        foreach (var edge in project.Edges)
        {
            foreach (var from in Members(project, edge.FromId))
            foreach (var to in Members(project, edge.ToId))
            {
                if (from == edge.FromId && to == edge.ToId) yield return edge;
                else yield return new TodoEdge { Id = edge.Id, FromId = from, ToId = to, Label = edge.Label };
            }
        }
    }

    public static string? Validate(TodoProject project)
    {
        if (project.Nodes.Select(n => n.Id).Distinct().Count() != project.Nodes.Count)
            return "ステップのIDが重複しています。";
        if (BlockService.Validate(project) is { } error) return error;
        var known = project.Nodes.Select(n => n.Id).Concat(project.Blocks.Select(b => b.Id)).ToHashSet();
        if (project.Edges.Any(e => e is null || !known.Contains(e.FromId) || !known.Contains(e.ToId)))
            return "存在しないステップまたはブロックへの接続があります。";
        if (project.Edges.Select(e => e.Id).Distinct().Count() != project.Edges.Count
            || project.Edges.Select(e => (e.FromId, e.ToId)).Distinct().Count() != project.Edges.Count)
            return "接続が重複しています。";
        return new TodoGraph(project).HasCycle() ? "依存関係が循環するため変更できません。" : null;
    }

    /// <summary>解除する囲みの端点だけを展開し、他の囲みへの接続は維持する。</summary>
    public static void Dissolve(TodoProject project, TodoBlock block)
    {
        foreach (var edge in project.Edges.Where(e => e.FromId == block.Id || e.ToId == block.Id).ToArray())
        {
            project.Edges.Remove(edge);
            var sources = edge.FromId == block.Id ? block.NodeIds : new List<Guid> { edge.FromId };
            var targets = edge.ToId == block.Id ? block.NodeIds : new List<Guid> { edge.ToId };
            foreach (var from in sources)
            foreach (var to in targets)
            {
                if (project.Edges.Any(e => e.FromId == from && e.ToId == to)) continue;
                var copy = edge.Clone();
                copy.Id = Guid.NewGuid(); copy.FromId = from; copy.ToId = to;
                project.Edges.Add(copy);
            }
        }
    }

    public static IReadOnlySet<Guid> FocusNodes(TodoGraph graph, Guid blockId)
    {
        var members = Members(graph.Project, blockId).ToHashSet();
        var keep = new HashSet<Guid>(members);
        foreach (var id in members)
        {
            keep.UnionWith(graph.ParentsOf(id).Select(n => n.Id));
            keep.UnionWith(graph.ChildrenOf(id).Select(n => n.Id));
        }
        return keep;
    }
}
