using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Tests;

public class CurveDirectionTests
{
    [Test]
    public async Task DiagonalConnections_KeepTangentsAlignedWithNodeEdges()
    {
        foreach (var size in new[] { new Vec2(24, 24), new Vec2(224, 88) })
        foreach (var signX in new[] { -1, 1 })
        foreach (var signY in new[] { -1, 1 })
        foreach (var horizontal in new[] { false, true })
        {
            var to = horizontal
                ? new Vec2(320 * signX, 310 * signY)
                : new Vec2(310 * signX, 320 * signY);
            var (start, end, c1, c2) = CurveGeometry.BetweenNodes(new Vec2(0, 0), to, size.X, size.Y);
            if (horizontal)
            {
                await Assert.That(c1.Y).IsEqualTo(start.Y);
                await Assert.That(c2.Y).IsEqualTo(end.Y);
                await Assert.That((end.X - c2.X) * signX > 0).IsTrue();
            }
            else
            {
                await Assert.That(c1.X).IsEqualTo(start.X);
                await Assert.That(c2.X).IsEqualTo(end.X);
                await Assert.That((end.Y - c2.Y) * signY > 0).IsTrue();
            }
            var midpoint = CurveGeometry.PointOnCurve(start, c1, c2, end, 0.5);
            await Assert.That(CurveGeometry.DistanceToCurve(midpoint, start, c1, c2, end) < 0.001).IsTrue();
        }
    }
}