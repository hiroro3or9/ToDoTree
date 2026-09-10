using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class BlockNestingTests
{
    [Test]
    public async Task HierarchyIndexesArbitraryDepthAndAllowsEmptyParent()
    {
        var p = Project(4);
        var root = new TodoBlock { Title = "親", NodeIds = [] };
        var child = new TodoBlock { Title = "子", ParentBlockId = root.Id, NodeIds = [p.Nodes[0].Id] };
        var leaf = new TodoBlock { Title = "孫", ParentBlockId = child.Id, NodeIds = [p.Nodes[1].Id, p.Nodes[2].Id] };
        p.Blocks.AddRange([root, child, leaf]);

        var hierarchy = new BlockHierarchy(p);
        await Assert.That(BlockService.Validate(p)).IsNull();
        await Assert.That(hierarchy.ChildrenOf(root.Id).Single().Id).IsEqualTo(child.Id);
        await Assert.That(hierarchy.AncestorsOf(leaf.Id).Select(b => b.Id).SequenceEqual([child.Id, root.Id])).IsTrue();
        await Assert.That(hierarchy.DescendantNodeIds(root.Id).SetEquals(p.Nodes.Take(3).Select(n => n.Id))).IsTrue();
        await Assert.That(hierarchy.PathOf(leaf.Id)).IsEqualTo("親 / 子 / 孫");
    }

    [Test]
    public async Task CreateInsideOneParentMovesOnlyDirectMembership()
    {
        var p = Project(4);
        var parent = BlockService.Create(p, p.Nodes.Take(3).Select(n => n.Id).ToArray(), "親").Block!;
        var child = BlockService.Create(p, [p.Nodes[0].Id, p.Nodes[1].Id], "子");

        await Assert.That(child.IsOk).IsTrue();
        await Assert.That(child.Block!.ParentBlockId).IsEqualTo(parent.Id);
        await Assert.That(parent.NodeIds.SequenceEqual([p.Nodes[2].Id])).IsTrue();

        var before = JsonSerializer.Serialize(p);
        var mixed = BlockService.Create(p, [p.Nodes[0].Id, p.Nodes[2].Id]);
        await Assert.That(mixed.IsOk).IsFalse();
        await Assert.That(JsonSerializer.Serialize(p)).IsEqualTo(before);
    }

    [Test]
    public async Task MoveRejectsHierarchyAndExpandedDependencyCyclesAtomically()
    {
        var p = Project(4);
        var a = BlockService.Create(p, [p.Nodes[0].Id, p.Nodes[1].Id], "A").Block!;
        var b = BlockService.Create(p, [p.Nodes[2].Id, p.Nodes[3].Id], "B").Block!;
        new TodoGraph(p).Connect(a.Id, b.Id);

        var before = JsonSerializer.Serialize(p);
        await Assert.That(BlockService.MoveBlock(p, b.Id, a.Id)).IsNotNull();
        await Assert.That(JsonSerializer.Serialize(p)).IsEqualTo(before);

        p.Edges.Clear();
        await Assert.That(BlockService.MoveBlock(p, b.Id, a.Id)).IsNull();
        var nested = JsonSerializer.Serialize(p);
        await Assert.That(BlockService.MoveBlock(p, a.Id, b.Id)).IsNotNull();
        await Assert.That(JsonSerializer.Serialize(p)).IsEqualTo(nested);
    }

    [Test]
    public async Task ConnectionsExpandThroughDescendantsAndDissolveKeepsMeaning()
    {
        var p = Project(4);
        var root = new TodoBlock { Title = "親", NodeIds = [p.Nodes[0].Id] };
        var child = new TodoBlock { Title = "子", ParentBlockId = root.Id, NodeIds = [p.Nodes[1].Id, p.Nodes[2].Id] };
        p.Blocks.AddRange([root, child]);
        new TodoGraph(p).Connect(p.Nodes[3].Id, root.Id);
        var before = BlockConnections.Expand(p).Select(e => (e.FromId, e.ToId)).ToHashSet();

        BlockService.Dissolve(p, root.Id);
        var after = BlockConnections.Expand(p).Select(e => (e.FromId, e.ToId)).ToHashSet();

        await Assert.That(after.SetEquals(before)).IsTrue();
        await Assert.That(child.ParentBlockId).IsNull();
        await Assert.That(p.Blocks.Single().Id).IsEqualTo(child.Id);
        await Assert.That(BlockConnections.Validate(p)).IsNull();
    }

    [Test]
    public async Task EmptyParentStaysWhileChildHasMembersAndThenPrunesBottomUp()
    {
        var p = Project(2);
        var parent = new TodoBlock { Title = "親", NodeIds = [p.Nodes[0].Id] };
        var child = new TodoBlock { Title = "子", ParentBlockId = parent.Id, NodeIds = [p.Nodes[1].Id] };
        p.Blocks.AddRange([parent, child]);

        await Assert.That(BlockService.Transfer(p, [p.Nodes[0].Id], child.Id)).IsNull();
        await Assert.That(p.Blocks.Count).IsEqualTo(2);
        await Assert.That(parent.NodeIds.Count).IsEqualTo(0);

        await Assert.That(BlockService.Transfer(p, p.Nodes.Select(n => n.Id).ToArray(), null)).IsNull();
        await Assert.That(p.Blocks.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SaveAndTemplateRoundTripRemapParents()
    {
        var p = Project(3);
        var root = new TodoBlock { Title = "親", NodeIds = [p.Nodes[0].Id] };
        var child = new TodoBlock { Title = "子", ParentBlockId = root.Id, NodeIds = [p.Nodes[1].Id, p.Nodes[2].Id] };
        p.Blocks.AddRange([root, child]);
        var path = Path.Combine(Path.GetTempPath(), $"todotree-nesting-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore();
            store.Save(path, p);
            var loaded = store.Load(path);
            await Assert.That(loaded.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);
            await Assert.That(loaded.Blocks.Single(b => b.Title == "子").ParentBlockId)
                .IsEqualTo(loaded.Blocks.Single(b => b.Title == "親").Id);

            var template = BranchTemplate.Capture(loaded, loaded.Nodes.Skip(1).Select(n => n.Id), "入れ子");
            var instance = BranchTemplate.Instantiate(template, 500, 600);
            var instanceRoot = instance.Blocks.Single(b => b.Title == "親");
            var instanceChild = instance.Blocks.Single(b => b.Title == "子");
            await Assert.That(instanceChild.ParentBlockId).IsEqualTo(instanceRoot.Id);
            await Assert.That(instanceRoot.Id == root.Id).IsFalse();
            await Assert.That(BlockConnections.Validate(instance)).IsNull();
        }
        finally
        {
            foreach (var candidate in new[] { path, path + ".bak", path + ".tmp" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Test]
    public async Task ValidationRejectsMissingParentsCyclesAndOldSchemaParents()
    {
        var p = Project(2);
        var a = new TodoBlock { Title = "A", NodeIds = [p.Nodes[0].Id] };
        var b = new TodoBlock { Title = "B", NodeIds = [p.Nodes[1].Id] };
        p.Blocks.AddRange([a, b]);

        a.ParentBlockId = Guid.NewGuid();
        await Assert.That(BlockService.Validate(p)).IsNotNull();
        a.ParentBlockId = b.Id;
        b.ParentBlockId = a.Id;
        await Assert.That(BlockService.Validate(p)).IsNotNull();

        b.ParentBlockId = null;
        p.SchemaVersion = 7;
        var path = Path.Combine(Path.GetTempPath(), $"todotree-old-nesting-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(p, JsonProjectStore.SerializerOptions));
            var rejected = false;
            try { new JsonProjectStore().Load(path); }
            catch (InvalidDataException) { rejected = true; }
            await Assert.That(rejected).IsTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TodoProject Project(int count) => new()
    {
        Nodes = [.. Enumerable.Range(0, count).Select(i => new TodoNode
        {
            Title = $"N{i}", X = i * 240, Y = 100,
        })],
    };
}
