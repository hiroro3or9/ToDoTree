using ToDoTree.Core.Models;

namespace ToDoTree.Core.Layout;

/// <summary>通常は曲線を使い、カードにぶつかる場合だけ空いている水平・垂直の通路を探す。</summary>
public static class EdgeRouting
{
    public static IReadOnlyList<Vec2> Route(Vec2 from, Vec2 to, double width, double height,
        IReadOnlyList<Vec2> obstacles, ConnectionSide fromSide = ConnectionSide.Auto,
        ConnectionSide toSide = ConnectionSide.Auto, IReadOnlyList<Vec2>? waypoints = null, IReadOnlyList<bool>? smoothWaypoints = null)
    {
        var (start, end, c1, c2) = CurveGeometry.BetweenNodes(from, to, width, height, fromSide, toSide);
        var original = Enumerable.Range(0, 65)
            .Select(i => CurveGeometry.PointOnCurve(start, c1, c2, end, i / 64d)).ToArray();
        var boxes = obstacles.Select(p => new Box(p.X - 12, p.Y - 12, p.X + width + 12, p.Y + height + 12)).ToList();
        // 接続元と接続先も障害物とする。端点が境界に触れることは許す。
        boxes.Add(new Box(from.X, from.Y, from.X + width, from.Y + height));
        boxes.Add(new Box(to.X, to.Y, to.X + width, to.Y + height));


        var a = start + (c1 - start) * (24 / (c1 - start).Length);
        var b = end + (c2 - end) * (24 / (c2 - end).Length);
        if (waypoints is not { Count: > 0 })
            return FindDetour(start, end, a, b, boxes, original);

        // 区間ごとに迂回することで、指定された点を順番どおり正確に通る。
        Vec2[] knots = [start, .. waypoints, end];
        List<Vec2> result = [start];
        var previous = start;
        for (var i = 0; i <= waypoints.Count; i++)
        {
            var next = i < waypoints.Count ? waypoints[i] : end;
            var departure = i == 0 ? a : previous;
            var arrival = i == waypoints.Count ? b : next;
            Vec2[] direct = [previous, departure, arrival, next];
            var leg = FindDetour(previous, next, departure, arrival, boxes, direct);
            bool IsSmooth(int knot) => knot > 0 && knot <= waypoints.Count
                && smoothWaypoints is not null && knot - 1 < smoothWaypoints.Count && smoothWaypoints[knot - 1];
            if (IsSmooth(i) || IsSmooth(i + 1))
            {
                var entry = i == 0 ? Unit(c1 - start)
                    : IsSmooth(i) ? Tangent(knots, i) : Unit(next - previous);
                var exit = i == waypoints.Count ? Unit(end - c2)
                    : IsSmooth(i + 1) ? Tangent(knots, i + 1) : Unit(next - previous);
                var length = (next - previous).Length;
                var entryLength = IsSmooth(i) ? Math.Min(length, (previous - knots[i - 1]).Length) : length;
                var exitLength = IsSmooth(i + 1) ? Math.Min(length, (knots[i + 2] - next).Length) : length;
                leg = SmoothLeg(previous, next, entry, exit, entryLength, exitLength, boxes) ?? leg;
            }
            foreach (var point in leg.Skip(1))
                if ((point - result[^1]).Length > 0.001) result.Add(point);
            previous = next;
        }
        if (result.Count == 1) result.Add(end);
        return result;
    }

    private static Vec2 Unit(Vec2 vector) => vector.Length > 0.001 ? vector * (1 / vector.Length) : new Vec2(0, 0);

    private static Vec2 Tangent(IReadOnlyList<Vec2> knots, int index) =>
        Unit(Unit(knots[index] - knots[index - 1]) + Unit(knots[index + 1] - knots[index]));

    // 局所的に角を削るのではなく、隣の通過点またはカードまでを1本のベジェでつなぐ。
    // 共通接線で指定点を通り、カードの出入りも指定された辺の向きに揃える。
    private static IReadOnlyList<Vec2>? SmoothLeg(Vec2 start, Vec2 end, Vec2 entry, Vec2 exit,
        double entryLength, double exitLength, List<Box> boxes)
    {
        if (entry.Length < 0.001 || exit.Length < 0.001 || (end - start).Length < 0.001) return null;
        var factor = 0.4;
        for (var attempt = 0; attempt < 6; attempt++, factor *= 0.6)
        {
            var c1 = start + entry * (entryLength * factor);
            var c2 = end - exit * (exitLength * factor);
            var samples = Math.Clamp((int)Math.Ceiling(((c1 - start).Length + (c2 - c1).Length + (end - c2).Length) / 6), 64, 512);
            var curve = Enumerable.Range(0, samples + 1)
                .Select(i => CurveGeometry.PointOnCurve(start, c1, c2, end, (double)i / samples)).ToArray();
            if (Clear(curve, boxes)) return curve;
        }
        // 曲線ではカードを避けられない区間は、先に求めた迂回路を使う。
        return null;
    }
    private static IReadOnlyList<Vec2> FindDetour(Vec2 start, Vec2 end, Vec2 a, Vec2 b,
        List<Box> boxes, IReadOnlyList<Vec2> original)
    {
        if (Clear(original, boxes)) return original;
        Vec2[]? best = null;
        var bestLength = double.PositiveInfinity;
        void Consider(params Vec2[] points)
        {
            var path = points.Where((p, i) => i == 0 || (p - points[i - 1]).Length > 0.001).ToArray();
            var length = path.Zip(path.Skip(1), (p, q) => (q - p).Length).Sum();
            if (length >= bestLength || !Clear(path, boxes)) return;
            best = path;
            bestLength = length;
        }
        Consider(start, a, new Vec2(b.X, a.Y), b, end);
        Consider(start, a, new Vec2(a.X, b.Y), b, end);
        foreach (var y in boxes.SelectMany(r => new[] { r.Top - 8, r.Bottom + 8 }).Distinct().Order())
            Consider(start, a, new Vec2(a.X, y), new Vec2(b.X, y), b, end);
        foreach (var x in boxes.SelectMany(r => new[] { r.Left - 8, r.Right + 8 }).Distinct().Order())
            Consider(start, a, new Vec2(x, a.Y), new Vec2(x, b.Y), b, end);
        // 重なったカードなど、通路がない場合も接続自体を消さない。
        return best ?? SearchCorridor(start, end, a, b, boxes) ?? original;
    }

    // 単純な迂回が塞がれているときだけ、矩形の縁で作った通路をA*で探索する。
    // 曲がり角にもコストを付け、短くても細かく折れ続ける経路を避ける。
    private static IReadOnlyList<Vec2>? SearchCorridor(Vec2 start, Vec2 end, Vec2 a, Vec2 b, List<Box> boxes)
    {
        if (!Clear([start, a], boxes) || !Clear([b, end], boxes)) return null;
        if (boxes.Any(r => a.X > r.Left && a.X < r.Right && a.Y > r.Top && a.Y < r.Bottom
            || b.X > r.Left && b.X < r.Right && b.Y > r.Top && b.Y < r.Bottom)) return null;
        var xs = boxes.SelectMany(r => new[] { r.Left - 8, r.Right + 8 }).Append(a.X).Append(b.X).Distinct().Order().ToArray();
        var ys = boxes.SelectMany(r => new[] { r.Top - 8, r.Bottom + 8 }).Append(a.Y).Append(b.Y).Distinct().Order().ToArray();
        var source = new RouteState(Array.BinarySearch(xs, a.X), Array.BinarySearch(ys, a.Y), 2);
        var targetX = Array.BinarySearch(xs, b.X);
        var targetY = Array.BinarySearch(ys, b.Y);
        var costs = new Dictionary<RouteState, double> { [source] = 0 };
        var parents = new Dictionary<RouteState, RouteState>();
        var queue = new PriorityQueue<(RouteState State, double Cost), (double Estimate, int Order)>();
        var order = 0;
        double Estimate(RouteState state) => Math.Abs(xs[state.X] - b.X) + Math.Abs(ys[state.Y] - b.Y);
        queue.Enqueue((source, 0), (Estimate(source), order++));
        // 大きなグラフや通路がない場合でもドラッグ操作を長時間止めない。
        var remaining = 16000;
        while (queue.TryDequeue(out var item, out _) && remaining-- > 0)
        {
            var state = item.State;
            if (item.Cost > costs[state]) continue;
            if (state.X == targetX && state.Y == targetY)
            {
                List<Vec2> path = [b];
                while (parents.TryGetValue(state, out var parent))
                {
                    state = parent;
                    path.Add(new Vec2(xs[state.X], ys[state.Y]));
                }
                path.Reverse();
                path.Insert(0, start);
                path.Add(end);
                List<Vec2> simplified = [];
                foreach (var point in path)
                {
                    if (simplified.Count > 0 && (point - simplified[^1]).Length < 0.001) continue;
                    if (simplified.Count > 1)
                    {
                        var u = simplified[^1] - simplified[^2];
                        var v = point - simplified[^1];
                        if (Math.Abs(u.X * v.Y - u.Y * v.X) < 0.001 && u.X * v.X + u.Y * v.Y > 0)
                            simplified.RemoveAt(simplified.Count - 1);
                    }
                    simplified.Add(point);
                }
                return simplified.Count >= 2 ? simplified : null;
            }
            foreach (var (dx, dy, axis) in new[] { (-1, 0, 0), (1, 0, 0), (0, -1, 1), (0, 1, 1) })
            {
                var x = state.X + dx;
                var y = state.Y + dy;
                if (x < 0 || x >= xs.Length || y < 0 || y >= ys.Length) continue;
                var current = new Vec2(xs[state.X], ys[state.Y]);
                var next = new Vec2(xs[x], ys[y]);
                if (boxes.Any(r => Intersects(current, next, r))) continue;
                var candidate = new RouteState(x, y, axis);
                var cost = item.Cost + (next - current).Length + (state.Axis != 2 && state.Axis != axis ? 24 : 0);
                if (costs.TryGetValue(candidate, out var known) && known <= cost) continue;
                costs[candidate] = cost;
                parents[candidate] = state;
                queue.Enqueue((candidate, cost), (cost + Estimate(candidate), order++));
            }
        }
        return null;
    }

    private readonly record struct RouteState(int X, int Y, int Axis);
    public static double Distance(Vec2 point, IReadOnlyList<Vec2> route)
    {
        var distance = double.PositiveInfinity;
        for (var i = 1; i < route.Count; i++)
            distance = Math.Min(distance, CurveGeometry.DistanceToSegment(point, route[i - 1], route[i]));
        return distance;
    }

    private static bool Clear(IReadOnlyList<Vec2> points, List<Box> boxes)
    {
        for (var i = 1; i < points.Count; i++)
            foreach (var box in boxes)
                if (Intersects(points[i - 1], points[i], box)) return false;
        return true;
    }

    // 線分と矩形の内側の交差。サンプル間にある小さな障害物も見落とさない。
    private static bool Intersects(Vec2 a, Vec2 b, Box box)
    {
        var low = 0d;
        var high = 1d;
        bool Clip(double origin, double delta, double min, double max)
        {
            min += 0.001;
            max -= 0.001;
            if (Math.Abs(delta) < 1e-9) return origin > min && origin < max;
            var t1 = (min - origin) / delta;
            var t2 = (max - origin) / delta;
            low = Math.Max(low, Math.Min(t1, t2));
            high = Math.Min(high, Math.Max(t1, t2));
            return low < high;
        }
        return Clip(a.X, b.X - a.X, box.Left, box.Right)
            && Clip(a.Y, b.Y - a.Y, box.Top, box.Bottom);
    }

    private readonly record struct Box(double Left, double Top, double Right, double Bottom);
}
