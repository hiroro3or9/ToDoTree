using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class ChoiceAndSpecimenTests
{
    private static (TodoGraph Graph, TodoNode Source, TodoNode A, TodoNode B, TodoNode Goal, TodoEdge Selected) Example()
    {
        var source = new TodoNode { Title = "作り方", IsChoice = true, Status = NodeStatus.Done };
        var a = new TodoNode { Title = "自作する" };
        var b = new TodoNode { Title = "既製品を使う", EstimateMinutes = 10000 };
        var goal = new TodoNode { Title = "完成", Kind = NodeKind.Goal };
        var graph = new TodoGraph(new TodoProject { Nodes = [source, a, b, goal] });
        var selected = graph.Connect(source.Id, a.Id)!;
        graph.Connect(source.Id, b.Id)!.DecisionReason = "仕組みを学ぶため今回は見送り";
        graph.Connect(a.Id, goal.Id); graph.Connect(b.Id, goal.Id);
        return (graph, source, a, b, goal, selected);
    }

    [Test]
    public async Task PendingChoice_GatesBothPathsAndJoin()
    {
        var (g, _, a, b, goal, _) = Example();
        await Assert.That(g.ReadinessOf(a)).IsEqualTo(Readiness.Blocked);
        await Assert.That(g.ReadinessOf(b)).IsEqualTo(Readiness.Blocked);
        await Assert.That(g.BranchStateOf(goal.Id)).IsEqualTo(BranchState.Pending);
        await Assert.That(NextActionPlanner.Suggest(g).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChosenPath_UnlocksJoinAndExcludesSkippedEstimateWithoutChangingStatus()
    {
        var (g, source, a, b, goal, edge) = Example();
        source.SelectedChoiceEdgeId = edge.Id; g.Rebuild();
        await Assert.That(g.ReadinessOf(a)).IsEqualTo(Readiness.Ready);
        await Assert.That(g.ReadinessOf(b)).IsEqualTo(Readiness.Cancelled);
        await Assert.That(b.Status).IsEqualTo(NodeStatus.NotStarted);
        await Assert.That(g.Progress().Total).IsEqualTo(3);
        await Assert.That(g.NodesUnlockedBy(a.Id).Single().Id).IsEqualTo(goal.Id);
        await Assert.That(CompletionImpact.Calculate(g, [a.Id]).Unlocked.Contains(goal.Id)).IsTrue();
        a.Status = NodeStatus.Done;
        await Assert.That(g.ReadinessOf(goal)).IsEqualTo(Readiness.Ready);
        await Assert.That(BlockerAnalysis.Find(g, goal.Id).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangingChoice_RetainsWorkAndCycleChecks()
    {
        var (g, source, a, b, goal, edge) = Example();
        source.SelectedChoiceEdgeId = edge.Id; a.Status = NodeStatus.Done; g.Rebuild();
        source.SelectedChoiceEdgeId = g.Project.Edges.Single(e => e.ToId == b.Id).Id; g.Rebuild();
        await Assert.That(a.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(g.ReadinessOf(b)).IsEqualTo(Readiness.Ready);
        await Assert.That(g.ReadinessOf(goal)).IsEqualTo(Readiness.Blocked);
        await Assert.That(g.CanConnect(goal.Id, a.Id)).IsEqualTo(ConnectionCheck.WouldCreateCycle);
        g.RemoveNode(b.Id);
        await Assert.That(source.SelectedChoiceEdgeId).IsNull();
        await Assert.That(ChoiceService.Validate(g.Project, 12)).IsNull();
    }

    [Test]
    public async Task SharedDescendant_RemainsActiveAndNestedChoiceRemainsPending()
    {
        var (g, source, a, b, goal, edge) = Example();
        var shared = g.AddNode(new TodoNode { Title = "共通処理", IsChoice = true });
        var nested = g.AddNode(new TodoNode { Title = "次の選択" });
        g.Connect(a.Id, shared.Id); g.Connect(b.Id, shared.Id); g.Connect(shared.Id, nested.Id);
        source.SelectedChoiceEdgeId = edge.Id; g.Rebuild();
        await Assert.That(g.BranchStateOf(shared.Id)).IsEqualTo(BranchState.Active);
        await Assert.That(g.BranchStateOf(nested.Id)).IsEqualTo(BranchState.Pending);
        await Assert.That(g.BranchStateOf(goal.Id)).IsEqualTo(BranchState.Active);
    }

    [Test]
    public async Task ExternalPrerequisite_DoesNotReactivateRejectedPath()
    {
        var (g, source, _, b, goal, edge) = Example();
        var external = g.AddNode(new TodoNode { Title = "共通の前提", Status = NodeStatus.Done });
        var downstream = g.AddNode(new TodoNode { Title = "購入後の作業" });
        g.Connect(external.Id, b.Id); g.Connect(b.Id, downstream.Id); g.Connect(external.Id, downstream.Id);
        source.SelectedChoiceEdgeId = edge.Id; g.Rebuild();
        await Assert.That(g.BranchStateOf(b.Id)).IsEqualTo(BranchState.Skipped);
        await Assert.That(g.BranchStateOf(downstream.Id)).IsEqualTo(BranchState.Skipped);
        await Assert.That(g.BranchStateOf(goal.Id)).IsEqualTo(BranchState.Active);
    }

    [Test]
    public async Task RejectedNestedChoice_DoesNotGateCommonGoalAndInsertionKeepsDecision()
    {
        var (g, source, a, b, goal, edge) = Example();
        b.IsChoice = true;
        source.SelectedChoiceEdgeId = edge.Id; g.Rebuild();
        await Assert.That(g.BranchStateOf(goal.Id)).IsEqualTo(BranchState.Active);
        edge.DecisionReason = "学びたい";
        var inserted = EdgeInserter.InsertBetween(g, source.Id, a.Id, new TodoNode())!;
        await Assert.That(g.BranchStateOf(inserted.Id)).IsEqualTo(BranchState.Active);
        await Assert.That(source.SelectedChoiceEdgeId).IsEqualTo(edge.Id);
        await Assert.That(g.Project.Edges.Single(e => e.ToId == inserted.Id).DecisionReason).IsEqualTo("学びたい");
    }

    [Test]
    public async Task BlockAlternative_ExpandsChoiceAndRetainsReason()
    {
        var (g, source, a, b, goal, edge) = Example();
        var extra = g.AddNode(new TodoNode { Title = "購入後の確認" });
        var block = BlockService.Create(g.Project, [b.Id, extra.Id], "購入案").Block!;
        g.Disconnect(source.Id, b.Id);
        var alternative = g.Connect(source.Id, block.Id)!; alternative.DecisionReason = "学習を優先";
        source.SelectedChoiceEdgeId = edge.Id; a.Status = goal.Status = NodeStatus.Done; g.Rebuild();
        var specimen = SpecimenService.Capture(g, goal.Id, "学んだこと", "理解できた");
        await Assert.That(g.BranchStateOf(b.Id)).IsEqualTo(BranchState.Skipped);
        await Assert.That(specimen.Edges.Any(e => e.DecisionReason == "学習を優先")).IsTrue();
    }

    [Test]
    public async Task Capture_FreezesGraphReasonsVariablesAndSurvivesDeletionAndReload()
    {
        var (g, source, a, b, goal, edge) = Example();
        g.Project.Variables.Add(new ProjectVariable { Name = "作品", Value = "時計" });
        goal.Title = "{作品}が完成";
        source.SelectedChoiceEdgeId = edge.Id; a.Status = goal.Status = NodeStatus.Done;
        goal.CompletedAt = DateTimeOffset.Now; g.Rebuild();
        var specimen = SpecimenService.Capture(g, goal.Id, "作ったもの", "配線を学んだ");
        g.Project.Specimens.Add(specimen);
        var clone = g.Project.DeepClone();
        clone.Specimens[0].Nodes[0].Title = "変更";
        await Assert.That(specimen.Nodes[0].Title).IsNotEqualTo("変更");
        g.RemoveNode(goal.Id); g.RemoveNode(b.Id);
        g.Project.Variables[0].Value = "別の作品";
        var path = Path.Combine(Path.GetTempPath(), $"specimen-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore(); store.Save(path, g.Project);
            var restored = store.Load(path).Specimens.Single();
            await Assert.That(restored.Title).IsEqualTo("時計が完成");
            await Assert.That(restored.Nodes.Count).IsEqualTo(4);
            await Assert.That(restored.SkippedNodeIds.Contains(b.Id)).IsTrue();
            await Assert.That(restored.Reflection).IsEqualTo("配線を学んだ");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Test]
    public async Task Capture_RejectsUnfinishedPathAndDuplicateAchievement()
    {
        var (g, source, a, _, goal, edge) = Example();
        source.SelectedChoiceEdgeId = edge.Id; goal.Status = NodeStatus.Done; goal.CompletedAt = DateTimeOffset.Now;
        bool Rejected()
        {
            try { SpecimenService.Capture(g, goal.Id, "学んだこと", ""); return false; }
            catch (InvalidOperationException) { return true; }
        }
        await Assert.That(Rejected()).IsTrue();
        a.Status = NodeStatus.Done;
        g.Project.Specimens.Add(SpecimenService.Capture(g, goal.Id, "学んだこと", ""));
        await Assert.That(Rejected()).IsTrue();
    }

    [Test]
    public async Task Template_ResetsDecisionAndDoesNotCopyAlbum()
    {
        var (g, source, a, _, goal, edge) = Example();
        source.SelectedChoiceEdgeId = edge.Id; a.Status = goal.Status = NodeStatus.Done;
        g.Project.Specimens.Add(SpecimenService.Capture(g, goal.Id, "学んだこと", ""));
        var copy = BranchTemplate.Capture(g.Project, g.Nodes.Select(n => n.Id), "部品");
        await Assert.That(copy.Nodes.Single(n => n.IsChoice).SelectedChoiceEdgeId).IsNull();
        await Assert.That(copy.Specimens.Count).IsEqualTo(0);
        await Assert.That(copy.Edges.All(e => e.DecisionReason.Length == 0)).IsTrue();
        await Assert.That(ChoiceService.Validate(copy, 12)).IsNull();
    }

    [Test]
    public async Task SkippedPathExternalWork_DoesNotBlockCaptureOrEnterCriticalPath()
    {
        var (g, source, a, b, goal, edge) = Example();
        var external = g.AddNode(new TodoNode { Title = "購入だけに必要な準備", EstimateMinutes = 20000 });
        g.Connect(external.Id, b.Id);
        source.SelectedChoiceEdgeId = edge.Id; a.Status = goal.Status = NodeStatus.Done; g.Rebuild();
        var specimen = SpecimenService.Capture(g, goal.Id, "作ったもの", "");
        await Assert.That(specimen.Nodes.Any(n => n.Id == external.Id)).IsFalse();
        await Assert.That(specimen.SkippedNodeIds.Contains(b.Id)).IsTrue();
        await Assert.That(g.CriticalPath().Any(n => n.Id == b.Id)).IsFalse();
    }

    [Test]
    public async Task Choice_RoundTripRetainsSelectionReasonsAndReadiness()
    {
        var (g, source, a, b, _, edge) = Example();
        source.SelectedChoiceEdgeId = edge.Id;
        var path = Path.Combine(Path.GetTempPath(), $"choice-roundtrip-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore(); store.Save(path, g.Project);
            var restored = new TodoGraph(store.Load(path));
            await Assert.That(restored.Find(source.Id)!.SelectedChoiceEdgeId).IsEqualTo(edge.Id);
            await Assert.That(restored.ReadinessOf(restored.Find(a.Id)!)).IsEqualTo(Readiness.Ready);
            await Assert.That(restored.ReadinessOf(restored.Find(b.Id)!)).IsEqualTo(Readiness.Cancelled);
            await Assert.That(restored.Project.Edges.Single(e => e.ToId == b.Id).DecisionReason).IsEqualTo("仕組みを学ぶため今回は見送り");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Test]
    public async Task Storage_RejectsInvalidSelectionAndReadsLegacyWithoutInventingChoices()
    {
        var (g, source, _, _, _, _) = Example();
        source.SelectedChoiceEdgeId = Guid.NewGuid();
        await Assert.That(ChoiceService.Validate(g.Project, 12)).IsNotNull();
        await Assert.That(ChoiceService.Validate(g.Project, 11)).IsNotNull();
        var path = Path.Combine(Path.GetTempPath(), $"legacy-choice-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":11,\"nodes\":[],\"edges\":[]}");
            var restored = new JsonProjectStore().Load(path);
            await Assert.That(restored.Specimens.Count).IsEqualTo(0);
            await Assert.That(restored.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
