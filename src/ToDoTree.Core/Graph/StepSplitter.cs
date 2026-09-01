using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Graph;

/// <summary>大きすぎるステップを、細かいステップに割る。</summary>
public static class StepSplitter
{
    /// <summary>
    /// <paramref name="nodeId"/> の下に <paramref name="items"/> を差し込み、
    /// もともとの後続は、新しく作った末端から繋ぎ直す（流れが切れないようにする）。
    /// </summary>
    public static IReadOnlyList<TodoNode> Split(TodoGraph graph, Guid nodeId, IReadOnlyList<OutlineItem> items)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(items);

        if (graph.Find(nodeId) is null || items.Count == 0)
        {
            return [];
        }

        // 元の後続をいったん外しておく。
        var successors = graph.OutgoingOf(nodeId).Select(e => (e.ToId, e.Label)).ToList();
        foreach (var (toId, _) in successors)
        {
            graph.Disconnect(nodeId, toId);
        }

        var created = OutlineImporter.Import(graph, items, nodeId);
        var createdIds = created.Select(n => n.Id).ToHashSet();

        // 新しく作ったものの中で、後続を持たないもの＝この塊の出口。
        var exits = created
            .Where(n => !graph.OutgoingOf(n.Id).Any(e => createdIds.Contains(e.ToId)))
            .ToList();

        foreach (var exit in exits)
        {
            foreach (var (toId, label) in successors)
            {
                if (graph.CanConnect(exit.Id, toId).IsOk())
                {
                    graph.Connect(exit.Id, toId, label);
                }
            }
        }

        return created;
    }
}
