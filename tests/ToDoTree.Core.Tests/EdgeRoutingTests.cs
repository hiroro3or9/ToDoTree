using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public class EdgeRoutingTests
{
    [Test]
    public async Task ClearRoute_KeepsOriginalCurve()
    {
        var from = new Vec2(0, 0);
        var to = new Vec2(500, 100);
        var route = EdgeRouting.Route(from, to, 224, 88, []);
        var (Start, End, Control1, Control2) = CurveGeometry.BetweenNodes(from, to, 224, 88);
        await Assert.That(route[32]).IsEqualTo(CurveGeometry.PointOnCurve(
            Start, Control1, Control2, End, 0.5));
    }

    [Test]
    public async Task BlockingCard_IsAvoidedInAllDirections()
    {
        foreach (var size in new[] { new Vec2(224, 88), new Vec2(24, 24) })
            foreach (var target in new[] { new Vec2(800, 0), new Vec2(-800, 0), new Vec2(0, 800), new Vec2(0, -800) })
            {
                var obstacle = target * 0.5;
                var route = EdgeRouting.Route(new Vec2(0, 0), target, size.X, size.Y, [obstacle]);
                await Assert.That(route.Count < 65).IsTrue();
                var (Start, End) = CurveGeometry.Anchors(new Vec2(0, 0), target, size.X, size.Y);
                await Assert.That(route[0]).IsEqualTo(Start);
                await Assert.That(route[^1]).IsEqualTo(End);
                for (var i = 1; i < route.Count; i++)
                    for (var j = 0; j <= 100; j++)
                    {
                        var p = route[i - 1] + (route[i] - route[i - 1]) * (j / 100d);
                        var inside = p.X > obstacle.X - 11 && p.X < obstacle.X + size.X + 11
                            && p.Y > obstacle.Y - 11 && p.Y < obstacle.Y + size.Y + 11;
                        await Assert.That(inside).IsFalse();
                    }
                var middle = (route[2] + route[3]) * 0.5;
                await Assert.That(EdgeRouting.Distance(middle, route) < 0.001).IsTrue();
            }
    }

    [Test]
    public async Task ExplicitPorts_KeepDepartureAndArrivalDirections()
    {
        var route = EdgeRouting.Route(new Vec2(0, 0), new Vec2(800, 0), 224, 88,
            [new Vec2(400, 0)], ConnectionSide.Right, ConnectionSide.Left);
        await Assert.That(route[1].X > route[0].X).IsTrue();
        await Assert.That(route[^2].X < route[^1].X).IsTrue();
        await Assert.That(route[0].Y).IsEqualTo(44d);
        await Assert.That(route[^1].Y).IsEqualTo(44d);
    }

    [Test]
    public async Task MultipleObstacles_UseOutsideLane()
    {
        Vec2[] obstacles = [new(400, 0), new(400, -100), new(400, 100)];
        var route = EdgeRouting.Route(new Vec2(0, 0), new Vec2(800, 0), 224, 88, obstacles);
        await Assert.That(route.Any(p => p.Y < -112 || p.Y > 200)).IsTrue();
        await Assert.That(route.SequenceEqual(EdgeRouting.Route(new Vec2(0, 0), new Vec2(800, 0), 224, 88, obstacles))).IsTrue();
    }
}
