using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Layout;

/// <summary>ブロック内の整列を試した結果。</summary>
public enum BlockLayoutStatus
{
    /// <summary>整列できる。<see cref="BlockLayoutResult.Positions"/> をそのまま書き戻してよい。</summary>
    Ready,

    /// <summary>すでに整列されている。座標を書き戻す必要はない。</summary>
    Unchanged,

    /// <summary>ステップが 1 件以下しかない。</summary>
    TooFewNodes,

    /// <summary>位置を固定したステップが混ざっている。</summary>
    ContainsPinnedNodes,

    /// <summary>並べ直すと、外のカードや別の囲みに重なってしまう。</summary>
    OverlapsOutside,

    /// <summary>入力が壊れている（存在しない所属、循環、寸法や座標の異常）。</summary>
    InvalidInput,
}

/// <summary>
/// ブロック内の整列の候補。
/// <see cref="BlockLayoutStatus.Ready"/> 以外では座標を要求しないので、辞書は空になる。
/// </summary>
public sealed record BlockLayoutResult(
    BlockLayoutStatus Status,
    IReadOnlyDictionary<Guid, Vec2> Positions,
    BlockBounds? Bounds)
{
    public bool IsReady => Status == BlockLayoutStatus.Ready;
}

/// <summary>
/// ブロックの中だけを、依存関係に沿って並べ直す。
///
/// 実プロジェクトには一切触れない。所属ノードと、両端が所属ノードの辺だけを写した
/// 一時グラフを整列し、その結果を座標の辞書として返す。書き戻すかどうかは呼ぶ側が決める。
///
/// 外へ出てから戻ってくる筋（内部 A → 外部 X → 内部 B）は内部では繋がない。
/// これは図全体の流れを最適化する機能ではなく、囲みの中の直接の関係を読みやすくする機能なので、
/// 外を含めて考え始めると「中を整えたいだけ」という意図から離れてしまう。
/// </summary>
public static class BlockLayoutService
{
    /// <summary>これ以下の差は「動いていない」とみなす（DIP）。</summary>
    public const double Tolerance = 0.01;

    public static BlockLayoutResult Compute(TodoProject project, Guid blockId, LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        if (!IsPositive(options.NodeWidth) || !IsPositive(options.NodeHeight)
            || !IsPositive(options.LayerSpacing) || !IsPositive(options.NodeSpacing))
        {
            return Failed(BlockLayoutStatus.InvalidInput);
        }

        if (project.Blocks.FirstOrDefault(b => b.Id == blockId) is not { } block)
        {
            return Failed(BlockLayoutStatus.InvalidInput);
        }

        var byId = new Dictionary<Guid, TodoNode>(project.Nodes.Count);
        foreach (var node in project.Nodes)
        {
            byId[node.Id] = node;
        }

        var members = new List<TodoNode>(block.NodeIds.Count);
        var memberIds = new HashSet<Guid>();

        foreach (var id in block.NodeIds)
        {
            if (!byId.TryGetValue(id, out var node) || !memberIds.Add(id))
            {
                return Failed(BlockLayoutStatus.InvalidInput);
            }

            if (!double.IsFinite(node.X) || !double.IsFinite(node.Y))
            {
                return Failed(BlockLayoutStatus.InvalidInput);
            }

            members.Add(node);
        }

        if (members.Count < 2)
        {
            return Failed(BlockLayoutStatus.TooFewNodes);
        }

        // 固定を一時的に外して並べると、ユーザーが留めた意図を黙って壊すことになる。
        // 固定を残したまま依存関係と衝突を両立させる配置は、初版の範囲を超える。
        if (members.Any(n => n.IsPinned))
        {
            return Failed(BlockLayoutStatus.ContainsPinnedNodes);
        }

        var temp = BuildInnerGraph(members, project, memberIds, options.Direction);
        if (temp.HasCycle())
        {
            return Failed(BlockLayoutStatus.InvalidInput);
        }

        LayeredLayoutEngine.Apply(temp, InnerOptionsFrom(options));

        var before = BoundsOf(members, options);
        var candidate = BoundsOf(temp.Nodes, options);
        if (before is not { } beforeBox || candidate is not { } candidateBox)
        {
            return Failed(BlockLayoutStatus.InvalidInput);
        }

        // 見出しの始点（囲みの左上）をワールド座標で維持する。
        // 中だけを整えたいので、囲み自体が別の場所へ飛ぶと操作の意味が変わってしまう。
        var dx = beforeBox.X - candidateBox.X;
        var dy = beforeBox.Y - candidateBox.Y;

        var positions = new Dictionary<Guid, Vec2>(temp.NodeCount);
        foreach (var node in temp.Nodes)
        {
            var x = node.X + dx;
            var y = node.Y + dy;

            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                return Failed(BlockLayoutStatus.InvalidInput);
            }

            positions[node.Id] = new Vec2(x, y);
        }

        var bounds = new BlockBounds(candidateBox.X + dx, candidateBox.Y + dy, candidateBox.Width, candidateBox.Height);

        // 変わらないなら、履歴も未保存の印も増やさずに終わる。
        // 重なりの判定より先に見る。いまの配置がすでに何かと重なっていても、
        // 「動かないのだから直しようがない」ことを重なりのせいにしないため。
        if (members.All(n => positions.TryGetValue(n.Id, out var p)
                             && Math.Abs(p.X - n.X) <= Tolerance
                             && Math.Abs(p.Y - n.Y) <= Tolerance))
        {
            return Failed(BlockLayoutStatus.Unchanged);
        }

        if (HasInnerCollision(positions, options))
        {
            return Failed(BlockLayoutStatus.InvalidInput);
        }

        if (IntersectsOutside(project, blockId, memberIds, byId, bounds, options))
        {
            return Failed(BlockLayoutStatus.OverlapsOutside);
        }

        return new BlockLayoutResult(BlockLayoutStatus.Ready, positions, bounds);
    }

    /// <summary>
    /// 所属ノードと、両端が所属ノードの辺だけを写した一時グラフ。
    ///
    /// 初期の並びは <c>CreatedAt</c> → <c>Id</c> の順で、いまの座標は使わない。
    /// 既存エンジンは現在座標を初期順序に使うので、そのまま渡すと同じ構造でも
    /// 実行のたびに順番が入れ替わり、2 回目が「変化なし」にならない。
    /// </summary>
    private static TodoGraph BuildInnerGraph(
        IReadOnlyList<TodoNode> members,
        TodoProject project,
        IReadOnlySet<Guid> memberIds,
        LayoutDirection direction)
    {
        var ordered = members.OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToList();
        var horizontal = direction == LayoutDirection.LeftToRight;
        var temp = new TodoProject();

        for (var i = 0; i < ordered.Count; i++)
        {
            var clone = ordered[i].Clone();
            clone.IsPinned = false;

            // 順番だけを伝えるための仮置き。値そのものは整列で上書きされる。
            clone.X = horizontal ? 0 : i;
            clone.Y = horizontal ? i : 0;
            temp.Nodes.Add(clone);
        }

        foreach (var edge in project.Edges)
        {
            if (memberIds.Contains(edge.FromId) && memberIds.Contains(edge.ToId))
            {
                temp.Edges.Add(edge.Clone());
            }
        }

        return new TodoGraph(temp);
    }

    /// <summary>渡された設定は変えず、内部整列用の写しを作る。</summary>
    private static LayoutOptions InnerOptionsFrom(LayoutOptions options) => new()
    {
        Direction = options.Direction,
        LayerSpacing = options.LayerSpacing,
        NodeSpacing = options.NodeSpacing,
        CrossingSweeps = options.CrossingSweeps,
        NodeWidth = options.NodeWidth,
        NodeHeight = options.NodeHeight,

        // 原点で組んでから、あとで見出しの位置へ平行移動する。
        OriginX = 0,
        OriginY = 0,

        // 一時固定も障害物も渡さない。外の都合で中の列を不規則に押し出す代わりに、
        // 出来上がった囲み全体を外側と突き合わせて判定する。
        RespectPinned = false,
    };

    private static BlockBounds? BoundsOf(IEnumerable<TodoNode> nodes, LayoutOptions options) =>
        BlockGeometry.Compute(
            [.. nodes.Select(n => new NodeRect(n.Id, n.X, n.Y, options.NodeWidth, options.NodeHeight))]);

    /// <summary>中のカード同士が重なっていないか（エンジンの出力を鵜呑みにしない）。</summary>
    private static bool HasInnerCollision(IReadOnlyDictionary<Guid, Vec2> positions, LayoutOptions options)
    {
        var rects = positions.Values
            .Select(p => new BlockBounds(p.X, p.Y, options.NodeWidth, options.NodeHeight))
            .ToList();

        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                if (rects[i].IntersectsWith(rects[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 並べ直した囲みが、外のカードや別の囲みに重なるか。
    /// 別ブロックは隠れている所属も含めた占有領域で代表させ、そのノードを二重に数えない。
    /// 辺と通過点は含めない（線が囲みを横切るのは許す）。
    /// </summary>
    private static bool IntersectsOutside(
        TodoProject project,
        Guid blockId,
        IReadOnlySet<Guid> memberIds,
        IReadOnlyDictionary<Guid, TodoNode> byId,
        BlockBounds bounds,
        LayoutOptions options)
    {
        var claimed = new HashSet<Guid>(memberIds);

        foreach (var other in project.Blocks)
        {
            if (other.Id == blockId)
            {
                continue;
            }

            var rects = new List<NodeRect>(other.NodeIds.Count);
            foreach (var id in other.NodeIds)
            {
                if (byId.TryGetValue(id, out var node))
                {
                    rects.Add(new NodeRect(node.Id, node.X, node.Y, options.NodeWidth, options.NodeHeight));
                    claimed.Add(id);
                }
            }

            if (BlockGeometry.Compute(rects) is { } area && bounds.IntersectsWith(area))
            {
                return true;
            }
        }

        foreach (var node in project.Nodes)
        {
            if (claimed.Contains(node.Id))
            {
                continue;
            }

            if (bounds.IntersectsWith(new BlockBounds(node.X, node.Y, options.NodeWidth, options.NodeHeight)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;

    private static BlockLayoutResult Failed(BlockLayoutStatus status) =>
        new(status, new Dictionary<Guid, Vec2>(), null);
}
