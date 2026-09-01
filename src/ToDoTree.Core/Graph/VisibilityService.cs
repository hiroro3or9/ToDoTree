namespace ToDoTree.Core.Graph;

public sealed class VisibilityOptions
{
    /// <summary>折りたたまれているステップ。この先だけから辿れるものが隠れる。</summary>
    public IReadOnlySet<Guid> Collapsed { get; init; } = new HashSet<Guid>();

    /// <summary>ここに絞る。指定すると、その上流・下流以外が隠れる。</summary>
    public Guid? FocusId { get; init; }

    /// <summary>完了・取り消しを隠す。</summary>
    public bool HideCompleted { get; init; }
}

public sealed record VisibilityResult(
    IReadOnlySet<Guid> Visible,
    IReadOnlyDictionary<Guid, int> CollapsedCounts)
{
    public bool IsVisible(Guid id) => Visible.Contains(id);

    public int HiddenBehind(Guid id) => CollapsedCounts.TryGetValue(id, out var count) ? count : 0;
}

/// <summary>
/// 「いま何を見せるか」をまとめて決める。折りたたみ・絞り込み・完了隠しは、
/// どれも「表示するノードの集合」の話なので 1 か所で計算する。
/// </summary>
public static class VisibilityService
{
    public static VisibilityResult Compute(TodoGraph graph, VisibilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        options ??= new VisibilityOptions();

        var hidden = new HashSet<Guid>();
        var order = graph.TopologicalOrder() ?? [.. graph.Nodes];

        // 折りたたみ：先行がすべて「折りたたまれた側」なら、そのステップは見えなくなる。
        // 別の枝からも辿れるステップは、そちらが生きているので残る。
        foreach (var node in order)
        {
            if (options.Collapsed.Contains(node.Id))
            {
                continue;
            }

            var parents = graph.ParentsOf(node.Id).ToList();
            if (parents.Count == 0)
            {
                continue;
            }

            if (parents.All(p => options.Collapsed.Contains(p.Id) || hidden.Contains(p.Id)))
            {
                hidden.Add(node.Id);
            }
        }

        var counts = new Dictionary<Guid, int>();
        foreach (var collapsed in options.Collapsed)
        {
            if (graph.Contains(collapsed))
            {
                counts[collapsed] = graph.Descendants(collapsed).Count(hidden.Contains);
            }
        }

        // 絞り込み：選んだステップの上流・下流だけを残す。
        if (options.FocusId is { } focus && graph.Contains(focus))
        {
            var keep = new HashSet<Guid>(graph.Ancestors(focus));
            keep.UnionWith(graph.Descendants(focus));
            keep.Add(focus);

            foreach (var node in graph.Nodes)
            {
                if (!keep.Contains(node.Id))
                {
                    hidden.Add(node.Id);
                }
            }
        }

        if (options.HideCompleted)
        {
            foreach (var node in graph.Nodes)
            {
                if (node.IsSettled)
                {
                    hidden.Add(node.Id);
                }
            }
        }

        var visible = graph.Nodes
            .Select(n => n.Id)
            .Where(id => !hidden.Contains(id))
            .ToHashSet();

        return new VisibilityResult(visible, counts);
    }
}
