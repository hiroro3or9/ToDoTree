using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Tests;

public static class GeometryTests
{
    public static void Register()
    {
        MiniTest.Case("横に並ぶと右端から左端へ線が出る", () =>
        {
            var (start, end) = CurveGeometry.Anchors(new Vec2(0, 0), new Vec2(400, 0), 210, 78);
            MiniTest.Equal(210d, start.X, "始点は右端");
            MiniTest.Equal(39d, start.Y, "始点は縦中央");
            MiniTest.Equal(400d, end.X, "終点は左端");
        });

        MiniTest.Case("縦に並ぶと下端から上端へ線が出る", () =>
        {
            var (start, end) = CurveGeometry.Anchors(new Vec2(0, 0), new Vec2(0, 400), 210, 78);
            MiniTest.Equal(105d, start.X, "始点は横中央");
            MiniTest.Equal(78d, start.Y, "始点は下端");
            MiniTest.Equal(400d, end.Y, "終点は上端");
        });

        MiniTest.Case("左向きでも辺の選び方が反転する", () =>
        {
            var (start, end) = CurveGeometry.Anchors(new Vec2(400, 0), new Vec2(0, 0), 210, 78);
            MiniTest.Equal(400d, start.X, "始点は左端");
            MiniTest.Equal(210d, end.X, "終点は右端");
        });

        MiniTest.Case("曲線は端点を通る", () =>
        {
            var start = new Vec2(0, 0);
            var end = new Vec2(300, 100);
            var (c1, c2) = CurveGeometry.ControlPoints(start, end);

            var head = CurveGeometry.PointOnCurve(start, c1, c2, end, 0);
            var tail = CurveGeometry.PointOnCurve(start, c1, c2, end, 1);

            MiniTest.True((head - start).Length < 0.001, "t=0 が始点");
            MiniTest.True((tail - end).Length < 0.001, "t=1 が終点");
        });

        MiniTest.Case("曲線上の点は距離がほぼ 0", () =>
        {
            var start = new Vec2(0, 0);
            var end = new Vec2(300, 120);
            var (c1, c2) = CurveGeometry.ControlPoints(start, end);
            var on = CurveGeometry.PointOnCurve(start, c1, c2, end, 0.37);

            MiniTest.True(CurveGeometry.DistanceToCurve(on, start, c1, c2, end) < 1.0, "曲線の上");
        });

        MiniTest.Case("離れた点は距離が大きい", () =>
        {
            var start = new Vec2(0, 0);
            var end = new Vec2(300, 0);
            var (c1, c2) = CurveGeometry.ControlPoints(start, end);

            MiniTest.True(CurveGeometry.DistanceToCurve(new Vec2(150, 200), start, c1, c2, end) > 100, "遠い点");
            MiniTest.True(CurveGeometry.DistanceToCurve(new Vec2(150, 2), start, c1, c2, end) < 6, "線の近く");
        });

        MiniTest.Case("点と線分の距離は端点でも正しい", () =>
        {
            MiniTest.Equal(5d, CurveGeometry.DistanceToSegment(new Vec2(-5, 0), new Vec2(0, 0), new Vec2(10, 0)), "手前");
            MiniTest.Equal(3d, CurveGeometry.DistanceToSegment(new Vec2(5, 3), new Vec2(0, 0), new Vec2(10, 0)), "真横");
            MiniTest.Equal(0d, CurveGeometry.DistanceToSegment(new Vec2(4, 4), new Vec2(4, 4), new Vec2(4, 4)), "長さゼロ");
        });
    }
}
