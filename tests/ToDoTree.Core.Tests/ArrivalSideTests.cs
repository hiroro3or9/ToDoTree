using System.Text.Json;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class ArrivalSideTests
{
    [Test]
    public async Task EveryArrivalSide_HasCorrectAnchorAndInwardArrow()
    {
        foreach (var size in new[] { new Vec2(24, 24), new Vec2(224, 88) })
        foreach (var fromSide in Enum.GetValues<ConnectionSide>())
        foreach (var to in new[] { new Vec2(320, 310), new Vec2(-320, -310) })
        foreach (var side in new[] { ConnectionSide.Top, ConnectionSide.Bottom, ConnectionSide.Left, ConnectionSide.Right })
        {
            var curve = CurveGeometry.BetweenNodes(new Vec2(0, 0), to, size.X, size.Y, fromSide, side);
            var expected = side switch
            {
                ConnectionSide.Top => new Vec2(to.X + size.X / 2, to.Y),
                ConnectionSide.Bottom => new Vec2(to.X + size.X / 2, to.Y + size.Y),
                ConnectionSide.Left => new Vec2(to.X, to.Y + size.Y / 2),
                _ => new Vec2(to.X + size.X, to.Y + size.Y / 2),
            };
            await Assert.That(curve.End).IsEqualTo(expected);
            var tangent = curve.End - curve.Control2;
            var inward = side switch
            {
                ConnectionSide.Top => tangent.X == 0 && tangent.Y > 0,
                ConnectionSide.Bottom => tangent.X == 0 && tangent.Y < 0,
                ConnectionSide.Left => tangent.Y == 0 && tangent.X > 0,
                _ => tangent.Y == 0 && tangent.X < 0,
            };
            await Assert.That(inward).IsTrue();
        }
    }

    [Test]
    public async Task ArrivalSide_SurvivesSaveAndClone_WithLegacyDefault()
    {
        foreach (var side in Enum.GetValues<ConnectionSide>())
        {
            var project = new TodoProject { Edges = [new TodoEdge { FromSide = ConnectionSide.Left, ToSide = side }] };
            var json = JsonSerializer.Serialize(project, JsonProjectStore.SerializerOptions);
            var restored = JsonSerializer.Deserialize<TodoProject>(json, JsonProjectStore.SerializerOptions)!;
            await Assert.That(restored.Edges[0].ToSide).IsEqualTo(side);
            await Assert.That(project.DeepClone().Edges[0].ToSide).IsEqualTo(side);
        }
        var legacy = JsonSerializer.Deserialize<TodoEdge>("{}", JsonProjectStore.SerializerOptions)!;
        await Assert.That(legacy.ToSide).IsEqualTo(ConnectionSide.Auto);
    }
}
