using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public static class AnalysisTests
{
    public static void Register()
    {
        MiniTest.Case("トポロジカル順は先行が必ず前に来る", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            var order = graph.TopologicalOrder();
            MiniTest.True(order is not null, "順序が得られる");
            var ids = order!.Select(n => n.Id).ToList();
            MiniTest.True(ids.IndexOf(a.Id) < ids.IndexOf(b.Id), "A は B より前");
            MiniTest.True(ids.IndexOf(b.Id) < ids.IndexOf(c.Id), "B は C より前");
        });

        MiniTest.Case("先行が終わるまでは着手できない", () =>
        {
            var (graph, a, b, _) = GraphTests.Chain();
            MiniTest.Equal(Readiness.Ready, graph.ReadinessOf(a), "A は着手可能");
            MiniTest.Equal(Readiness.Blocked, graph.ReadinessOf(b), "B は待ち");

            a.Status = NodeStatus.Done;
            MiniTest.Equal(Readiness.Ready, graph.ReadinessOf(b), "A 完了後は B が着手可能");
        });

        MiniTest.Case("取り消した先行は待ちの理由にならない", () =>
        {
            var (graph, a, b, _) = GraphTests.Chain();
            a.Status = NodeStatus.Cancelled;
            MiniTest.Equal(Readiness.Ready, graph.ReadinessOf(b), "取り消し後は B が着手可能");
        });

        MiniTest.Case("着手可能な一覧が取れる", () =>
        {
            var (graph, a, _, _) = GraphTests.Chain();
            var ready = graph.ReadyNodes().ToList();
            MiniTest.Equal(1, ready.Count, "着手可能の数");
            MiniTest.Equal(a.Id, ready[0].Id, "着手可能なのは A");
        });

        MiniTest.Case("上流と下流をたどれる", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            MiniTest.True(graph.Ancestors(c.Id).SetEquals([a.Id, b.Id]), "C の上流は A と B");
            MiniTest.True(graph.Descendants(a.Id).SetEquals([b.Id, c.Id]), "A の下流は B と C");
        });

        MiniTest.Case("進捗が数えられる", () =>
        {
            var (graph, a, b, _) = GraphTests.Chain();
            a.Status = NodeStatus.Done;
            b.Status = NodeStatus.InProgress;
            var progress = graph.Progress();
            MiniTest.Equal(3, progress.Total, "母数");
            MiniTest.Equal(1, progress.Done, "完了数");
            MiniTest.Equal(1, progress.InProgress, "進行中の数");
            MiniTest.Equal(1, progress.Blocked, "待ちの数");
            MiniTest.True(Math.Abs(progress.Percent - 100d / 3) < 0.001, "進捗率");
        });

        MiniTest.Case("取り消しは進捗の母数から外れる", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            a.Status = NodeStatus.Done;
            b.Status = NodeStatus.Cancelled;
            c.Status = NodeStatus.Done;
            var progress = graph.Progress();
            MiniTest.Equal(2, progress.Total, "母数");
            MiniTest.Equal(2, progress.Done, "完了数");
            MiniTest.Equal(100d, progress.Percent, "進捗率");
        });

        MiniTest.Case("クリティカルパスは一番重い鎖を返す", () =>
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
            MiniTest.Equal(3, path.Count, "経路の長さ");
            MiniTest.True(path.Contains(slow.Id), "長い道を通る");
            MiniTest.False(path.Contains(quick.Id), "短い道は通らない");
        });
    }
}
