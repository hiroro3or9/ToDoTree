using ToDoTree.Core.Models;

namespace ToDoTree.Core.Layout;

/// <summary>画面に依存しない矩形。WPF の Rect を Core に持ち込まないための最小の型。</summary>
public readonly record struct NodeRect(Guid Id, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>囲みの外周（見出しの帯を含む）。保存はせず、所属ノードの位置から毎回作り直す。</summary>
public readonly record struct BlockBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>見出しの帯。上端に置き、ここを掴むと囲みごと動く。</summary>
    public BlockBounds Header => new(X, Y, Width, BlockGeometry.HeaderHeight);

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    public bool IntersectsWith(BlockBounds other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>
/// 囲みの見た目の寸法と、まとめて動かす対象の割り出し。
///
/// 境界は保存しない。所属ノードの表示矩形の外接矩形に余白を足しただけの派生値なので、
/// カードを動かせば囲みは勝手に追従し、ずれた状態がファイルに残ることもない。
/// </summary>
public static class BlockGeometry
{
    /// <summary>左右の余白。</summary>
    public const double SidePadding = 24;

    /// <summary>下の余白。</summary>
    public const double BottomPadding = 24;

    /// <summary>見出しの帯の高さ。</summary>
    public const double HeaderHeight = 32;

    /// <summary>見出しの帯と、いちばん上のカードのあいだ。</summary>
    public const double HeaderGap = 12;

    /// <summary>所属ノードの矩形から囲みを作る。1 件も無ければ null（囲みを出さない）。</summary>
    public static BlockBounds? Compute(IReadOnlyList<NodeRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);
        if (rects.Count == 0)
        {
            return null;
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var rect in rects)
        {
            minX = Math.Min(minX, rect.X);
            minY = Math.Min(minY, rect.Y);
            maxX = Math.Max(maxX, rect.Right);
            maxY = Math.Max(maxY, rect.Bottom);
        }

        var left = minX - SidePadding;
        var top = minY - HeaderGap - HeaderHeight;

        return new BlockBounds(
            left,
            top,
            (maxX + SidePadding) - left,
            (maxY + BottomPadding) - top);
    }

    /// <summary>
    /// 見出しをドラッグしたときに、通過点まで一緒に動かす辺。
    /// 両端とも移動対象の辺だけが対象で、片端だけの辺は座標を保って接続部分だけが引き直される。
    /// </summary>
    public static IReadOnlyList<TodoEdge> InternalEdges(TodoProject project, IReadOnlySet<Guid> movingNodeIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(movingNodeIds);

        return [.. project.Edges.Where(e =>
            e.Waypoints.Count > 0 && movingNodeIds.Contains(e.FromId) && movingNodeIds.Contains(e.ToId))];
    }
}
