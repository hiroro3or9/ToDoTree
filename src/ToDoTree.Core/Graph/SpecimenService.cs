using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Graph;

public static class SpecimenService
{
    public static AchievementSpecimen Capture(TodoGraph graph, Guid goalId, string category, string reflection)
    {
        graph.Rebuild();
        var goal = graph.Find(goalId) ?? throw new ArgumentException("ゴールが見つかりません。");
        if (goal.Kind != NodeKind.Goal || goal.Status != NodeStatus.Done || graph.BranchStateOf(goalId) != BranchState.Active)
            throw new InvalidOperationException("選んだ道の、完了したゴールを選択してください。");
        if (graph.Project.Specimens.Any(s => s.GoalId == goalId && (goal.CompletedAt is null || s.CompletedAt == goal.CompletedAt)))
            throw new InvalidOperationException("この達成はすでに標本帳に保存されています。");
        var ids = new HashSet<Guid> { goalId };
        var pending = new Stack<Guid>(); pending.Push(goalId);
        while (pending.TryPop(out var current))
            foreach (var edge in graph.IncomingOf(current))
                if (ChoiceService.EdgeState(graph, edge) != BranchState.Skipped && ids.Add(edge.FromId))
                    pending.Push(edge.FromId);
        if (ids.Any(id => graph.BranchStateOf(id) == BranchState.Pending
            || (graph.BranchStateOf(id) == BranchState.Active && !graph.Find(id)!.IsSettled)))
            throw new InvalidOperationException("道のりに未完了のステップ、または未選択の分岐があります。");
        // ゴールへ合流しない見送り案も、判断理由を振り返れるように残す。
        foreach (var choiceId in ids.Where(id => graph.Find(id)!.IsChoice).ToArray())
            ids.UnionWith(graph.Descendants(choiceId).Where(id => graph.BranchStateOf(id) == BranchState.Skipped));
        var resolver = ProjectVariableResolver.From(graph.Project);
        var nodes = graph.Nodes.Where(n => ids.Contains(n.Id)).Select(n => n.Clone()).ToList();
        foreach (var node in nodes)
        {
            node.Title = resolver.Expand(node.Title);
            node.Notes = resolver.Expand(node.Notes);
        }
        return new AchievementSpecimen
        {
            GoalId = goalId, Title = resolver.Expand(goal.Title), Category = category.Trim(), Reflection = reflection.Trim(),
            CompletedAt = goal.CompletedAt ?? DateTimeOffset.Now, Nodes = nodes,
            Edges = [.. graph.Edges.Where(e => ids.Contains(e.FromId) && ids.Contains(e.ToId)).Select(e => e.Clone())],
            SkippedNodeIds = [.. ids.Where(id => graph.BranchStateOf(id) == BranchState.Skipped)],
        };
    }
}
