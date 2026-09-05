using ToDoTree.Core.Models;

namespace ToDoTree.Core.Layout;

/// <summary>
/// ステップ同士を結ぶ曲線の形。描画とクリック判定で同じ計算を使うために、ここに集めてある。
/// </summary>
public static class CurveGeometry
{
    /// <summary>
    /// 2 枚のカードの位置関係から、線がどの辺から出てどの辺に入るかを決める。
    /// 横に並んでいれば左右、縦に並んでいれば上下から出るので、どちら向きのレイアウトでも自然に見える。
    /// </summary>
    public static (Vec2 Start, Vec2 End) Anchors(Vec2 from, Vec2 to, double width, double height)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return dx >= 0
                ? (new Vec2(from.X + width, from.Y + (height / 2)), new Vec2(to.X, to.Y + (height / 2)))
                : (new Vec2(from.X, from.Y + (height / 2)), new Vec2(to.X + width, to.Y + (height / 2)));
        }

        return dy >= 0
            ? (new Vec2(from.X + (width / 2), from.Y + height), new Vec2(to.X + (width / 2), to.Y))
            : (new Vec2(from.X + (width / 2), from.Y), new Vec2(to.X + (width / 2), to.Y + height));
    }

    /// <summary>接続辺と接線の向きを同じ位置関係から決める。描画・クリック判定で共有する。</summary>
    public static (Vec2 Start, Vec2 End, Vec2 Control1, Vec2 Control2) BetweenNodes(
        Vec2 from, Vec2 to, double width, double height, ConnectionSide fromSide = ConnectionSide.Auto, ConnectionSide toSide = ConnectionSide.Auto)
    {
        var (start, end) = Anchors(from, to, width, height);
        start = fromSide switch
        {
            ConnectionSide.Top => new Vec2(from.X + width / 2, from.Y),
            ConnectionSide.Bottom => new Vec2(from.X + width / 2, from.Y + height),
            ConnectionSide.Left => new Vec2(from.X, from.Y + height / 2),
            ConnectionSide.Right => new Vec2(from.X + width, from.Y + height / 2),
            _ => start,
        };
        end = toSide switch
        {
            ConnectionSide.Top => new Vec2(to.X + width / 2, to.Y),
            ConnectionSide.Bottom => new Vec2(to.X + width / 2, to.Y + height),
            ConnectionSide.Left => new Vec2(to.X, to.Y + height / 2),
            ConnectionSide.Right => new Vec2(to.X + width, to.Y + height / 2),
            _ => end,
        };
        var (control1, control2) = ControlPoints(start, end, to - from);
        control1 = SourceControlPoint(start, end, control1, fromSide);
        control2 = SourceControlPoint(end, start, control2, toSide);
        return (start, end, control1, control2);
    }
    /// <summary>ベジェ曲線の制御点。始点と終点の向きに沿って膨らませる。</summary>
    public static (Vec2 Control1, Vec2 Control2) ControlPoints(Vec2 start, Vec2 end, Vec2? direction = null)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        var flow = direction ?? new Vec2(dx, dy);
        if (Math.Abs(flow.X) >= Math.Abs(flow.Y))
        {
            var offset = Math.Max(48, Math.Abs(dx) * 0.45) * (flow.X < 0 ? -1 : 1);
            return (new Vec2(start.X + offset, start.Y), new Vec2(end.X - offset, end.Y));
        }

        var vertical = Math.Max(48, Math.Abs(dy) * 0.45) * (flow.Y < 0 ? -1 : 1);
        return (new Vec2(start.X, start.Y + vertical), new Vec2(end.X, end.Y - vertical));
    }

    /// <summary>明示した接続辺の外向きに接線を揃える。プレビューでも使う。</summary>
    public static Vec2 SourceControlPoint(Vec2 start, Vec2 end, Vec2 automatic, ConnectionSide side)
    {
        var offset = Math.Max(48, Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)) * 0.45);
        return side switch
        {
            ConnectionSide.Top => new Vec2(start.X, start.Y - offset),
            ConnectionSide.Bottom => new Vec2(start.X, start.Y + offset),
            ConnectionSide.Left => new Vec2(start.X - offset, start.Y),
            ConnectionSide.Right => new Vec2(start.X + offset, start.Y),
            _ => automatic,
        };
    }
    public static Vec2 PointOnCurve(Vec2 start, Vec2 control1, Vec2 control2, Vec2 end, double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;

        return new Vec2(
            (start.X * a) + (control1.X * b) + (control2.X * c) + (end.X * d),
            (start.Y * a) + (control1.Y * b) + (control2.Y * c) + (end.Y * d));
    }

    /// <summary>点から曲線までの距離。曲線を細かい折れ線に割って測る（クリック判定用）。</summary>
    public static double DistanceToCurve(
        Vec2 point,
        Vec2 start,
        Vec2 control1,
        Vec2 control2,
        Vec2 end,
        int samples = 32)
    {
        var steps = Math.Max(4, samples);
        var best = double.MaxValue;
        var previous = start;

        for (var i = 1; i <= steps; i++)
        {
            var current = PointOnCurve(start, control1, control2, end, (double)i / steps);
            best = Math.Min(best, DistanceToSegment(point, previous, current));
            previous = current;
        }

        return best;
    }

    /// <summary>点と線分の距離。</summary>
    public static double DistanceToSegment(Vec2 point, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);

        if (lengthSquared < 1e-9)
        {
            return (point - a).Length;
        }

        var ap = point - a;
        var t = Math.Clamp(((ap.X * ab.X) + (ap.Y * ab.Y)) / lengthSquared, 0, 1);
        return (point - (a + (ab * t))).Length;
    }
}
