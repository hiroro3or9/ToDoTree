using System.Text.Json;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class WaypointTests
{
    [Test]
    public async Task Route_PassesThroughWaypointsInOrder_AndKeepsPorts()
    {
        Vec2[] points = [new(300, -150), new(600, -150)];
        var route = EdgeRouting.Route(new(0, 0), new(800, 0), 224, 88, [new(400, 0)],
            ConnectionSide.Right, ConnectionSide.Left, points);
        var first = route.ToList().IndexOf(points[0]);
        var second = route.ToList().IndexOf(points[1]);
        await Assert.That(first > 0 && second > first).IsTrue();
        await Assert.That(route[1].X > route[0].X).IsTrue();
        await Assert.That(route[^2].X < route[^1].X).IsTrue();
        await Assert.That(EdgeRouting.Distance(points[0], route)).IsEqualTo(0d);
    }

    [Test]
    public async Task WaypointLeg_AvoidsIntermediateCard()
    {
        var waypoint = new Vec2(700, 44);
        var route = EdgeRouting.Route(new(0, 0), new(1000, 0), 224, 88, [new(400, 0)], waypoints: [waypoint]);
        await Assert.That(route.Contains(waypoint)).IsTrue();
        for (var i = 1; i < route.Count; i++)
            for (var j = 0; j <= 100; j++)
            {
                var p = route[i - 1] + (route[i] - route[i - 1]) * (j / 100d);
                await Assert.That(p.X > 389 && p.X < 635 && p.Y > -11 && p.Y < 99).IsFalse();
            }
    }

    [Test]
    public async Task Clone_DoesNotShareWaypointList()
    {
        var edge = new TodoEdge { Waypoints = [new(10, 20)] };
        var copy = edge.Clone();
        edge.Waypoints[0] = new(50, 60);
        edge.Waypoints.Add(new(70, 80));
        await Assert.That(copy.Waypoints.Count).IsEqualTo(1);
        await Assert.That(copy.Waypoints[0]).IsEqualTo(new JunctionPoint(10, 20));
    }

    [Test]
    public async Task Serialization_PreservesPoints_AndLoadsOlderEdges()
    {
        var edge = new TodoEdge { Waypoints = [new(-10, 20), new(30, 40)] };
        var json = JsonSerializer.Serialize(edge, JsonProjectStore.SerializerOptions);
        var loaded = JsonSerializer.Deserialize<TodoEdge>(json, JsonProjectStore.SerializerOptions)!;
        await Assert.That(loaded.Waypoints.SequenceEqual(edge.Waypoints)).IsTrue();
        await Assert.That(JsonSerializer.Deserialize<TodoEdge>("{}", JsonProjectStore.SerializerOptions)!.Waypoints.Count).IsEqualTo(0);
        await Assert.That(JsonSerializer.Deserialize<TodoEdge>("{\"waypoints\":null}", JsonProjectStore.SerializerOptions)!.Waypoints.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RepeatedOrOverlappingWaypoints_ProduceFiniteRoute()
    {
        var route = EdgeRouting.Route(new(0, 0), new(800, 0), 224, 88, [],
            waypoints: [new(100, 40), new(100, 40)]);
        await Assert.That(route.Count >= 2).IsTrue();
        await Assert.That(route.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y))).IsTrue();
    }
}
