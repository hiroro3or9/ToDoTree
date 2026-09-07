using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public class CompletionImpactTests
{
    [Test]
    public async Task Preview_UnlocksOnlyImmediateSuccessorWithoutMutation()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        var impact = CompletionImpact.Calculate(graph, [a.Id]);
        await Assert.That(impact.Unlocked.SetEquals([b.Id])).IsTrue();
        await Assert.That(a.Status).IsEqualTo(NodeStatus.NotStarted);
        await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Blocked);
    }

    [Test]
    public async Task Join_RequiresLastPredecessorOrBatch()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        graph.Connect(a.Id, c.Id);
        await Assert.That(CompletionImpact.Calculate(graph, [a.Id]).Unlocked.SetEquals([b.Id])).IsTrue();
        await Assert.That(CompletionImpact.Calculate(graph, [b.Id]).Unlocked.Count).IsEqualTo(0);
        await Assert.That(CompletionImpact.Calculate(graph, [a.Id, b.Id]).Unlocked.SetEquals([c.Id])).IsTrue();
        a.Status = NodeStatus.Cancelled;
        await Assert.That(CompletionImpact.Calculate(graph, [b.Id]).Unlocked.SetEquals([c.Id])).IsTrue();
    }

    [Test]
    public async Task SettledAndUnknownSources_DoNotUnlock()
    {
        var (graph, a, _, _) = GraphTests.Chain();
        a.Status = NodeStatus.Done;
        var impact = CompletionImpact.Calculate(graph, [a.Id, Guid.NewGuid()]);
        await Assert.That(impact.Sources.Count).IsEqualTo(0);
        await Assert.That(impact.Unlocked.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ActiveOrSettledSuccessors_AreNotNewlyReady()
    {
        var (graph, a, b, _) = GraphTests.Chain();
        foreach (var status in new[] { NodeStatus.InProgress, NodeStatus.Done, NodeStatus.Cancelled })
        {
            b.Status = status;
            await Assert.That(CompletionImpact.Calculate(graph, [a.Id]).Unlocked.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Fork_UnlocksAllBranchesAndDeduplicatesSources()
    {
        var (graph, a, b, _) = GraphTests.Chain();
        var extra = graph.AddNode(new TodoNode { Title = "別の枝" });
        graph.Connect(a.Id, extra.Id);
        var impact = CompletionImpact.Calculate(graph, [a.Id, a.Id]);
        await Assert.That(impact.Sources.Count).IsEqualTo(1);
        await Assert.That(impact.Unlocked.SetEquals([b.Id, extra.Id])).IsTrue();
    }
}
