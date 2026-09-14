using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

public sealed record BlockingCause(TodoNode Node, string Reason);

public static class BlockerAnalysis
{
    /// <summary>完了済みの前提より先は辿らず、未解決の前提と手動ブロックを重複なく返す。</summary>
    public static IReadOnlyList<BlockingCause> Find(TodoGraph graph, Guid targetId)
    {
        if (graph.Find(targetId) is not { IsSettled: false } target) return [];
        if (graph.BranchStateOf(targetId) == BranchState.Skipped) return [new(target, "分岐で見送った道です")];
        if (graph.BranchStateOf(targetId) == BranchState.Pending) return [new(target, "分岐元で進める道を選んでください")];
        var causes = new List<BlockingCause>();
        var seen = new HashSet<Guid>();
        var pending = new Stack<TodoNode>();
        pending.Push(target);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node.Id) || node.IsSettled) continue;
            if (node.IsManuallyBlocked)
                causes.Add(new(node, string.IsNullOrWhiteSpace(node.BlockReason)
                    ? "手動でブロック中（理由未入力）" : $"ブロック中：{node.BlockReason}"));
            var parents = graph.IncomingOf(node.Id).Where(e => ChoiceService.EdgeState(graph, e) != BranchState.Skipped)
                .Select(e => graph.Find(e.FromId)!).Where(p => !p.IsSettled).DistinctBy(p => p.Id).ToList();
            if (node.Id != targetId && !node.IsManuallyBlocked && parents.Count == 0)
                causes.Add(new(node, node.Status == NodeStatus.InProgress ? "前提の作業が進行中" : "前提の作業が未着手"));
            foreach (var parent in parents) pending.Push(parent);
        }
        return causes.OrderByDescending(c => c.Node.IsManuallyBlocked).ThenBy(c => c.Node.Title).ToList();
    }
}
