using System.Text.Json;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class SmoothWaypointTests
{
    [Test]
    public async Task Smoothing_SpansWholeLegs_IncludingCardPorts()
    {
        var startNode = new Vec2(0, 80);
        var endNode = new Vec2(800, 0);
        var waypoint = new Vec2(430, 310);
        var route = EdgeRouting.Route(startNode, endNode, 24, 24, [],
            ConnectionSide.Right, ConnectionSide.Left, [waypoint], [true]);
        // 旧実装の24pxの直線→斜線の折れ点が残らないこと。
        await Assert.That(route.Contains(new Vec2(48, 92))).IsFalse();
        await Assert.That(route.Contains(new Vec2(776, 12))).IsFalse();
        var index = route.ToList().IndexOf(waypoint);
        await Assert.That(index > 0).IsTrue();
        var firstHalf = route[index / 4];
        var lastHalf = route[index + (route.Count - 1 - index) / 4];
        await Assert.That(CurveGeometry.DistanceToSegment(firstHalf, route[0], waypoint) > 10).IsTrue();
        await Assert.That(CurveGeometry.DistanceToSegment(lastHalf, waypoint, route[^1]) > 10).IsTrue();
        var departure = route[1] - route[0];
        var arrival = route[^1] - route[^2];
        await Assert.That(Math.Abs(departure.Y / departure.X) < 0.1).IsTrue();
        await Assert.That(Math.Abs(arrival.Y / arrival.X) < 0.1).IsTrue();
    }
    [Test]
    public async Task SmoothPoint_IsInterpolatedWithContinuousDirection()
    {
        Vec2[] points = [new(450, -160)];
        var sharp = EdgeRouting.Route(new(0, 0), new(800, 0), 224, 88, [], waypoints: points);
        var smooth = EdgeRouting.Route(new(0, 0), new(800, 0), 224, 88, [], waypoints: points, smoothWaypoints: [true]);
        await Assert.That(smooth.Count > sharp.Count).IsTrue();
        var index = smooth.ToList().IndexOf(points[0]);
        await Assert.That(index > 0).IsTrue();
        var incoming = points[0] - smooth[index - 1];
        var outgoing = smooth[index + 1] - points[0];
        var cosine = (incoming.X * outgoing.X + incoming.Y * outgoing.Y) / (incoming.Length * outgoing.Length);
        await Assert.That(cosine > 0.98).IsTrue();
        await Assert.That(smooth[0]).IsEqualTo(sharp[0]);
        await Assert.That(smooth[^1]).IsEqualTo(sharp[^1]);
        await Assert.That(EdgeRouting.Distance(points[0], smooth)).IsEqualTo(0d);
    }

[Test]
    public async Task MixedModes_OnlySmoothSelectedPoint()
    {
        Vec2[] points = [new(400, -160), new(650, -80)];
        var sharp = EdgeRouting.Route(new(0, 0), new(1000, 0), 224, 88, [], waypoints: points);
        var route = EdgeRouting.Route(new(0, 0), new(1000, 0), 224, 88, [], waypoints: points, smoothWaypoints: [true, false]);
        var before = sharp.ToList().IndexOf(points[1]);
        var after = route.ToList().IndexOf(points[1]);
        await Assert.That(route[after + 1]).IsEqualTo(sharp[before + 1]);
        await Assert.That(route.ToList().IndexOf(points[0]) < after).IsTrue();
        var disabled = EdgeRouting.Route(new(0, 0), new(1000, 0), 224, 88, [], waypoints: points, smoothWaypoints: [false, false]);
        await Assert.That(disabled.SequenceEqual(sharp)).IsTrue();
    }

[Test]
    public async Task SmoothSetting_RoundTrips_AndDefaultsToSharp()
    {
        var edge = new TodoEdge { Waypoints = [new(450, -160, true), new(700, 44)] };
        var json = JsonSerializer.Serialize(edge, JsonProjectStore.SerializerOptions);
        var loaded = JsonSerializer.Deserialize<TodoEdge>(json, JsonProjectStore.SerializerOptions)!;
        await Assert.That(loaded.Waypoints.SequenceEqual(edge.Waypoints)).IsTrue();
        var old = JsonSerializer.Deserialize<JunctionPoint>("{\"x\":1,\"y\":2}", JsonProjectStore.SerializerOptions);
        await Assert.That(old.IsSmooth).IsFalse();
        var snapshot = edge.Clone();
        edge.Waypoints[0] = edge.Waypoints[0] with { IsSmooth = false };
        await Assert.That(snapshot.Waypoints[0].IsSmooth).IsTrue();
    }

[Test]
    public async Task Smoothing_PreservesObstacleClearance()
    {
        var route = EdgeRouting.Route(new(0, 0), new(1000, 0), 224, 88, [new(400, 0)],
            waypoints: [new(650, -20)], smoothWaypoints: [true]);
        for (var i = 1; i < route.Count; i++)
        for (var step = 0; step <= 50; step++)
        {
            var p = route[i - 1] + (route[i] - route[i - 1]) * (step / 50d);
            await Assert.That(p.X > 389 && p.X < 635 && p.Y > -11 && p.Y < 99).IsFalse();
        }
    }
}
