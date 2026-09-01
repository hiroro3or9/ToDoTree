using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public class AnalysisTests
{
    [Test]
    [DisplayName("トポロジカル順は先行が必ず前に来る")]
    public async Task TopologicalOrder_PutsPredecessorsFirst()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        var order = graph.TopologicalOrder();
        await Assert.That(order is not null).IsTrue().Because("順序が得られる");
        var ids = order!.Select(n => n.Id).ToList();
        await Assert.That(ids.IndexOf(a.Id) < ids.IndexOf(b.Id)).IsTrue().Because("A は B より前");
        await Assert.That(ids.IndexOf(b.Id) < ids.IndexOf(c.Id)).IsTrue().Because("B は C より前");
    }

    [Test]
    [DisplayName("先行が終わるまでは着手できない")]
    public async Task Readiness_BlockedUntilPredecessorDone()
    {
        var (graph, a, b, _) = GraphTests.Chain();
        await Assert.That(graph.ReadinessOf(a)).IsEqualTo(Readiness.Ready).Because("A は着手可能");
        await Assert.That(graph.ReadinessOf(b)).IsEqualTo(Readiness.Blocked).Because("B は待ち");

        a.Status = NodeStatus.Done;
        await Assert.That(graph.ReadinessOf(b)).IsEqualTo(Readiness.Ready).Because("A 完了後は B が着手可能");
    }

    [Test]
    [DisplayName("取り消した先行は待ちの理由にならない")]
    public async Task Readiness_IgnoresCancelledPredecessor()
    {
        var (graph, a, b, _) = GraphTests.Chain();
        a.Status = NodeStatus.Cancelled;
        await Assert.That(graph.ReadinessOf(b)).IsEqualTo(Readiness.Ready).Because("取り消し後は B が着手可能");
    }

    [Test]
    [DisplayName("着手可能な一覧が取れる")]
    public async Task ReadyNodes_ListsActionableNodes()
    {
        var (graph, a, _, _) = GraphTests.Chain();
        var ready = graph.ReadyNodes().ToList();
        await Assert.That(ready.Count).IsEqualTo(1).Because("着手可能の数");
        await Assert.That(ready[0].Id).IsEqualTo(a.Id).Because("着手可能なのは A");
    }

    [Test]
    [DisplayName("上流と下流をたどれる")]
    public async Task AncestorsAndDescendants_Traverse()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        await Assert.That(graph.Ancestors(c.Id).SetEquals([a.Id, b.Id])).IsTrue().Because("C の上流は A と B");
        await Assert.That(graph.Descendants(a.Id).SetEquals([b.Id, c.Id])).IsTrue().Because("A の下流は B と C");
    }

    [Test]
    [DisplayName("進捗が数えられる")]
    public async Task Progress_CountsStatuses()
    {
        var (graph, a, b, _) = GraphTests.Chain();
        a.Status = NodeStatus.Done;
        b.Status = NodeStatus.InProgress;
        var progress = graph.Progress();
        await Assert.That(progress.Total).IsEqualTo(3).Because("母数");
        await Assert.That(progress.Done).IsEqualTo(1).Because("完了数");
        await Assert.That(progress.InProgress).IsEqualTo(1).Because("進行中の数");
        await Assert.That(progress.Blocked).IsEqualTo(1).Because("待ちの数");
        await Assert.That(Math.Abs(progress.Percent - 100d / 3) < 0.001).IsTrue().Because("進捗率");
    }

    [Test]
    [DisplayName("取り消しは進捗の母数から外れる")]
    public async Task Progress_ExcludesCancelled()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        a.Status = NodeStatus.Done;
        b.Status = NodeStatus.Cancelled;
        c.Status = NodeStatus.Done;
        var progress = graph.Progress();
        await Assert.That(progress.Total).IsEqualTo(2).Because("母数");
        await Assert.That(progress.Done).IsEqualTo(2).Because("完了数");
        await Assert.That(progress.Percent).IsEqualTo(100d).Because("進捗率");
    }

    [Test]
    [DisplayName("クリティカルパスは一番重い鎖を返す")]
    public async Task CriticalPath_ReturnsHeaviestChain()
    {
        var graph = new TodoGraph(new TodoProject());
        var start = graph.AddNode(new TodoNode { Title = "開始", EstimateMinutes = 10 });
        var quick = graph.AddNode(new TodoNode { Title = "短い道", EstimateMinutes = 10 });
        var slow = graph.AddNode(new TodoNode { Title = "長い道", EstimateMinutes = 600 });
        var goal = graph.AddNode(new TodoNode { Title = "ゴール", EstimateMinutes = 10 });
        graph.Connect(start.Id, quick.Id);
        graph.Connect(start.Id, slow.Id);
        graph.Connect(quick.Id, goal.Id);
        graph.Connect(slow.Id, goal.Id);

        var path = graph.CriticalPath().Select(n => n.Id).ToList();
        await Assert.That(path.Count).IsEqualTo(3).Because("経路の長さ");
        await Assert.That(path.Contains(slow.Id)).IsTrue().Because("長い道を通る");
        await Assert.That(path.Contains(quick.Id)).IsFalse().Because("短い道は通らない");
    }
}
