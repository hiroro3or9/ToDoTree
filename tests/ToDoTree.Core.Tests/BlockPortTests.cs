using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class BlockPortTests
{
    private static TodoProject Scene()
    {
        var a = new TodoNode { X = 100, Y = 100 };
        var b = new TodoNode { X = 600, Y = 100 };
        var left = new TodoBlock { NodeIds = [a.Id], Ports = [new(Guid.NewGuid(), ConnectionSide.Right, 0.25)] };
        var right = new TodoBlock { NodeIds = [b.Id], Ports = [new(Guid.NewGuid(), ConnectionSide.Left, 0.75)] };
        return new TodoProject { Nodes = [a, b], Blocks = [left, right], Edges =
            [new() { FromId = left.Id, ToId = right.Id, FromPortId = left.Ports[0].Id, ToPortId = right.Ports[0].Id }] };
    }

    [Test]
    public async Task PortFollowsTranslationResizeAndCollapsedBounds()
    {
        var port = BlockPort.At(new(100, 100, 400, 200), new(503, 150));
        await Assert.That(port.Side).IsEqualTo(ConnectionSide.Right);
        await Assert.That(port.Position).IsEqualTo(0.25);
        await Assert.That(port.Resolve(new(200, 300, 800, 400))).IsEqualTo(new Vec2(1000, 400));
        await Assert.That(port.Resolve(new(200, 300, 224, 88))).IsEqualTo(new Vec2(424, 322));
        foreach (var side in new[] { ConnectionSide.Left, ConnectionSide.Right, ConnectionSide.Top, ConnectionSide.Bottom })
        {
            var original = port with { Side = side, Position = 0.4 };
            var bounds = new BlockBounds(5, 10, 300, 120);
            var projected = BlockPort.At(bounds, original.Resolve(bounds), original.Id);
            await Assert.That(projected).IsEqualTo(original);
        }
    }

    [Test]
    public async Task RouteUsesExactPortsAndKeepsWaypoints()
    {
        var from = new BlockBounds(0, 0, 300, 240);
        var to = new BlockBounds(800, 100, 200, 320);
        var a = new BlockPort(Guid.NewGuid(), ConnectionSide.Right, 0.2);
        var b = new BlockPort(Guid.NewGuid(), ConnectionSide.Top, 0.8);
        var waypoint = new Vec2(550, -100);
        var route = EdgeRouting.RouteRects(from, to, [], a.Side, b.Side, [waypoint], [true], a.Offset(from), b.Offset(to));
        await Assert.That(route[0]).IsEqualTo(a.Resolve(from));
        await Assert.That(route[^1]).IsEqualTo(b.Resolve(to));
        await Assert.That(route.Contains(waypoint)).IsTrue();
        await Assert.That(route.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y))).IsTrue();
    }

    [Test]
    public async Task SaveLoadAndCloneKeepIndependentPortLists()
    {
        var project = Scene();
        var path = Path.Combine(Path.GetTempPath(), $"ports-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore();
            store.Save(path, project);
            var loaded = store.Load(path);
            await Assert.That(loaded.Blocks[0].Ports.SequenceEqual(project.Blocks[0].Ports)).IsTrue();
            await Assert.That(loaded.Edges[0].FromPortId).IsEqualTo(project.Edges[0].FromPortId);
            await Assert.That(loaded.Edges[0].ToPortId).IsEqualTo(project.Edges[0].ToPortId);
            var clone = loaded.DeepClone();
            loaded.Blocks[0].Ports[0] = loaded.Blocks[0].Ports[0] with { Position = 0.9 };
            loaded.Blocks[0].Ports.Clear();
            await Assert.That(clone.Blocks[0].Ports[0].Position).IsEqualTo(0.25);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task LegacyProjectGetsEmptyPortsAndMigrates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ports-old-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":6}");
            var loaded = new JsonProjectStore().Load(path);
            await Assert.That(loaded.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);
            await Assert.That(JsonSerializer.Deserialize<TodoBlock>("{}")!.Ports.Count).IsEqualTo(0);
            await Assert.That(JsonSerializer.Deserialize<TodoBlock>("{\"ports\":null}", JsonProjectStore.SerializerOptions)!.Ports.Count).IsEqualTo(0);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task InvalidPortDataAndReferencesAreRejected()
    {
        var valid = Scene();
        await Assert.That(BlockConnections.Validate(valid)).IsNull();
        foreach (var position in new[] { double.NaN, double.PositiveInfinity, -0.01, 1.01 })
        {
            var p = valid.DeepClone();
            p.Blocks[0].Ports[0] = p.Blocks[0].Ports[0] with { Position = position };
            await Assert.That(BlockConnections.Validate(p)).IsNotNull();
        }
        var badSide = valid.DeepClone();
        badSide.Blocks[0].Ports[0] = badSide.Blocks[0].Ports[0] with { Side = ConnectionSide.Auto };
        await Assert.That(BlockConnections.Validate(badSide)).IsNotNull();
        var duplicate = valid.DeepClone();
        duplicate.Blocks[0].Ports.Add(duplicate.Blocks[0].Ports[0]);
        await Assert.That(BlockConnections.Validate(duplicate)).IsNotNull();
        var wrongOwner = valid.DeepClone();
        wrongOwner.Edges[0].ToPortId = wrongOwner.Blocks[0].Ports[0].Id;
        await Assert.That(BlockConnections.Validate(wrongOwner)).IsNotNull();
        wrongOwner.Edges[0].FromId = wrongOwner.Nodes[0].Id;
        await Assert.That(BlockConnections.Validate(wrongOwner)).IsNotNull();
    }

    [Test]
    public async Task DissolveClearsOnlyRemovedBlockPortAndKeepsDependencies()
    {
        var project = Scene();
        var targetPort = project.Edges[0].ToPortId;
        var block = project.Blocks[0];
        BlockConnections.Dissolve(project, block);
        project.Blocks.Remove(block);
        await Assert.That(project.Edges[0].FromId).IsEqualTo(project.Nodes[0].Id);
        await Assert.That(project.Edges[0].FromPortId).IsNull();
        await Assert.That(project.Edges[0].ToPortId).IsEqualTo(targetPort);
        await Assert.That(BlockConnections.Validate(project)).IsNull();
        await Assert.That(new TodoGraph(project).ReadinessOf(project.Nodes[1])).IsEqualTo(Readiness.Blocked);
    }

    [Test]
    public async Task TemplateKeepsPortReferencesAndDoesNotShareLists()
    {
        var project = Scene();
        var template = BranchTemplate.Capture(project, project.Nodes.Select(n => n.Id), "接続点付き");
        var placed = BranchTemplate.Instantiate(template, 900, 400);
        await Assert.That(BlockConnections.Validate(placed)).IsNull();
        await Assert.That(placed.Blocks[0].Id != project.Blocks[0].Id).IsTrue();
        await Assert.That(placed.Blocks[0].Ports[0].Position).IsEqualTo(0.25);
        template.Blocks[0].Ports.Clear();
        await Assert.That(placed.Blocks[0].Ports.Count).IsEqualTo(1);
        await Assert.That(project.Blocks[0].Ports.Count).IsEqualTo(1);
    }
}
