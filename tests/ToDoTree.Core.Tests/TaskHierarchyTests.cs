using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class TaskHierarchyTests
{
    private static (TodoProject Project, TodoNode Before, TodoNode Parent, TodoNode After, TodoNode First, TodoNode Second) Sample()
    {
        var before = new TodoNode { Title = "承認" };
        var parent = new TodoNode { Title = "開発", X = 350, Y = 120 };
        var after = new TodoNode { Title = "公開", Kind = NodeKind.Goal };
        var first = new TodoNode { Title = "実装", ParentTaskId = parent.Id, X = 60, Y = 100 };
        var second = new TodoNode { Title = "確認", ParentTaskId = parent.Id, X = 360, Y = 100 };
        var project = new TodoProject { Nodes = [before, parent, after, first, second] };
        var graph = new TodoGraph(project);
        graph.Connect(before.Id, parent.Id);
        graph.Connect(parent.Id, after.Id);
        graph.Connect(first.Id, second.Id);
        return (project, before, parent, after, first, second);
    }

    [Test]
    public async Task ChildrenInheritExternalPrerequisitesAndManualBlock()
    {
        var s = Sample(); var graph = new TodoGraph(s.Project);
        await Assert.That(graph.ReadinessOf(s.First)).IsEqualTo(Readiness.Blocked);
        s.Before.Status = NodeStatus.Done; graph.Rebuild();
        await Assert.That(graph.ReadinessOf(s.First)).IsEqualTo(Readiness.Ready);
        await Assert.That(graph.ReadinessOf(s.Second)).IsEqualTo(Readiness.Blocked);
        s.Parent.IsManuallyBlocked = true; graph.Rebuild();
        await Assert.That(graph.ReadinessOf(s.First)).IsEqualTo(Readiness.Blocked);
        s.Parent.IsManuallyBlocked = false;
        s.First.Status = NodeStatus.Done; graph.Rebuild();
        await Assert.That(graph.ReadinessOf(s.Second)).IsEqualTo(Readiness.Ready);
        await Assert.That(s.Parent.Status).IsEqualTo(NodeStatus.InProgress);
    }

    [Test]
    public async Task CompletionAndReopeningPropagateAcrossMultipleLevels()
    {
        var s = Sample();
        var deep = new TodoNode { Title = "詳細な検証", ParentTaskId = s.Second.Id };
        s.Project.Nodes.Add(deep);
        var graph = new TodoGraph(s.Project);
        s.Before.Status = s.First.Status = deep.Status = NodeStatus.Done;
        graph.Rebuild();
        await Assert.That(s.Second.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(s.Parent.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(graph.ReadinessOf(s.After)).IsEqualTo(Readiness.Ready);
        var completedAt = s.Parent.CompletedAt;
        graph.Rebuild();
        await Assert.That(s.Parent.CompletedAt).IsEqualTo(completedAt);
        deep.Status = NodeStatus.NotStarted; graph.Rebuild();
        await Assert.That(s.Second.Status).IsEqualTo(NodeStatus.NotStarted);
        await Assert.That(s.Parent.Status).IsEqualTo(NodeStatus.InProgress);
        await Assert.That(s.Parent.CompletedAt).IsNull();
        await Assert.That(graph.ReadinessOf(s.After)).IsEqualTo(Readiness.Blocked);
        await Assert.That(graph.Progress().Total).IsEqualTo(3);
        await Assert.That(graph.Progress(s.Parent.Id).Total).IsEqualTo(2);
    }

    [Test]
    public async Task ExternalChoiceAndNestedChoiceAreHonored()
    {
        var s = Sample(); var graph = new TodoGraph(s.Project);
        var alternative = graph.AddNode(new TodoNode { Title = "別案" });
        var alternativeEdge = graph.Connect(s.Before.Id, alternative.Id)!;
        s.Before.IsChoice = true; graph.Rebuild();
        await Assert.That(graph.BranchStateOf(s.First.Id)).IsEqualTo(BranchState.Pending);
        s.Before.SelectedChoiceEdgeId = alternativeEdge.Id; graph.Rebuild();
        await Assert.That(graph.BranchStateOf(s.First.Id)).IsEqualTo(BranchState.Skipped);
        s.Before.SelectedChoiceEdgeId = s.Project.Edges.Single(e => e.ToId == s.Parent.Id).Id;
        s.Before.Status = NodeStatus.Done;
        s.First.IsChoice = true;
        var skip = graph.AddNode(new TodoNode { Title = "見送る内部作業", ParentTaskId = s.Parent.Id });
        graph.Connect(s.First.Id, skip.Id);
        s.First.SelectedChoiceEdgeId = s.Project.Edges.Single(e => e.ToId == s.Second.Id).Id;
        s.First.Status = s.Second.Status = NodeStatus.Done; graph.Rebuild();
        await Assert.That(s.Parent.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(skip.Status).IsEqualTo(NodeStatus.NotStarted);
    }

    [Test]
    public async Task CrossScopeEdgesAndBlocksAreRejected()
    {
        var s = Sample(); var graph = new TodoGraph(s.Project);
        await Assert.That(graph.CanConnect(s.Parent.Id, s.First.Id)).IsEqualTo(ConnectionCheck.DifferentTaskScope);
        await Assert.That(graph.CanConnect(s.First.Id, s.After.Id)).IsEqualTo(ConnectionCheck.DifferentTaskScope);
        var block = new TodoBlock { NodeIds = [s.First.Id, s.Second.Id] };
        s.Project.Blocks.Add(block);
        await Assert.That(graph.CanConnect(s.Before.Id, block.Id)).IsEqualTo(ConnectionCheck.DifferentTaskScope);
        block.NodeIds.Add(s.After.Id);
        await Assert.That(BlockConnections.Validate(s.Project)).IsNotNull();
    }

    [Test]
    public async Task InvalidParentAndCyclesAndIncompatibleParentsAreRejected()
    {
        var s = Sample();
        s.First.ParentTaskId = Guid.NewGuid();
        await Assert.That(TaskHierarchy.Validate(s.Project, 14)).IsNotNull();
        s.First.ParentTaskId = s.Parent.Id; s.Parent.ParentTaskId = s.First.Id;
        await Assert.That(TaskHierarchy.Validate(s.Project, 14)).IsNotNull();
        s.Parent.ParentTaskId = null; s.Parent.Repeat = new() { TargetCount = 3 };
        await Assert.That(TaskHierarchy.Validate(s.Project, 14)).IsNotNull();
        s.Parent.Repeat = null;
        await Assert.That(TaskHierarchy.Validate(s.Project, 13)).IsNotNull();
    }

    [Test]
    public async Task DeleteParentRemovesAllDescendantsAndBridgesOnlyOuterEdges()
    {
        var s = Sample();
        var child = new TodoNode { Title = "さらに中", ParentTaskId = s.Second.Id };
        s.Project.Nodes.Add(child);
        s.Project.Bookmark = new() { NodeId = child.Id };
        s.Project.Blocks.Add(new TodoBlock { NodeIds = [s.First.Id, s.Second.Id] });
        var graph = new TodoGraph(s.Project);
        graph.RemoveNodeAndBridge(s.Parent.Id);
        await Assert.That(s.Project.Nodes.Count).IsEqualTo(2);
        await Assert.That(s.Project.Edges.Single().FromId).IsEqualTo(s.Before.Id);
        await Assert.That(s.Project.Edges.Single().ToId).IsEqualTo(s.After.Id);
        await Assert.That(s.Project.Blocks.Count).IsEqualTo(0);
        await Assert.That(s.Project.Bookmark).IsNull();
        await Assert.That(TaskHierarchy.Validate(s.Project, 14)).IsNull();
    }

    [Test]
    public async Task TemplatesAndBranchMoveKeepInternalSteps()
    {
        var s = Sample();
        var template = BranchTemplate.Capture(s.Project, [s.Parent.Id], "開発部品");
        await Assert.That(template.Nodes.Count).IsEqualTo(3);
        var parent = template.Nodes.Single(n => n.ParentTaskId is null);
        await Assert.That(template.Nodes.Count(n => n.ParentTaskId == parent.Id)).IsEqualTo(2);
        await Assert.That(TaskHierarchy.Validate(template, 14)).IsNull();
        var move = BranchMoveService.Create(s.Project, s.Parent.Id, "move.json", "引っ越し");
        await Assert.That(move.Destination.Nodes.Count).IsEqualTo(4);
        await Assert.That(move.Destination.Nodes.Single(n => n.Id == s.First.Id).ParentTaskId).IsEqualTo(s.Parent.Id);
        await Assert.That(TaskHierarchy.Validate(move.Source, 14)).IsNull();
        await Assert.That(TaskHierarchy.Validate(move.Destination, 14)).IsNull();
        var internalMove = BranchMoveService.Create(s.Project, s.First.Id, "internal.json", "内部から独立");
        await Assert.That(internalMove.Destination.Nodes.All(n => n.ParentTaskId is null)).IsTrue();
        await Assert.That(internalMove.Source.Nodes.Single(n => n.Id == s.First.Id).ParentTaskId).IsEqualTo(s.Parent.Id);
        await Assert.That(TaskHierarchy.Validate(internalMove.Source, 14)).IsNull();
    }

    [Test]
    public async Task ScopeLayoutCannotMoveOuterNodes()
    {
        var s = Sample();
        var scope = new TodoGraph(TaskHierarchy.Scope(s.Project, s.Parent.Id));
        await Assert.That(scope.Nodes.Select(n => n.Id).ToHashSet().SetEquals([s.First.Id, s.Second.Id])).IsTrue();
        await Assert.That(scope.Edges.Count).IsEqualTo(1);
        ToDoTree.Core.Layout.LayeredLayoutEngine.Apply(scope, new());
        await Assert.That(s.Parent.X).IsEqualTo(350);
        await Assert.That(s.Parent.Y).IsEqualTo(120);
    }

    [Test]
    public async Task SaveRoundTripAndOldSchemaDetection()
    {
        var s = Sample();
        var path = Path.Combine(Path.GetTempPath(), $"task-hierarchy-{Guid.NewGuid():N}.json");
        var store = new JsonProjectStore();
        try
        {
            store.Save(path, s.Project);
            var loaded = store.Load(path);
            await Assert.That(JsonSerializer.Serialize(loaded)).IsEqualTo(JsonSerializer.Serialize(s.Project));
            var text = File.ReadAllText(path).Replace("\"schemaVersion\": 14", "\"schemaVersion\": 13");
            File.WriteAllText(path, text);
            await Assert.That(() => store.Load(path)).Throws<InvalidDataException>();
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); File.Delete(path + ".tmp"); }
    }
}