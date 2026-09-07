using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Layout;

/// <summary>
/// Sugiyama 風の階層レイアウト。
/// (1) 最長経路法でレイヤを決め → (2) バリセンター法で並び替えて交差を減らし → (3) 座標を配る。
/// </summary>
public static class LayeredLayoutEngine
{
    public static IReadOnlyDictionary<Guid, int> ComputeLayers(TodoGraph graph)
    {
        var layers = new Dictionary<Guid, int>(graph.NodeCount);
        var order = graph.TopologicalOrder();

        if (order is null)
        {
            // 循環がある（通常は起きない）ときは全部同じレイヤに置いて逃げる。
            foreach (var node in graph.Nodes)
            {
                layers[node.Id] = 0;
            }

            return layers;
        }

        foreach (var node in order)
        {
            var layer = 0;
            foreach (var edge in graph.IncomingOf(node.Id))
            {
                if (layers.TryGetValue(edge.FromId, out var parentLayer))
                {
                    layer = Math.Max(layer, parentLayer + 1);
                }
            }

            layers[node.Id] = layer;
        }

        return layers;
    }

    public static void Apply(TodoGraph graph, LayoutOptions? options = null)
    {
        options ??= new LayoutOptions();
        if (graph.NodeCount == 0)
        {
            return;
        }

        var layers = ComputeLayers(graph);
        var maxLayer = layers.Values.Max();

        // レイヤごとに、現在の座標順を初期並びとして採用する（手で並べた感じを保つ）。
        var columns = new List<List<TodoNode>>(maxLayer + 1);
        for (var i = 0; i <= maxLayer; i++)
        {
            columns.Add([]);
        }

        foreach (var node in graph.Nodes.OrderBy(n => Cross(n, options)).ThenBy(n => n.CreatedAt))
        {
            columns[layers[node.Id]].Add(node);
        }

        ReduceCrossings(graph, columns, options.CrossingSweeps);
        AssignCoordinates(columns, options);
    }

    private static void ReduceCrossings(TodoGraph graph, List<List<TodoNode>> columns, int sweeps)
    {
        var index = new Dictionary<Guid, int>();

        void Reindex()
        {
            index.Clear();
            foreach (var column in columns)
            {
                for (var i = 0; i < column.Count; i++)
                {
                    index[column[i].Id] = i;
                }
            }
        }

        double Barycenter(TodoNode node, bool useParents, int fallback)
        {
            var neighbours = useParents
                ? graph.IncomingOf(node.Id).Select(e => e.FromId)
                : graph.OutgoingOf(node.Id).Select(e => e.ToId);

            var positions = neighbours
                .Where(index.ContainsKey)
                .Select(id => (double)index[id])
                .ToList();

            return positions.Count == 0 ? fallback : positions.Average();
        }

        for (var sweep = 0; sweep < sweeps; sweep++)
        {
            Reindex();
            var forward = sweep % 2 == 0;

            if (forward)
            {
                for (var i = 1; i < columns.Count; i++)
                {
                    SortColumn(columns[i], node => Barycenter(node, useParents: true, columns[i].IndexOf(node)));
                    Reindex();
                }
            }
            else
            {
                for (var i = columns.Count - 2; i >= 0; i--)
                {
                    SortColumn(columns[i], node => Barycenter(node, useParents: false, columns[i].IndexOf(node)));
                    Reindex();
                }
            }
        }
    }

    private static void SortColumn(List<TodoNode> column, Func<TodoNode, double> key)
    {
        var keyed = column.Select((node, i) => (node, key: key(node), i))
            .OrderBy(t => t.key)
            .ThenBy(t => t.i)
            .Select(t => t.node)
            .ToList();

        column.Clear();
        column.AddRange(keyed);
    }

    private static void AssignCoordinates(List<List<TodoNode>> columns, LayoutOptions options)
    {
        var tallest = columns.Count == 0 ? 0 : columns.Max(c => c.Count);
        var span = Math.Max(0, tallest - 1) * options.NodeSpacing;
        var horizontal = options.Direction == LayoutDirection.LeftToRight;

        // 流れる向きの箱の長さと、それに直交する向きの箱の長さ。
        var flowSize = horizontal ? options.NodeWidth : options.NodeHeight;
        var crossSize = horizontal ? options.NodeHeight : options.NodeWidth;

        for (var layer = 0; layer < columns.Count; layer++)
        {
            var column = columns[layer];
            var offset = (span - Math.Max(0, column.Count - 1) * options.NodeSpacing) / 2d;
            var flow = (horizontal ? options.OriginX : options.OriginY) + layer * options.LayerSpacing;

            // 囲みを避けて後ろへずれたぶんを、次のステップにも持ち越す（同じ場所に積み上がらないように）。
            var cursor = double.MinValue;

            for (var i = 0; i < column.Count; i++)
            {
                var node = column[i];
                var slot = (horizontal ? options.OriginY : options.OriginX) + offset + i * options.NodeSpacing;

                if ((options.RespectPinned && node.IsPinned) || options.IsFixed(node.Id))
                {
                    // 動かさないノードでも枠は空けておく（元の並びの見え方を保つ）。
                    cursor = Math.Max(cursor, slot + options.NodeSpacing);
                    continue;
                }

                var cross = Avoid(Math.Max(slot, cursor), flow, flowSize, crossSize, options, horizontal);
                cursor = cross + options.NodeSpacing;

                if (horizontal)
                {
                    node.X = flow;
                    node.Y = cross;
                }
                else
                {
                    node.X = cross;
                    node.Y = flow;
                }
            }
        }
    }

    /// <summary>
    /// 囲みの占有領域に重なっていたら、直交する向きに押し出す。
    /// 押し出した先がまた別の囲みに重なることがあるので、動かなくなるまで繰り返す。
    /// </summary>
    private static double Avoid(
        double cross,
        double flow,
        double flowSize,
        double crossSize,
        LayoutOptions options,
        bool horizontal)
    {
        if (options.Obstacles.Count == 0)
        {
            return cross;
        }

        var gap = Math.Max(16, options.NodeSpacing - crossSize);

        for (var guard = 0; guard < 64; guard++)
        {
            var moved = false;

            foreach (var area in options.Obstacles)
            {
                var areaFlowStart = horizontal ? area.X : area.Y;
                var areaFlowEnd = horizontal ? area.Right : area.Bottom;
                var areaCrossStart = horizontal ? area.Y : area.X;
                var areaCrossEnd = horizontal ? area.Bottom : area.Right;

                if (flow + flowSize <= areaFlowStart || flow >= areaFlowEnd)
                {
                    continue;
                }

                if (cross + crossSize <= areaCrossStart || cross >= areaCrossEnd)
                {
                    continue;
                }

                cross = areaCrossEnd + gap;
                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }

        return cross;
    }

    private static double Cross(TodoNode node, LayoutOptions options) =>
        options.Direction == LayoutDirection.LeftToRight ? node.Y : node.X;
}
