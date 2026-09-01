using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public static class GraphTests
{
    public static void Register()
    {
        MiniTest.Case("ノードを追加すると数が増える", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            graph.AddNode(new TodoNode { Title = "A" });
            graph.AddNode(new TodoNode { Title = "B" });
            MiniTest.Equal(2, graph.NodeCount, "ノード数");
        });

        MiniTest.Case("繋いだ辺が先行・後続に現れる", () =>
        {
            var (graph, a, b, _) = Chain();
            MiniTest.Equal(1, graph.OutgoingOf(a.Id).Count, "A の後続の数");
            MiniTest.True(graph.ChildrenOf(a.Id).Any(n => n.Id == b.Id), "A の子に B がいる");
            MiniTest.True(graph.ParentsOf(b.Id).Any(n => n.Id == a.Id), "B の親に A がいる");
        });

        MiniTest.Case("自分自身には繋げない", () =>
        {
            var (graph, a, _, _) = Chain();
            MiniTest.Equal(ConnectionCheck.SameNode, graph.CanConnect(a.Id, a.Id), "自己ループの判定");
            MiniTest.True(graph.Connect(a.Id, a.Id) is null, "自己ループは作られない");
        });

        MiniTest.Case("同じ辺は二重に張れない", () =>
        {
            var (graph, a, b, _) = Chain();
            MiniTest.Equal(ConnectionCheck.Duplicate, graph.CanConnect(a.Id, b.Id), "重複の判定");
        });

        MiniTest.Case("循環する接続は拒否される", () =>
        {
            var (graph, a, _, c) = Chain();
            MiniTest.Equal(ConnectionCheck.WouldCreateCycle, graph.CanConnect(c.Id, a.Id), "循環の判定");
            MiniTest.True(graph.Connect(c.Id, a.Id) is null, "循環は作られない");
            MiniTest.False(graph.HasCycle(), "グラフは DAG のまま");
        });

        MiniTest.Case("合流（複数の親）は許される", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var a = graph.AddNode(new TodoNode { Title = "A" });
            var b = graph.AddNode(new TodoNode { Title = "B" });
            var merged = graph.AddNode(new TodoNode { Title = "合流" });
            MiniTest.True(graph.Connect(a.Id, merged.Id) is not null, "A から合流へ");
            MiniTest.True(graph.Connect(b.Id, merged.Id) is not null, "B から合流へ");
            MiniTest.Equal(2, graph.IncomingOf(merged.Id).Count, "親の数");
        });

        MiniTest.Case("ノードを消すと繋がっていた辺も消える", () =>
        {
            var (graph, _, b, _) = Chain();
            graph.RemoveNode(b.Id);
            MiniTest.Equal(2, graph.NodeCount, "残ったノード数");
            MiniTest.Equal(0, graph.Edges.Count, "残った辺の数");
        });

        MiniTest.Case("途中のノードを消すと前後が繋ぎ直される", () =>
        {
            var (graph, a, b, c) = Chain();
            graph.RemoveNodeAndBridge(b.Id);
            MiniTest.Equal(2, graph.NodeCount, "残ったノード数");
            MiniTest.True(graph.ChildrenOf(a.Id).Any(n => n.Id == c.Id), "A と C が直結している");
        });

        MiniTest.Case("Rebuild で壊れた辺が捨てられる", () =>
        {
            var project = new TodoProject();
            var node = new TodoNode { Title = "A" };
            project.Nodes.Add(node);
            project.Edges.Add(new TodoEdge { FromId = node.Id, ToId = Guid.NewGuid() });
            var graph = new TodoGraph(project);
            MiniTest.Equal(0, graph.Edges.Count, "存在しない先を指す辺は消える");
        });
    }

    internal static (TodoGraph Graph, TodoNode A, TodoNode B, TodoNode C) Chain()
    {
        var graph = new TodoGraph(new TodoProject());
        var a = graph.AddNode(new TodoNode { Title = "A", Kind = NodeKind.Start });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        var c = graph.AddNode(new TodoNode { Title = "C", Kind = NodeKind.Goal });
        graph.Connect(a.Id, b.Id);
        graph.Connect(b.Id, c.Id);
        return (graph, a, b, c);
    }
}
