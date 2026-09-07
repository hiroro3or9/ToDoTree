namespace ToDoTree.Core.Layout;

public enum SnapAnchor { Start, Center, End }
public sealed record SnapTarget(Guid Id, BlockBounds Bounds);
public sealed record AxisSnap(Guid TargetId, SnapAnchor Anchor);
public sealed record SnapState(AxisSnap? X = null, AxisSnap? Y = null);
public sealed record SnapGuide(bool IsVertical, double Coordinate, double From, double To);
public sealed record SnapResult(Vec2 Delta, SnapState State, IReadOnlyList<SnapGuide> Guides);

public sealed record BlockSnapOptions(
    double AcquireDistance = 6,
    double ReleaseDistance = 10,
    double CrossDistance = 160,
    double CrossReleaseDistance = 184);

/// <summary>囲みの初期位置と未補正の移動量から吸着を計算する。モデルは変更しない。</summary>
public static class BlockSnapService
{
    public static SnapResult Compute(BlockBounds start, Vec2 rawDelta,
        IReadOnlyList<SnapTarget> targets, SnapState previous, double zoom,
        bool isBypassed = false, BlockSnapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(previous);
        options ??= new();
        if (!double.IsFinite(zoom) || zoom <= 0 || !Valid(start)
            || !double.IsFinite(rawDelta.X) || !double.IsFinite(rawDelta.Y)
            || targets.Any(t => !Valid(t.Bounds))
            || !double.IsFinite(options.AcquireDistance) || options.AcquireDistance <= 0
            || !double.IsFinite(options.ReleaseDistance) || options.ReleaseDistance < options.AcquireDistance
            || !double.IsFinite(options.CrossDistance) || options.CrossDistance < 0
            || !double.IsFinite(options.CrossReleaseDistance) || options.CrossReleaseDistance < options.CrossDistance)
            throw new ArgumentOutOfRangeException(nameof(zoom), "吸着には有限の座標・寸法と有効な距離設定が必要です。");

        var raw = start with { X = start.X + rawDelta.X, Y = start.Y + rawDelta.Y };
        if (!Valid(raw)) throw new ArgumentOutOfRangeException(nameof(rawDelta));
        if (isBypassed) return new(rawDelta, new(), []);

        var x = Select(true, previous.X);
        var y = Select(false, previous.Y);
        var dx = x is null ? rawDelta.X : Anchor(Target(x), x.Anchor, true) - Anchor(start, x.Anchor, true);
        var dy = y is null ? rawDelta.Y : Anchor(Target(y), y.Anchor, false) - Anchor(start, y.Anchor, false);
        var moved = start with { X = start.X + dx, Y = start.Y + dy };
        var guides = new List<SnapGuide>(2);
        if (x is not null)
        {
            var target = Target(x);
            guides.Add(new(true, Anchor(target, x.Anchor, true), Math.Min(moved.Y, target.Y), Math.Max(moved.Bottom, target.Bottom)));
        }
        if (y is not null)
        {
            var target = Target(y);
            guides.Add(new(false, Anchor(target, y.Anchor, false), Math.Min(moved.X, target.X), Math.Max(moved.Right, target.Right)));
        }
        return new(new(dx, dy), new(x, y), guides);

        BlockBounds Target(AxisSnap state) => targets.First(t => t.Id == state.TargetId).Bounds;

        AxisSnap? Select(bool horizontal, AxisSnap? held)
        {
            if (held is not null)
            {
                var target = targets.FirstOrDefault(t => t.Id == held.TargetId);
                if (target is not null
                    && Math.Abs(Anchor(target.Bounds, held.Anchor, horizontal) - Anchor(raw, held.Anchor, horizontal)) * zoom <= options.ReleaseDistance
                    && Gap(raw, target.Bounds, horizontal) * zoom <= options.CrossReleaseDistance)
                    return held;
                // 解放した更新では別候補へ飛ばない。
                return null;
            }

            var candidates = new List<(AxisSnap State, double Distance, double Cross)>();
            foreach (var target in targets)
            {
                var cross = Gap(raw, target.Bounds, horizontal) * zoom;
                if (cross > options.CrossDistance) continue;
                foreach (var anchor in Enum.GetValues<SnapAnchor>())
                {
                    var distance = Math.Abs(Anchor(target.Bounds, anchor, horizontal) - Anchor(raw, anchor, horizontal)) * zoom;
                    if (distance <= options.AcquireDistance)
                        candidates.Add((new(target.Id, anchor), distance, cross));
                }
            }
            if (candidates.Count == 0) return null;
            // 最小値からの許容幅で段階的に絞る。曖昧な比較関数をSortに渡さない。
            var minimum = candidates.Min(c => c.Distance);
            var nearest = candidates.Where(c => c.Distance <= minimum + 0.01).ToList();
            var crossMinimum = nearest.Min(c => c.Cross);
            return nearest.Where(c => c.Cross <= crossMinimum + 0.01)
                .OrderBy(c => c.State.Anchor).ThenBy(c => c.State.TargetId).First().State;
        }
    }

    private static double Anchor(BlockBounds b, SnapAnchor anchor, bool horizontal)
    {
        var start = horizontal ? b.X : b.Y;
        var size = horizontal ? b.Width : b.Height;
        return start + size * ((int)anchor / 2d);
    }

    private static double Gap(BlockBounds a, BlockBounds b, bool horizontal) => horizontal
        ? Math.Max(0, Math.Max(a.Y - b.Bottom, b.Y - a.Bottom))
        : Math.Max(0, Math.Max(a.X - b.Right, b.X - a.Right));

    private static bool Valid(BlockBounds b) => double.IsFinite(b.X) && double.IsFinite(b.Y)
        && double.IsFinite(b.Width) && double.IsFinite(b.Height) && b.Width > 0 && b.Height > 0
        && double.IsFinite(b.Right) && double.IsFinite(b.Bottom);
}
