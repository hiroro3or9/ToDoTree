using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

public class BlockConnectionTests
{
    private static (TodoProject Project, TodoGraph Graph, TodoBlock A, TodoBlock B, TodoNode[] Nodes) Scene()
    {
        var nodes = Enumerable.Range(0, 6).Select(i => new TodoNode { Title = $"Step {i}", X = i * 200, Y = 100 }).ToArray();
        var project = new TodoProject { Nodes = [.. nodes] };
        var a = BlockService.Create(project, [nodes[0].Id, nodes[1].Id], "設計").Block!;
        var b = BlockService.Create(project, [nodes[2].Id, nodes[3].Id], "実装").Block!;
        return (project, new TodoGraph(project), a, b, nodes);
    }

    [Test]
    public async Task AllMembersAndInternalOrderGateReadiness()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(n[2].Id, n[3].Id);
        var connection = g.Connect(a.Id, b.Id)!;
        await Assert.That(connection is not null).IsTrue();
        await Assert.That(p.Edges.Count).IsEqualTo(2);
        await Assert.That(g.ReadinessOf(n[2])).IsEqualTo(Readiness.Blocked);
        n[0].Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(n[2])).IsEqualTo(Readiness.Blocked);
        n[1].Status = NodeStatus.Cancelled; // 既存の「片付いた」ルールに合わせる。
        await Assert.That(g.ReadinessOf(n[2])).IsEqualTo(Readiness.Ready);
        await Assert.That(g.ReadinessOf(n[3])).IsEqualTo(Readiness.Blocked);
        n[2].Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(n[3])).IsEqualTo(Readiness.Ready);
        await Assert.That(g.NodeCount).IsEqualTo(6);
    }

    [Test]
    public async Task MixedEndpointsAndMultiplePredecessors()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(a.Id, b.Id); g.Connect(n[4].Id, b.Id); g.Connect(b.Id, n[5].Id);
        n[0].Status = n[1].Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(n[2])).IsEqualTo(Readiness.Blocked);
        n[4].Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(n[2])).IsEqualTo(Readiness.Ready);
        n[2].Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(n[5])).IsEqualTo(Readiness.Blocked);
        n[3].Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(n[5])).IsEqualTo(Readiness.Ready);
        await Assert.That(p.Edges.Count).IsEqualTo(3);
    }

    [Test]
    public async Task CyclesAndMembershipCyclesAreRejectedAtomically()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(a.Id, b.Id);
        await Assert.That(g.CanConnect(b.Id, a.Id)).IsEqualTo(ConnectionCheck.WouldCreateCycle);
        await Assert.That(g.CanConnect(a.Id, n[0].Id)).IsEqualTo(ConnectionCheck.WouldCreateCycle);
        g.Connect(b.Id, n[4].Id);
        var before = JsonSerializer.Serialize(p);
        await Assert.That(BlockService.Transfer(p, [n[4].Id], a.Id) is not null).IsTrue();
        await Assert.That(JsonSerializer.Serialize(p)).IsEqualTo(before);
        await Assert.That(BlockService.Add(p, a.Id, [n[4].Id]).IsOk).IsFalse();
        await Assert.That(JsonSerializer.Serialize(p)).IsEqualTo(before);
    }

    [Test]
    public async Task AddedMembersGateStartedWorkWithoutChangingItsStatus()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(a.Id, b.Id);
        n[0].Status = n[1].Status = NodeStatus.Done;
        n[2].Status = NodeStatus.InProgress;
        await Assert.That(BlockService.Transfer(p, [n[4].Id], a.Id)).IsNull();
        g.Rebuild();
        await Assert.That(n[2].Status).IsEqualTo(NodeStatus.InProgress);
        await Assert.That(g.ReadinessOf(n[3])).IsEqualTo(Readiness.Blocked);
        await Assert.That(g.ParentsOf(n[2].Id).Any(n => !n.IsSettled)).IsTrue();
    }

    [Test]
    public async Task DissolvingPreservesDependenciesAndOtherBlockEndpoints()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(a.Id, b.Id); g.Connect(n[0].Id, n[2].Id);
        var expected = g.Edges.Select(e => (e.FromId, e.ToId)).ToHashSet();
        BlockService.Dissolve(p, a.Id); g.Rebuild();
        await Assert.That(g.Edges.Select(e => (e.FromId, e.ToId)).ToHashSet().SetEquals(expected)).IsTrue();
        await Assert.That(p.Edges.Count(e => e.ToId == b.Id)).IsEqualTo(2);
        BlockService.Dissolve(p, b.Id); g.Rebuild();
        await Assert.That(g.Edges.Select(e => (e.FromId, e.ToId)).ToHashSet().SetEquals(expected)).IsTrue();
        await Assert.That(p.Edges.Count).IsEqualTo(4);
    }

    [Test]
    public async Task RemovingOneMemberKeepsTheRestOfTheBlockConnection()
    {
        var (p, g, a, b, n) = Scene();
        var edge = g.Connect(a.Id, b.Id)!;
        g.RemoveNode(n[0].Id);
        await Assert.That(p.Edges.Single().Id).IsEqualTo(edge.Id);
        await Assert.That(g.ParentsOf(n[2].Id).Single().Id).IsEqualTo(n[1].Id);
        g.RemoveNode(n[1].Id);
        await Assert.That(p.Edges.Count).IsEqualTo(0);
        await Assert.That(BlockConnections.Validate(p)).IsNull();
    }

    [Test]
    public async Task TransferAllMembersToAnotherBlockRemovesEmptySource()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(a.Id, n[4].Id);
        await Assert.That(BlockService.Transfer(p, [n[0].Id, n[1].Id], b.Id)).IsNull();
        await Assert.That(p.Blocks.Count).IsEqualTo(1);
        await Assert.That(b.NodeIds.Count).IsEqualTo(4);
        await Assert.That(p.Edges.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FocusContainsOnlyMembersAndDirectNeighbours()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(n[4].Id, a.Id); g.Connect(a.Id, b.Id); g.Connect(b.Id, n[5].Id);
        var keep = BlockConnections.FocusNodes(g, a.Id);
        await Assert.That(keep.SetEquals(n.Take(5).Select(n => n.Id))).IsTrue();
    }

    [Test]
    public async Task SaveRoundTripAndTemplatePreserveBlockConnections()
    {
        var (p, g, a, b, n) = Scene();
        g.Connect(a.Id, b.Id); a.IsCollapsed = true;
        var folder = Path.Combine(Path.GetTempPath(), $"todotree-block-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "project.json");
            var store = new JsonProjectStore(); store.Save(path, p);
            var restored = store.Load(path);
            await Assert.That(restored.Blocks[0].IsCollapsed).IsTrue();
            await Assert.That(restored.Edges.Single().FromId).IsEqualTo(a.Id);
            await Assert.That(new TodoGraph(restored).ReadinessOf(restored.Nodes[2])).IsEqualTo(Readiness.Blocked);
            var template = BranchTemplate.Capture(restored, n.Take(4).Select(n => n.Id), "工程");
            await Assert.That(template.Edges.Count).IsEqualTo(1);
            await Assert.That(template.Edges[0].FromId).IsEqualTo(template.Blocks[0].Id);
            var instance = BranchTemplate.Instantiate(template, 600, 800);
            await Assert.That(instance.Blocks[0].IsCollapsed).IsFalse();
            await Assert.That(instance.Edges[0].FromId == template.Edges[0].FromId).IsFalse();
            await Assert.That(BlockConnections.Validate(instance)).IsNull();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public async Task ExportIncludesResolvedDependencies()
    {
        var (p, g, a, b, n) = Scene(); g.Connect(a.Id, b.Id);
        var mermaid = GraphExporter.ToMermaid(p);
        await Assert.That(mermaid.Contains("n1 --> n3")).IsTrue();
        await Assert.That(mermaid.Contains("n2 --> n4")).IsTrue();
    }

    [Test]
    public async Task DifferentSizedEndpointsAndWaypointAreRoutedFromTheirPorts()
    {
        var a = new BlockBounds(0, 0, 340, 48);
        var b = new BlockBounds(650, 0, 180, 120);
        var point = new Vec2(460, 180);
        var route = EdgeRouting.RouteRects(a, b, [], ConnectionSide.Right, ConnectionSide.Left, [point], [true]);
        await Assert.That(route[0]).IsEqualTo(new Vec2(340, 24));
        await Assert.That(route[^1]).IsEqualTo(new Vec2(650, 60));
        await Assert.That(route.Any(p => (p - point).Length < 0.01)).IsTrue();
    }
}
