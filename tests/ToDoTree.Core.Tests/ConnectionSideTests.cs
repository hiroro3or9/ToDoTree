using System.Text.Json;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class ConnectionSideTests
{
    [Test]
    public async Task ExplicitSide_PreservesAnchorAndOutwardTangent()
    {
        foreach (var size in new[] { new Vec2(24, 24), new Vec2(224, 88) })
        foreach (var target in new[] { new Vec2(320, -310), new Vec2(-320, 310), new Vec2(0, 320) })
        {
            var top = CurveGeometry.BetweenNodes(new Vec2(0, 0), target, size.X, size.Y, ConnectionSide.Top);
            await Assert.That(top.Start).IsEqualTo(new Vec2(size.X / 2, 0));
            await Assert.That(top.Control1.X).IsEqualTo(top.Start.X);
            await Assert.That(top.Control1.Y < top.Start.Y).IsTrue();
            var bottom = CurveGeometry.BetweenNodes(new Vec2(0, 0), target, size.X, size.Y, ConnectionSide.Bottom);
            await Assert.That(bottom.Start).IsEqualTo(new Vec2(size.X / 2, size.Y));
            await Assert.That(bottom.Control1.X).IsEqualTo(bottom.Start.X);
            await Assert.That(bottom.Control1.Y > bottom.Start.Y).IsTrue();
            var left = CurveGeometry.BetweenNodes(new Vec2(0, 0), target, size.X, size.Y, ConnectionSide.Left);
            await Assert.That(left.Start).IsEqualTo(new Vec2(0, size.Y / 2));
            await Assert.That(left.Control1.Y).IsEqualTo(left.Start.Y);
            await Assert.That(left.Control1.X < left.Start.X).IsTrue();
            var right = CurveGeometry.BetweenNodes(new Vec2(0, 0), target, size.X, size.Y, ConnectionSide.Right);
            await Assert.That(right.Start).IsEqualTo(new Vec2(size.X, size.Y / 2));
            await Assert.That(right.Control1.Y).IsEqualTo(right.Start.Y);
            await Assert.That(right.Control1.X > right.Start.X).IsTrue();
        }
    }

    [Test]
    public async Task Side_SurvivesSaveAndUndoClone_AndOldEdgesDefaultToAuto()
    {
        foreach (var side in Enum.GetValues<ConnectionSide>())
        {
        var project = new TodoProject { Edges = [new TodoEdge { FromSide = side }] };
        var json = JsonSerializer.Serialize(project, JsonProjectStore.SerializerOptions);
        var restored = JsonSerializer.Deserialize<TodoProject>(json, JsonProjectStore.SerializerOptions)!;
        await Assert.That(restored.Edges[0].FromSide).IsEqualTo(side);
        await Assert.That(project.DeepClone().Edges[0].FromSide).IsEqualTo(side);
        }
        var legacy = JsonSerializer.Deserialize<TodoEdge>("{}", JsonProjectStore.SerializerOptions)!;
        await Assert.That(legacy.FromSide).IsEqualTo(ConnectionSide.Auto);
    }
}