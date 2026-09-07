using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public class CorridorRoutingTests
{
    [Test]
    public async Task BoxedInDeparture_FindsMultiBendCorridor()
    {
        Vec2[] obstacles = [new(200, -100), new(200, 100), new(500, 0)];
        var route = EdgeRouting.Route(new(0, 0), new(1200, 0), 224, 88, obstacles,
            ConnectionSide.Right, ConnectionSide.Left);
        await Assert.That(route.Count < 65).IsTrue();
        await Assert.That(route.Any(p => p.X > 436 && p.X < 488 && (p.Y < -12 || p.Y > 100))).IsTrue();
        await CheckClear(route, obstacles);
        await Assert.That(route.SequenceEqual(EdgeRouting.Route(new(0, 0), new(1200, 0), 224, 88,
            [.. obstacles.Reverse()], ConnectionSide.Right, ConnectionSide.Left))).IsTrue();
        await Assert.That(route[1].X > route[0].X && route[1].Y == route[0].Y).IsTrue();
        await Assert.That(route[^2].X < route[^1].X && route[^2].Y == route[^1].Y).IsTrue();
    }

    [Test]
    public async Task Corridor_WithWaypoint_PreservesManualPosition()
    {
        Vec2[] obstacles = [new(200, -100), new(200, 100), new(500, 0)];
        var waypoint = new Vec2(1000, 44);
        var route = EdgeRouting.Route(new(0, 0), new(1200, 0), 224, 88, obstacles,
            ConnectionSide.Right, ConnectionSide.Left, [waypoint], [true]);
        await Assert.That(route.Contains(waypoint)).IsTrue();
        await CheckClear(route, obstacles);
        await Assert.That(EdgeRouting.Distance(waypoint, route)).IsEqualTo(0d);
    }

    private static async Task CheckClear(IReadOnlyList<Vec2> route, Vec2[] obstacles)
    {
        for (var i = 1; i < route.Count; i++)
            for (var step = 0; step <= 100; step++)
            {
                var p = route[i - 1] + (route[i] - route[i - 1]) * (step / 100d);
                foreach (var obstacle in obstacles)
                    await Assert.That(p.X > obstacle.X - 11 && p.X < obstacle.X + 235
                        && p.Y > obstacle.Y - 11 && p.Y < obstacle.Y + 99).IsFalse();
            }
    }
}
