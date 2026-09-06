namespace ToDoTree.Core.Graph;

/// <summary>状態を書き換えずに、単独／一括完了による新しい着手先を調べる。</summary>
public sealed record CompletionImpact(IReadOnlySet<Guid> Sources, IReadOnlySet<Guid> Unlocked)
{
    public static CompletionImpact Calculate(TodoGraph graph, IEnumerable<Guid> completing)
    {
        var sources = completing.Where(id => graph.Find(id) is { IsSettled: false }).ToHashSet();
        var unlocked = sources.SelectMany(graph.ChildrenOf).DistinctBy(n => n.Id)
            .Where(n => !sources.Contains(n.Id) && graph.ReadinessOf(n) == Models.Readiness.Blocked
                && graph.ParentsOf(n.Id).All(p => p.IsSettled || sources.Contains(p.Id)))
            .Select(n => n.Id).ToHashSet();
        return new(sources, unlocked);
    }
}
