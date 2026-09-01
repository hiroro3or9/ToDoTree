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

        for (var layer = 0; layer < columns.Count; layer++)
        {
            var column = columns[layer];
            var offset = (span - Math.Max(0, column.Count - 1) * options.NodeSpacing) / 2d;

            for (var i = 0; i < column.Count; i++)
            {
                var node = column[i];
                if (options.RespectPinned && node.IsPinned)
                {
                    continue;
                }

                if (options.Direction == LayoutDirection.LeftToRight)
                {
                    node.X = options.OriginX + layer * options.LayerSpacing;
                    node.Y = options.OriginY + offset + i * options.NodeSpacing;
                }
                else
                {
                    node.X = options.OriginX + offset + i * options.NodeSpacing;
                    node.Y = options.OriginY + layer * options.LayerSpacing;
                }
            }
        }
    }

    private static double Cross(TodoNode node, LayoutOptions options) =>
        options.Direction == LayoutDirection.LeftToRight ? node.Y : node.X;
}
