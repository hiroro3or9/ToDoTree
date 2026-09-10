using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>保存された接続の端点を、計算用のステップ集合へ解決する。</summary>
public static class BlockConnections
{
    public static IReadOnlyList<Guid> Members(TodoProject project, Guid endpoint)
    {
        if (project.Nodes.Any(n => n.Id == endpoint)) return [endpoint];
        var hierarchy = new BlockHierarchy(project);
        if (hierarchy.Find(endpoint) is not { } root) return [];
        return [.. root.NodeIds.Concat(hierarchy.DescendantsOf(endpoint).SelectMany(b => b.NodeIds)).Distinct()];
    }

    public static IEnumerable<TodoEdge> Expand(TodoProject project)
    {
        var endpoints = project.Nodes.ToDictionary(n => n.Id, n => (IReadOnlyList<Guid>)[n.Id]);
        var hierarchy = new BlockHierarchy(project);
        foreach (var block in project.Blocks)
            endpoints[block.Id] = [.. block.NodeIds.Concat(hierarchy.DescendantsOf(block.Id).SelectMany(b => b.NodeIds)).Distinct()];
        foreach (var edge in project.Edges)
        {
            if (!endpoints.TryGetValue(edge.FromId, out var sources) || !endpoints.TryGetValue(edge.ToId, out var targets)) continue;
            foreach (var from in sources)
            foreach (var to in targets)
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
        if (ValidatePorts(project) is { } portError) return portError;
        var known = project.Nodes.Select(n => n.Id).Concat(project.Blocks.Select(b => b.Id)).ToHashSet();
        if (project.Edges.Any(e => e is null || !known.Contains(e.FromId) || !known.Contains(e.ToId)))
            return "存在しないステップまたはブロックへの接続があります。";
        if (project.Edges.Select(e => e.Id).Distinct().Count() != project.Edges.Count
            || project.Edges.Select(e => (e.FromId, e.ToId)).Distinct().Count() != project.Edges.Count)
            return "接続が重複しています。";
        return new TodoGraph(project).HasCycle() ? "依存関係が循環するため変更できません。" : null;
    }

    /// <summary>解除する囲みの端点だけを展開し、他の囲みへの接続は維持する。</summary>
    public static void Dissolve(TodoProject project, TodoBlock block, IReadOnlyCollection<Guid>? resolvedMembers = null)
    {
        var members = resolvedMembers ?? Members(project, block.Id);
        foreach (var edge in project.Edges.Where(e => e.FromId == block.Id || e.ToId == block.Id).ToArray())
        {
            project.Edges.Remove(edge);
            IEnumerable<Guid> sources = edge.FromId == block.Id ? members : [edge.FromId];
            IEnumerable<Guid> targets = edge.ToId == block.Id ? members : [edge.ToId];
            foreach (var from in sources)
            foreach (var to in targets)
            {
                if (project.Edges.Any(e => e.FromId == from && e.ToId == to)) continue;
                var copy = edge.Clone();
                copy.Id = Guid.NewGuid(); copy.FromId = from; copy.ToId = to;
                if (edge.FromId == block.Id) copy.FromPortId = null;
                if (edge.ToId == block.Id) copy.ToPortId = null;
                project.Edges.Add(copy);
            }
        }
    }

    public static string? ValidatePorts(TodoProject project)
    {
        foreach (var block in project.Blocks)
        {
            if (block.Ports.Any(p => p is null || p.Id == Guid.Empty
                || p.Side is not (ConnectionSide.Top or ConnectionSide.Bottom or ConnectionSide.Left or ConnectionSide.Right)
                || !double.IsFinite(p.Position) || p.Position < 0 || p.Position > 1)
                || block.Ports.Select(p => p.Id).Distinct().Count() != block.Ports.Count)
                return "ブロックの接続点のIDまたは位置が不正です。";
        }
        foreach (var edge in project.Edges)
        {
            if (edge is null) continue;
            if (edge.FromPortId is { } from && FindPort(project, edge.FromId, from) is null
                || edge.ToPortId is { } to && FindPort(project, edge.ToId, to) is null)
                return "存在しない接続点への接続があります。";
        }
        return null;
    }

    public static BlockPort? FindPort(TodoProject project, Guid blockId, Guid? portId) =>
        portId is null ? null : project.Blocks.FirstOrDefault(b => b.Id == blockId)?.Ports.FirstOrDefault(p => p.Id == portId);

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
