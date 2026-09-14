using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

public enum BranchState { Active, Pending, Skipped }

public static class ChoiceService
{
    /// <summary>各分岐のどの選択肢から到達するかを伝播する。別の前提を接続しても見送りを解除しない。</summary>
    public static IReadOnlyDictionary<Guid, BranchState> Evaluate(TodoGraph graph)
    {
        var states = new Dictionary<Guid, BranchState>();
        var choices = graph.Nodes.Where(n => n.IsChoice).ToDictionary(n => n.Id);
        if (choices.Count == 0) return states;
        var explicitEdges = graph.Project.Edges.ToLookup(e => e.Id, e => e.FromId);
        var origins = new Dictionary<Guid, Dictionary<Guid, HashSet<Guid>>>();
        foreach (var node in graph.TopologicalOrder() ?? graph.Nodes)
        {
            var paths = new Dictionary<Guid, HashSet<Guid>>();
            void Add(Guid choice, IEnumerable<Guid> alternatives)
            {
                if (!paths.TryGetValue(choice, out var set)) paths[choice] = set = [];
                set.UnionWith(alternatives);
            }
            foreach (var edge in graph.IncomingOf(node.Id))
            {
                if (origins.TryGetValue(edge.FromId, out var inherited))
                    foreach (var (choice, alternatives) in inherited) Add(choice, alternatives);
                if (choices.ContainsKey(edge.FromId) && explicitEdges[edge.Id].Contains(edge.FromId))
                    Add(edge.FromId, [edge.Id]);
            }
            origins[node.Id] = paths;
            var state = BranchState.Active;
            foreach (var (choiceId, alternatives) in paths)
            {
                // 見送り側にある内側の分岐は、合流後の共通作業を止めない。
                if (states.GetValueOrDefault(choiceId, BranchState.Active) != BranchState.Active) continue;
                if (choices[choiceId].SelectedChoiceEdgeId is not { } selected)
                    state = state == BranchState.Skipped ? state : BranchState.Pending;
                else if (!alternatives.Contains(selected)) state = BranchState.Skipped;
            }
            states[node.Id] = state;
        }
        return states;
    }

    private static BranchState RouteState(TodoGraph graph, TodoEdge edge, IReadOnlyDictionary<Guid, BranchState> states)
    {
        var state = states.GetValueOrDefault(edge.FromId, BranchState.Active);
        if (state != BranchState.Active) return state;
        var source = graph.Find(edge.FromId);
        if (source?.IsChoice != true || !graph.Project.Edges.Any(e => e.Id == edge.Id && e.FromId == source.Id)) return BranchState.Active;
        return source.SelectedChoiceEdgeId is null ? BranchState.Pending
            : source.SelectedChoiceEdgeId == edge.Id ? BranchState.Active : BranchState.Skipped;
    }

    public static BranchState EdgeState(TodoGraph graph, TodoEdge edge) => RouteState(graph, edge, graph.BranchStates);

    public static string? Validate(TodoProject project, int version)
    {
        if (version < 12 && (project.Nodes.Any(n => n.IsChoice || n.SelectedChoiceEdgeId is not null)
            || project.Edges.Any(e => !string.IsNullOrEmpty(e.DecisionReason)) || project.Specimens.Count > 0))
            return "選択分岐と標本帳には形式12以降が必要です。";
        foreach (var node in project.Nodes)
            if (node.SelectedChoiceEdgeId is { } id && (!node.IsChoice || !project.Edges.Any(e => e.Id == id && e.FromId == node.Id)))
                return "選択した道が分岐元に存在しません。";
        if (project.Specimens.Any(s => s is null) || project.Specimens.Select(s => s.Id).Distinct().Count() != project.Specimens.Count)
            return "標本のIDが重複しています。";
        foreach (var specimen in project.Specimens)
        {
            if (specimen.Nodes is null || specimen.Edges is null || specimen.SkippedNodeIds is null
                || specimen.Nodes.Any(n => n is null || !double.IsFinite(n.X) || !double.IsFinite(n.Y))
                || specimen.Edges.Any(e => e is null)
                || !specimen.Nodes.Any(n => n.Id == specimen.GoalId && n.Kind == NodeKind.Goal && n.Status == NodeStatus.Done)
                || specimen.Nodes.Select(n => n.Id).Distinct().Count() != specimen.Nodes.Count
                || specimen.Edges.Any(e => !specimen.Nodes.Any(n => n.Id == e.FromId) || !specimen.Nodes.Any(n => n.Id == e.ToId)))
                return "標本のグラフまたは達成したゴールが不正です。";
        }
        return null;
    }
}
