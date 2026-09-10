using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class TaskDetailsTests
{
    [Test]
    public async Task BlockedInProgress_IsExcludedFromSuggestions_AndDoesNotUnlock()
    {
        var parent = new TodoNode();
        var blocked = new TodoNode { Status = NodeStatus.InProgress, IsManuallyBlocked = true, BlockReason = "返事待ち" };
        var goal = new TodoNode { Kind = NodeKind.Goal };
        var graph = new TodoGraph(new() { Nodes = [parent, blocked, goal] });
        graph.Connect(parent.Id, blocked.Id);
        graph.Connect(blocked.Id, goal.Id);
        await Assert.That(graph.ReadinessOf(blocked)).IsEqualTo(Readiness.Blocked);
        await Assert.That(NextActionPlanner.Suggest(graph).Any(a => a.Node.Id == blocked.Id)).IsFalse();
        await Assert.That(CompletionImpact.Calculate(graph, [parent.Id]).Unlocked.Count).IsEqualTo(0);
        await Assert.That(graph.NodesUnlockedBy(parent.Id).Count).IsEqualTo(0);
        parent.Status = NodeStatus.Done;
        var causes = BlockerAnalysis.Find(graph, goal.Id);
        await Assert.That(causes.Count).IsEqualTo(1);
        await Assert.That(causes[0].Reason).IsEqualTo("ブロック中：返事待ち");
        blocked.IsManuallyBlocked = false;
        await Assert.That(graph.ReadinessOf(blocked)).IsEqualTo(Readiness.InProgress);
        blocked.IsManuallyBlocked = true;
        blocked.Status = NodeStatus.Done;
        await Assert.That(blocked.IsManuallyBlocked).IsFalse();
        await Assert.That(BlockerAnalysis.Find(graph, goal.Id).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Causes_DeduplicateDiamond_StopAtSettledParents_AndExpandBlocks()
    {
        var source = new TodoNode { Title = "回答", IsManuallyBlocked = true };
        var left = new TodoNode(); var right = new TodoNode(); var goal = new TodoNode();
        var project = new TodoProject { Nodes = [source, left, right, goal] };
        var block = BlockService.Create(project, [left.Id, right.Id], "設計").Block!;
        var graph = new TodoGraph(project);
        graph.Connect(source.Id, block.Id); graph.Connect(block.Id, goal.Id);
        await Assert.That(BlockerAnalysis.Find(graph, goal.Id).Count).IsEqualTo(1);
        left.Status = NodeStatus.Done; right.Status = NodeStatus.Cancelled;
        await Assert.That(BlockerAnalysis.Find(graph, goal.Id).Count).IsEqualTo(0);
        goal.IsManuallyBlocked = true;
        await Assert.That(BlockerAnalysis.Find(graph, goal.Id).Single().Node.Id).IsEqualTo(goal.Id);
    }

    [Test]
    public async Task SaveCloneAndTemplate_PreserveDefinitionsWithoutSharingProgress()
    {
        var node = new TodoNode { Title = "送信", IsManuallyBlocked = true, BlockReason = "承認待ち",
            Checklist = [new() { Title = "添付", IsChecked = true }] };
        var project = new TodoProject { Nodes = [node], Inbox = [new() { Title = "後で整理" }] };
        var path = Path.Combine(Path.GetTempPath(), $"details-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore(); store.Save(path, project);
            var loaded = store.Load(path);
            await Assert.That(loaded.Nodes[0].IsManuallyBlocked).IsTrue();
            await Assert.That(loaded.Nodes[0].BlockReason).IsEqualTo("承認待ち");
            await Assert.That(loaded.Nodes[0].Checklist[0].IsChecked).IsTrue();
            await Assert.That(loaded.Inbox[0].Title).IsEqualTo("後で整理");
            await Assert.That(new TodoGraph(loaded).Progress().Total).IsEqualTo(1);
            var copy = project.DeepClone();
            copy.Inbox[0].Title = "変更"; copy.Nodes[0].Checklist[0].IsChecked = false;
            await Assert.That(project.Inbox[0].Title).IsEqualTo("後で整理");
            await Assert.That(node.Checklist[0].IsChecked).IsTrue();
            var template = BranchTemplate.Instantiate(project, 0, 0);
            await Assert.That(template.Nodes[0].Checklist[0].IsChecked).IsFalse();
            await Assert.That(template.Nodes[0].IsManuallyBlocked).IsFalse();
            await Assert.That(node.IsManuallyBlocked).IsTrue();
            project.SchemaVersion = 8;
            File.WriteAllText(path, JsonSerializer.Serialize(project, JsonProjectStore.SerializerOptions));
            var rejected = false;
            try { store.Load(path); } catch (InvalidDataException) { rejected = true; }
            await Assert.That(rejected).IsTrue();
            project.Inbox.Clear(); node.Checklist.Clear(); node.IsManuallyBlocked = false; node.BlockReason = "";
            File.WriteAllText(path, JsonSerializer.Serialize(project, JsonProjectStore.SerializerOptions));
            await Assert.That(store.Load(path).SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
}
