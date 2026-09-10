using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Models;

/// <summary>ブロックの辺上に置く接続点。位置は辺の始端からの割合（0～1）。</summary>
public sealed record BlockPort(Guid Id, ConnectionSide Side, double Position)
{
    public double Offset(BlockBounds bounds) =>
        (Position - 0.5) * (Side is ConnectionSide.Top or ConnectionSide.Bottom ? bounds.Width : bounds.Height);

    public Vec2 Resolve(BlockBounds bounds) => EdgeRouting.Port(bounds, Side, Offset(bounds));

    public static BlockPort At(BlockBounds bounds, Vec2 point, Guid? id = null)
    {
        var x = Math.Clamp(point.X, bounds.X, bounds.Right);
        var y = Math.Clamp(point.Y, bounds.Y, bounds.Bottom);
        var candidates = new[]
        {
            (ConnectionSide.Left, new Vec2(bounds.X, y)),
            (ConnectionSide.Right, new Vec2(bounds.Right, y)),
            (ConnectionSide.Top, new Vec2(x, bounds.Y)),
            (ConnectionSide.Bottom, new Vec2(x, bounds.Bottom)),
        };
        var (side, closest) = candidates.MinBy(c => (c.Item2 - point).Length);
        var position = side is ConnectionSide.Top or ConnectionSide.Bottom
            ? (closest.X - bounds.X) / Math.Max(1, bounds.Width)
            : (closest.Y - bounds.Y) / Math.Max(1, bounds.Height);
        return new BlockPort(id ?? Guid.NewGuid(), side, Math.Clamp(position, 0, 1));
    }
}
