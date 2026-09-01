using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Tests;

public class GeometryTests
{
    [Test]
    [DisplayName("横に並ぶと右端から左端へ線が出る")]
    public async Task Anchors_HorizontalUsesRightAndLeftEdges()
    {
        var (start, end) = CurveGeometry.Anchors(new Vec2(0, 0), new Vec2(400, 0), 210, 78);
        await Assert.That(start.X).IsEqualTo(210d).Because("始点は右端");
        await Assert.That(start.Y).IsEqualTo(39d).Because("始点は縦中央");
        await Assert.That(end.X).IsEqualTo(400d).Because("終点は左端");
    }

    [Test]
    [DisplayName("縦に並ぶと下端から上端へ線が出る")]
    public async Task Anchors_VerticalUsesBottomAndTopEdges()
    {
        var (start, end) = CurveGeometry.Anchors(new Vec2(0, 0), new Vec2(0, 400), 210, 78);
        await Assert.That(start.X).IsEqualTo(105d).Because("始点は横中央");
        await Assert.That(start.Y).IsEqualTo(78d).Because("始点は下端");
        await Assert.That(end.Y).IsEqualTo(400d).Because("終点は上端");
    }

    [Test]
    [DisplayName("左向きでも辺の選び方が反転する")]
    public async Task Anchors_FlipsForRightToLeft()
    {
        var (start, end) = CurveGeometry.Anchors(new Vec2(400, 0), new Vec2(0, 0), 210, 78);
        await Assert.That(start.X).IsEqualTo(400d).Because("始点は左端");
        await Assert.That(end.X).IsEqualTo(210d).Because("終点は右端");
    }

    [Test]
    [DisplayName("曲線は端点を通る")]
    public async Task Curve_PassesThroughEndpoints()
    {
        var start = new Vec2(0, 0);
        var end = new Vec2(300, 100);
        var (c1, c2) = CurveGeometry.ControlPoints(start, end);

        var head = CurveGeometry.PointOnCurve(start, c1, c2, end, 0);
        var tail = CurveGeometry.PointOnCurve(start, c1, c2, end, 1);

        await Assert.That((head - start).Length < 0.001).IsTrue().Because("t=0 が始点");
        await Assert.That((tail - end).Length < 0.001).IsTrue().Because("t=1 が終点");
    }

    [Test]
    [DisplayName("曲線上の点は距離がほぼ 0")]
    public async Task DistanceToCurve_NearZeroOnCurve()
    {
        var start = new Vec2(0, 0);
        var end = new Vec2(300, 120);
        var (c1, c2) = CurveGeometry.ControlPoints(start, end);
        var on = CurveGeometry.PointOnCurve(start, c1, c2, end, 0.37);

        await Assert.That(CurveGeometry.DistanceToCurve(on, start, c1, c2, end) < 1.0).IsTrue().Because("曲線の上");
    }

    [Test]
    [DisplayName("離れた点は距離が大きい")]
    public async Task DistanceToCurve_LargeWhenFarAway()
    {
        var start = new Vec2(0, 0);
        var end = new Vec2(300, 0);
        var (c1, c2) = CurveGeometry.ControlPoints(start, end);

        await Assert.That(CurveGeometry.DistanceToCurve(new Vec2(150, 200), start, c1, c2, end) > 100).IsTrue().Because("遠い点");
        await Assert.That(CurveGeometry.DistanceToCurve(new Vec2(150, 2), start, c1, c2, end) < 6).IsTrue().Because("線の近く");
    }

    [Test]
    [DisplayName("点と線分の距離は端点でも正しい")]
    public async Task DistanceToSegment_CorrectAtEndpoints()
    {
        await Assert.That(CurveGeometry.DistanceToSegment(new Vec2(-5, 0), new Vec2(0, 0), new Vec2(10, 0))).IsEqualTo(5d).Because("手前");
        await Assert.That(CurveGeometry.DistanceToSegment(new Vec2(5, 3), new Vec2(0, 0), new Vec2(10, 0))).IsEqualTo(3d).Because("真横");
        await Assert.That(CurveGeometry.DistanceToSegment(new Vec2(4, 4), new Vec2(4, 4), new Vec2(4, 4))).IsEqualTo(0d).Because("長さゼロ");
    }
}
