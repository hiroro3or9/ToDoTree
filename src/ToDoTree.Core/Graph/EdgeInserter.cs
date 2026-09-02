using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>
/// すでに繋がっている 2 つのステップのあいだに、新しいステップを挟む。
/// <see cref="TodoGraph.RemoveNodeAndBridge"/>（途中を抜いて繋ぎ直す）の逆にあたる操作。
/// </summary>
public static class EdgeInserter
{
    /// <summary>
    /// from → to の線を外して、from → 新規 → to に繋ぎ直す。
    ///
    /// 線のラベルは後ろ半分（新規 → to）へ移す。ラベルは「何に向かうか」を説明していることが
    /// 多いので、<see cref="StepSplitter"/> が後続の線にラベルを引き継ぐのと同じ扱いにした。
    /// 座標は 2 つの中点。挟んだ直後から、もとの線の上に載って見える。
    ///
    /// 挟めなかったときは null を返し、グラフは呼ぶ前のままにする。
    /// </summary>
    public static TodoNode? InsertBetween(TodoGraph graph, Guid fromId, Guid toId, TodoNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        if (graph.Find(fromId) is not { } from || graph.Find(toId) is not { } to)
        {
            return null;
        }

        if (graph.OutgoingOf(fromId).FirstOrDefault(e => e.ToId == toId) is not { } edge)
        {
            return null;
        }

        var label = edge.Label;
        graph.Disconnect(fromId, toId);

        graph.AddNode(node);
        node.X = (from.X + to.X) / 2;
        node.Y = (from.Y + to.Y) / 2;

        // 新しいステップはどこにも繋がっていないので、ここで循環になることはない。
        // それでも繋げなかったときは、元の線に戻して何もなかったことにする。
        if (graph.Connect(fromId, node.Id) is null || graph.Connect(node.Id, toId, label) is null)
        {
            graph.RemoveNode(node.Id);
            graph.Connect(fromId, toId, label);
            return null;
        }

        return node;
    }
}
