using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Graph;

/// <summary>読み取ったアウトラインを、実際のステップとしてグラフに差し込む。</summary>
public static class OutlineImporter
{
    /// <summary>
    /// <paramref name="anchorId"/> の続きとして取り込む。深さ 0 の行がアンカーにぶら下がり、
    /// 以降はひとつ浅い行にぶら下がる。
    /// </summary>
    public static IReadOnlyList<TodoNode> Import(
        TodoGraph graph,
        IReadOnlyList<OutlineItem> items,
        Guid? anchorId = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(items);

        var created = new List<TodoNode>();
        var lastAtDepth = new Dictionary<int, Guid>();

        foreach (var item in items)
        {
            var node = new TodoNode
            {
                Title = item.Title,
                Tags = [.. item.Tags],
                Due = item.Due,
                EstimateMinutes = item.EstimateMinutes,
            };

            graph.AddNode(node);
            created.Add(node);

            var parentId = ResolveParent(item.Depth, lastAtDepth, anchorId, graph);
            if (parentId is { } parent)
            {
                graph.Connect(parent, node.Id);
            }

            lastAtDepth[item.Depth] = node.Id;

            // より深い階層の記憶は、浅い行が来た時点で捨てる。
            foreach (var deeper in lastAtDepth.Keys.Where(d => d > item.Depth).ToList())
            {
                lastAtDepth.Remove(deeper);
            }
        }

        return created;
    }

    private static Guid? ResolveParent(int depth, Dictionary<int, Guid> lastAtDepth, Guid? anchorId, TodoGraph graph)
    {
        for (var d = depth - 1; d >= 0; d--)
        {
            if (lastAtDepth.TryGetValue(d, out var id))
            {
                return id;
            }
        }

        return anchorId is { } anchor && graph.Contains(anchor) ? anchor : null;
    }
}
