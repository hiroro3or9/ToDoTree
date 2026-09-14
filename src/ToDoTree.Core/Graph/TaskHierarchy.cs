using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>作業の包含関係。依存関係の線とは独立して保持する。</summary>
public static class TaskHierarchy
{
    public static IEnumerable<TodoNode> Children(TodoProject project, Guid id) =>
        project.Nodes.Where(n => n.ParentTaskId == id);

    public static IReadOnlyList<TodoNode> Ancestors(TodoProject project, Guid id)
    {
        var byId = project.Nodes.ToDictionary(n => n.Id);
        var result = new List<TodoNode>();
        var seen = new HashSet<Guid> { id };
        while (byId.TryGetValue(id, out var node) && node.ParentTaskId is { } parent && seen.Add(parent)
            && byId.TryGetValue(parent, out var owner))
        {
            result.Add(owner);
            id = parent;
        }
        return result;
    }

    public static HashSet<Guid> IncludeDescendants(TodoProject project, IEnumerable<Guid> selection)
    {
        var children = project.Nodes.Where(n => n.ParentTaskId is not null).ToLookup(n => n.ParentTaskId!.Value);
        var ids = selection.ToHashSet();
        var queue = new Queue<Guid>(ids);
        while (queue.TryDequeue(out var id))
            foreach (var node in children[id])
                if (ids.Add(node.Id)) queue.Enqueue(node.Id);
        return ids;
    }

    public static string? Validate(TodoProject project, int version)
    {
        if (project.Nodes.Any(n => n is null) || project.Blocks.Any(b => b is null) || project.Edges.Any(e => e is null))
            return "プロジェクトに空の要素が含まれています。";
        if (version < 14 && project.Nodes.Any(n => n.ParentTaskId is not null))
            return "内部ステップには形式14以降が必要です。";
        var nodes = project.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var node in project.Nodes)
        {
            if (node.ParentTaskId is not { } parent) continue;
            if (!nodes.TryGetValue(parent, out var owner)) return "内部ステップの親作業が存在しません。";
            if (owner.Repeat is not null || owner.ProjectLink is not null)
                return "回数の項目とプロジェクトの入口には内部ステップを持たせられません。";
            var seen = new HashSet<Guid> { node.Id };
            var cursor = node;
            while (cursor.ParentTaskId is { } id && nodes.TryGetValue(id, out cursor))
                if (!seen.Add(id)) return "作業の親子関係が循環しています。";
        }
        foreach (var block in project.Blocks)
            if (BlockConnections.Members(project, block.Id).Where(nodes.ContainsKey)
                .Select(id => nodes[id].ParentTaskId).Distinct().Count() > 1)
                return "異なる作業階層のステップを同じブロックには入れられません。";
        foreach (var edge in project.Edges)
            if (BlockConnections.Members(project, edge.FromId).Concat(BlockConnections.Members(project, edge.ToId))
                .Where(nodes.ContainsKey).Select(id => nodes[id].ParentTaskId).Distinct().Count() > 1)
                return "異なる作業階層へは直接接続できません。外側の親カードを接続してください。";
        return null;
    }

    /// <summary>外側の先行・ブロック・選択条件をすべての内部ステップへ引き継ぐ。</summary>
    public static bool AncestorsReady(TodoGraph graph, Guid id) =>
        graph.TaskAncestors(id).All(p => !p.IsManuallyBlocked && p.Status != NodeStatus.Cancelled
            && graph.BranchStateOf(p.Id) == BranchState.Active && graph.DependenciesSettled(p.Id));

    /// <summary>深い階層から親へ集計する。完了日時は完了状態が変わったときだけ更新する。</summary>
    public static void Synchronize(TodoGraph graph)
    {
        var groups = graph.Nodes.Where(n => n.ParentTaskId is not null).ToLookup(n => n.ParentTaskId!.Value);
        var parents = graph.Nodes.Where(n => groups.Contains(n.Id))
            .OrderByDescending(n => graph.TaskAncestors(n.Id).Count());
        foreach (var parent in parents)
        {
            var children = groups[parent.Id].Where(n => graph.BranchStateOf(n.Id) != BranchState.Skipped).ToArray();
            var done = children.All(n => n.IsSettled && graph.BranchStateOf(n.Id) != BranchState.Pending);
            var status = done ? NodeStatus.Done
                : children.Any(n => n.Status is NodeStatus.Done or NodeStatus.InProgress) ? NodeStatus.InProgress
                : NodeStatus.NotStarted;
            if (parent.Status == status) continue;
            parent.Status = status;
            parent.CompletedAt = done ? DateTimeOffset.Now : null;
            parent.UpdatedAt = DateTimeOffset.Now;
        }
    }

    /// <summary>現在の階層だけを操作するためのビュー。ノードや線の実体は元文書と共有する。</summary>
    public static TodoProject Scope(TodoProject project, Guid? parent)
    {
        var nodes = project.Nodes.Where(n => n.ParentTaskId == parent).ToList();
        var ids = nodes.Select(n => n.Id).ToHashSet();
        var blocks = project.Blocks.Where(b => BlockConnections.Members(project, b.Id).Any(ids.Contains)).ToList();
        ids.UnionWith(blocks.Select(b => b.Id));
        return new TodoProject
        {
            Id = project.Id, Name = project.Name, Variables = project.Variables,
            Nodes = nodes, Blocks = blocks,
            Edges = project.Edges.Where(e => ids.Contains(e.FromId) && ids.Contains(e.ToId)).ToList(),
        };
    }
}