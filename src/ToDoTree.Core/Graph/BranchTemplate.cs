using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>選択範囲を再利用できる独立したグラフへ変換する。元データは変更しない。</summary>
public static class BranchTemplate
{
    public static TodoProject Capture(TodoProject source, IEnumerable<Guid> selection, string name)
    {
        var ids = selection.ToHashSet();
        var endpoints = ids.Concat(source.Blocks.Where(b => b.NodeIds.Any(ids.Contains)).Select(b => b.Id)).ToHashSet();
        var fragment = new TodoProject
        {
            Name = name.Trim(),
            Nodes = [.. source.Nodes.Where(n => ids.Contains(n.Id)).Select(n => n.Clone())],
            Edges = [.. source.Edges.Where(e => endpoints.Contains(e.FromId) && endpoints.Contains(e.ToId)).Select(e => e.Clone())],
            Blocks = [.. source.Blocks.Where(b => b.NodeIds.Any(ids.Contains)).Select(b => new TodoBlock
            {
                Id = b.Id,
                Title = b.Title,
                ColorId = b.ColorId,
                NodeIds = [.. b.NodeIds.Where(ids.Contains)],
            })],
        };
        Validate(fragment);
        return Instantiate(fragment, 0, 0);
    }

    public static TodoProject Instantiate(TodoProject template, double x, double y)
    {
        Validate(template);
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("配置位置が不正です。");
        var copy = template.DeepClone();
        copy.Id = Guid.NewGuid();
        var map = copy.Nodes.Select(n => n.Id).Concat(copy.Blocks.Select(b => b.Id)).ToDictionary(id => id, _ => Guid.NewGuid());
        var dx = x - copy.Nodes.Min(n => n.X);
        var dy = y - copy.Nodes.Min(n => n.Y);
        var now = DateTimeOffset.Now;
        foreach (var node in copy.Nodes)
        {
            node.Id = map[node.Id];
            node.X += dx; node.Y += dy;
            node.Status = NodeStatus.NotStarted;
            node.Due = null; node.CompletedAt = null;

            // 目標回数は部品の性格なので残し、実績だけ 0 に戻す。
            // 元の実績を持ち込むと、置いた直後から完了済みの項目が現れる。
            if (node.Repeat is { } repeat) repeat.CompletedCount = 0;
            node.IsPinned = false;
            node.CreatedAt = node.UpdatedAt = now;
        }
        foreach (var edge in copy.Edges)
        {
            edge.Id = Guid.NewGuid();
            edge.FromId = map[edge.FromId]; edge.ToId = map[edge.ToId];
            edge.Waypoints = [.. edge.Waypoints.Select(p => new JunctionPoint(p.X + dx, p.Y + dy, p.IsSmooth))];
        }
        foreach (var block in copy.Blocks)
        {
            block.Id = map[block.Id];
            block.IsCollapsed = false;
            block.NodeIds = [.. block.NodeIds.Select(id => map[id])];
        }
        Validate(copy);
        return copy;
    }

    public static void Validate(TodoProject template)
    {
        if (template.Nodes.Any(n => n is null || n.Tags is null)
            || template.Edges.Any(e => e is null) || template.Blocks.Any(b => b is null))
            throw new InvalidDataException("部品の中に空の要素があります。");
        if (template.SchemaVersion != TodoProject.CurrentSchemaVersion)
            throw new InvalidDataException("未対応の部品形式です。");
        if (string.IsNullOrWhiteSpace(template.Name) || template.Nodes.Count == 0)
            throw new InvalidDataException("部品には名前と1件以上のステップが必要です。");
        var ids = template.Nodes.Select(n => n.Id).ToHashSet();
        if (ids.Count != template.Nodes.Count || template.Nodes.Any(n => !double.IsFinite(n.X) || !double.IsFinite(n.Y)))
            throw new InvalidDataException("ステップのIDまたは座標が不正です。");
        ids.UnionWith(template.Blocks.Select(b => b.Id));
        if (template.Edges.Any(e => !ids.Contains(e.FromId) || !ids.Contains(e.ToId)
            || e.Waypoints.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            || template.Edges.Select(e => e.Id).Distinct().Count() != template.Edges.Count
            || template.Edges.Select(e => (e.FromId, e.ToId)).Distinct().Count() != template.Edges.Count)
            throw new InvalidDataException("部品の接続情報が不正です。");
        if (RepeatService.Validate(template) is { } repeatError) throw new InvalidDataException(repeatError);
        if (BlockService.Validate(template) is { } error) throw new InvalidDataException(error);
        if (new TodoGraph(template.DeepClone()).HasCycle()) throw new InvalidDataException("部品の接続が循環しています。");
    }
}
