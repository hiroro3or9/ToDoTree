using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

public enum MoveDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>矢印キーでの移動先を、画面上の位置関係から決める。</summary>
public static class Navigation
{
    /// <summary>その向きにある、一番近いステップ。無ければ null。</summary>
    public static TodoNode? FindNeighbor(TodoGraph graph, Guid fromId, MoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Find(fromId) is not { } origin)
        {
            return null;
        }

        TodoNode? best = null;
        var bestCost = double.MaxValue;

        foreach (var node in graph.Nodes)
        {
            if (node.Id == fromId)
            {
                continue;
            }

            var dx = node.X - origin.X;
            var dy = node.Y - origin.Y;

            var (primary, perpendicular) = direction switch
            {
                MoveDirection.Left => (-dx, Math.Abs(dy)),
                MoveDirection.Right => (dx, Math.Abs(dy)),
                MoveDirection.Up => (-dy, Math.Abs(dx)),
                _ => (dy, Math.Abs(dx)),
            };

            // その向きに十分離れているものだけを相手にする。
            if (primary <= 4)
            {
                continue;
            }

            // まっすぐ近いものを優先する（横にずれているほど不利）。
            var cost = primary + (perpendicular * 3);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = node;
            }
        }

        return best;
    }
}
