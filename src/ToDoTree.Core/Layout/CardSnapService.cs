namespace ToDoTree.Core.Layout;

/// <summary>吸着先になりうる囲み。所属ノードは、動かす群と重なるかの判定にだけ使う。</summary>
public sealed record BlockSnapCandidate(Guid Id, BlockBounds Bounds, IReadOnlyList<Guid> NodeIds);

/// <summary>
/// カードを動かすときの、吸着の下ごしらえ。
///
/// 「何を動かすか」（移動群の外接矩形）と「何に揃えるか」（吸着先）を決めるだけで、
/// 状態を持たない。吸着の判定そのものは <see cref="BlockSnapService"/> が行う。
/// 複数選択でも外接矩形をひとつ作り、そこへ差分をまとめて当てることで、
/// カード同士の相対配置を崩さない。
/// </summary>
public static class CardSnapService
{
    /// <summary>動かすカードの外接矩形。使える矩形が 1 件も無ければ null。</summary>
    public static BlockBounds? MovingBounds(IReadOnlyList<NodeRect> moving)
    {
        ArgumentNullException.ThrowIfNull(moving);

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var count = 0;

        foreach (var rect in moving)
        {
            if (!Valid(rect))
            {
                continue;
            }

            minX = Math.Min(minX, rect.X);
            minY = Math.Min(minY, rect.Y);
            maxX = Math.Max(maxX, rect.Right);
            maxY = Math.Max(maxY, rect.Bottom);
            count++;
        }

        return count == 0 ? null : new BlockBounds(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// 揃え先の一覧。動かすカード自身と、そのカードを含む囲みは外す。
    /// 囲みはカードが動けば一緒に動くので、揃え先にすると自分を追いかけることになる。
    /// 表示範囲と交差しないものも外し、画面の外の偶然の一致で引っ張られないようにする。
    /// </summary>
    public static IReadOnlyList<SnapTarget> Targets(
        IReadOnlyList<NodeRect> cards,
        IReadOnlyList<BlockSnapCandidate> blocks,
        IReadOnlySet<Guid> moving,
        BlockBounds viewport)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(moving);

        // 表示範囲が取れていないうちは、揃え先を作らない（吸着しないだけで害はない）。
        if (!Valid(viewport))
        {
            return [];
        }

        var targets = new List<SnapTarget>(cards.Count + blocks.Count);

        foreach (var card in cards)
        {
            if (moving.Contains(card.Id) || !Valid(card))
            {
                continue;
            }

            var bounds = new BlockBounds(card.X, card.Y, card.Width, card.Height);
            if (bounds.IntersectsWith(viewport))
            {
                targets.Add(new SnapTarget(card.Id, bounds));
            }
        }

        foreach (var block in blocks)
        {
            if (!Valid(block.Bounds) || block.NodeIds.Any(moving.Contains))
            {
                continue;
            }

            if (block.Bounds.IntersectsWith(viewport))
            {
                targets.Add(new SnapTarget(block.Id, block.Bounds));
            }
        }

        return targets;
    }

    private static bool Valid(NodeRect r) => double.IsFinite(r.X) && double.IsFinite(r.Y)
        && double.IsFinite(r.Width) && double.IsFinite(r.Height) && r.Width > 0 && r.Height > 0;

    private static bool Valid(BlockBounds b) => double.IsFinite(b.X) && double.IsFinite(b.Y)
        && double.IsFinite(b.Width) && double.IsFinite(b.Height) && b.Width > 0 && b.Height > 0;
}
