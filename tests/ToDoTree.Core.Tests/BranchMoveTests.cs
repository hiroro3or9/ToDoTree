using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class BranchMoveTests
{
    private static TodoProject Sample()
    {
        var project = new TodoProject
        {
            Name = "元のプロジェクト",
            Variables = [new() { Name = "製品", Value = "みかん" }],
            Nodes =
            [
                new() { Title = "共通の前提", X = 40, Y = 100 },
                new() { Title = "{製品}の準備", X = 320, Y = 100, Notes = "作業メモ" },
                new() { Title = "検証", X = 600, Y = 100, Status = NodeStatus.InProgress,
                    Repeat = new() { TargetCount = 3, CompletedCount = 1 },
                    Checklist = [new() { Title = "点検", IsChecked = true }] },
                new() { Title = "別の前提", X = 40, Y = 360 },
            ],
        };
        var graph = new TodoGraph(project);
        graph.Connect(project.Nodes[0].Id, project.Nodes[1].Id);
        graph.Connect(project.Nodes[1].Id, project.Nodes[2].Id);
        graph.Connect(project.Nodes[3].Id, project.Nodes[2].Id);
        return project;
    }

    [Test]
    public async Task Move_PreservesDataAndEveryIncomingEndpoint_WithoutMutatingSource()
    {
        var project = Sample();
        var root = project.Nodes[1];
        var child = project.Nodes[2];
        project.Bookmark = new() { NodeId = child.Id, Note = "ここから再開" };
        project.Edges[1].Label = "確認へ";
        project.Edges[1].Waypoints = [new(480, 210, true)];
        project.Edges[2].ColorId = "blue";
        project.Edges[2].DecisionReason = "外部の条件";
        var before = JsonSerializer.Serialize(project);
        var result = BranchMoveService.Create(project, root.Id, "branch.json", "独立した枝");
        await Assert.That(JsonSerializer.Serialize(project)).IsEqualTo(before);
        await Assert.That(result.MovedCount).IsEqualTo(2);
        await Assert.That(result.Destination.Id != project.Id).IsTrue();
        await Assert.That(JsonSerializer.Serialize(result.Destination.Nodes[1])).IsEqualTo(JsonSerializer.Serialize(child));
        await Assert.That(JsonSerializer.Serialize(result.Destination.Edges.Single())).IsEqualTo(JsonSerializer.Serialize(project.Edges[1]));
        await Assert.That(result.Destination.Bookmark!.NodeId).IsEqualTo(child.Id);
        await Assert.That(result.Source.Bookmark!.NodeId).IsEqualTo(root.Id);
        await Assert.That(result.Source.Nodes.Count(n => n.ProjectLink is not null)).IsEqualTo(2);
        await Assert.That(result.Source.Edges.Select(e => e.Id).ToHashSet()
            .SetEquals(new[] { project.Edges[0].Id, project.Edges[2].Id })).IsTrue();
        await Assert.That(JsonSerializer.Serialize(result.Source.Edges[1])).IsEqualTo(JsonSerializer.Serialize(project.Edges[2]));
        await Assert.That(BlockConnections.Validate(result.Source)).IsNull();
        await Assert.That(BlockConnections.Validate(result.Destination)).IsNull();
        result.Destination.Nodes[1].Repeat!.CompletedCount = 2;
        result.Destination.Nodes[1].Checklist[0].IsChecked = false;
        result.Destination.Variables[0].Value = "りんご";
        await Assert.That(JsonSerializer.Serialize(project)).IsEqualTo(before);
        var clone = result.Source.DeepClone();
        clone.Nodes.Single(n => n.Id == root.Id).ProjectLink!.ProjectName = "変更";
        await Assert.That(result.Source.Nodes.Single(n => n.Id == root.Id).ProjectLink!.ProjectName).IsEqualTo("独立した枝");
    }

    [Test]
    public async Task Move_IncludesNestedBlocksAndTheirDownstream_PreservesPorts()
    {
        var project = Sample();
        var root = project.Nodes[1];
        var child = project.Nodes[2];
        var block = BlockService.Create(project, [root.Id, child.Id], "内側").Block!;
        block.Ports = [new(Guid.NewGuid(), ConnectionSide.Left, 0.3)];
        var outer = BlockService.Wrap(project, block.Id, "外側").Block!;
        var sibling = new TodoNode { Title = "同じ囲みの別作業" };
        project.Nodes.Add(sibling);
        outer.NodeIds.Add(sibling.Id);
        var graph = new TodoGraph(project);
        var toBlock = graph.Connect(project.Nodes[0].Id, block.Id)!;
        toBlock.ToPortId = block.Ports[0].Id;
        var after = new TodoNode { Title = "囲みの後続" };
        project.Nodes.Add(after);
        graph = new TodoGraph(project);
        graph.Connect(sibling.Id, after.Id);
        var result = BranchMoveService.Create(project, root.Id, "nested.json", "まとまり");
        await Assert.That(result.MovedCount).IsEqualTo(4);
        await Assert.That(result.Destination.Blocks.Count).IsEqualTo(2);
        await Assert.That(result.Destination.Blocks.Single(b => b.Id == block.Id).ParentBlockId).IsEqualTo(outer.Id);
        await Assert.That(result.Source.Edges.Single(e => e.Id == toBlock.Id).ToPortId).IsEqualTo(block.Ports[0].Id);
        await Assert.That(result.Source.Blocks.Single(b => b.Id == block.Id).Ports[0].Position).IsEqualTo(0.3);
        await Assert.That(BlockConnections.Validate(result.Source)).IsNull();
        await Assert.That(BlockConnections.Validate(result.Destination)).IsNull();
    }

    [Test]
    public async Task Move_BlockOnlyBoundaryGetsAnEntrance_AndUnreferencedBlocksAreRemoved()
    {
        var project = Sample();
        project.Edges.RemoveAt(2);
        var root = project.Nodes[1];
        var child = project.Nodes[2];
        var childBlock = new TodoBlock { Title = "後半", NodeIds = [child.Id] };
        project.Blocks.Add(childBlock);
        var graph = new TodoGraph(project);
        var edge = graph.Connect(project.Nodes[3].Id, childBlock.Id)!;
        var result = BranchMoveService.Create(project, root.Id, "branch.json", "枝");
        await Assert.That(result.Source.Edges.Single(e => e.Id == edge.Id).ToId).IsEqualTo(childBlock.Id);
        var portalId = result.Source.Blocks.Single().NodeIds.Single();
        await Assert.That(result.Source.Nodes.Single(n => n.Id == portalId).ProjectLink!.NodeId).IsEqualTo(child.Id);
        await Assert.That(BlockConnections.Validate(result.Source)).IsNull();
        project.Edges.Remove(edge);
        var noBoundary = BranchMoveService.Create(project, root.Id, "branch.json", "枝");
        await Assert.That(noBoundary.Source.Blocks.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Move_PreservesChoiceEdgeIdsAndDecisions()
    {
        var project = Sample();
        var root = project.Nodes[1];
        root.IsChoice = true;
        root.SelectedChoiceEdgeId = project.Edges[1].Id;
        project.Nodes[0].IsChoice = true;
        project.Nodes[0].SelectedChoiceEdgeId = project.Edges[0].Id;
        var result = BranchMoveService.Create(project, root.Id, "choice.json", "選択の枝");
        await Assert.That(result.Destination.Nodes.Single(n => n.Id == root.Id).SelectedChoiceEdgeId).IsEqualTo(project.Edges[1].Id);
        await Assert.That(result.Source.Nodes.Single(n => n.Id == root.Id).IsChoice).IsFalse();
        await Assert.That(ChoiceService.Validate(result.Source, TodoProject.CurrentSchemaVersion)).IsNull();
        await Assert.That(ChoiceService.Validate(result.Destination, TodoProject.CurrentSchemaVersion)).IsNull();
    }

    [Test]
    public async Task Move_IsolatedRootLeavesOneEntrance()
    {
        var project = new TodoProject { Nodes = [new() { Title = "枝の全体" }] };
        var result = BranchMoveService.Create(project, project.Nodes[0].Id, "one.json", "独立");
        await Assert.That(result.Source.Nodes.Single().ProjectLink!.NodeId).IsEqualTo(project.Nodes[0].Id);
        await Assert.That(result.Source.Edges.Count).IsEqualTo(0);
        await Assert.That(result.Destination.Nodes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Links_RoundTripAndRejectOldSchemaAndInvalidTarget()
    {
        var project = Sample();
        var result = BranchMoveService.Create(project, project.Nodes[1].Id, "branch.json", "独立");
        var path = Path.Combine(Path.GetTempPath(), $"branch-move-{Guid.NewGuid():N}.json");
        var store = new JsonProjectStore();
        try
        {
            store.Save(path, result.Source);
            var loaded = store.Load(path);
            await Assert.That(JsonSerializer.Serialize(loaded)).IsEqualTo(JsonSerializer.Serialize(result.Source));
            var json = File.ReadAllText(path).Replace(
                $"\"schemaVersion\": {TodoProject.CurrentSchemaVersion}", "\"schemaVersion\": 12");
            File.WriteAllText(path, json);
            await Assert.That(() => store.Load(path)).Throws<InvalidDataException>();
            result.Source.Nodes.First(n => n.ProjectLink is not null).ProjectLink!.NodeId = Guid.Empty;
            await Assert.That(() => store.Save(path, result.Source)).Throws<InvalidDataException>();
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); File.Delete(path + ".tmp"); }
    }

    [Test]
    public async Task Collect_RejectsMissingRootAndProcedure()
    {
        var project = Sample();
        await Assert.That(() => BranchMoveService.Collect(project, Guid.NewGuid())).Throws<InvalidOperationException>();
        project.DocumentKind = DocumentKind.Procedure;
        await Assert.That(() => BranchMoveService.Collect(project, project.Nodes[1].Id)).Throws<InvalidOperationException>();
    }
}
