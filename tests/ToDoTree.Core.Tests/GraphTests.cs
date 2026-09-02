using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public class GraphTests
{
    [Test]
    [DisplayName("ノードを追加すると数が増える")]
    public async Task AddNode_IncreasesNodeCount()
    {
        var graph = new TodoGraph(new TodoProject());
        graph.AddNode(new TodoNode { Title = "A" });
        graph.AddNode(new TodoNode { Title = "B" });
        await Assert.That(graph.NodeCount).IsEqualTo(2).Because("ノード数");
    }

    [Test]
    [DisplayName("繋いだ辺が先行・後続に現れる")]
    public async Task Connect_ShowsUpInParentsAndChildren()
    {
        var (graph, a, b, _) = Chain();
        await Assert.That(graph.OutgoingOf(a.Id).Count).IsEqualTo(1).Because("A の後続の数");
        await Assert.That(graph.ChildrenOf(a.Id).Any(n => n.Id == b.Id)).IsTrue().Because("A の子に B がいる");
        await Assert.That(graph.ParentsOf(b.Id).Any(n => n.Id == a.Id)).IsTrue().Because("B の親に A がいる");
    }

    [Test]
    [DisplayName("自分自身には繋げない")]
    public async Task Connect_RejectsSelfLoop()
    {
        var (graph, a, _, _) = Chain();
        await Assert.That(graph.CanConnect(a.Id, a.Id)).IsEqualTo(ConnectionCheck.SameNode).Because("自己ループの判定");
        await Assert.That(graph.Connect(a.Id, a.Id) is null).IsTrue().Because("自己ループは作られない");
    }

    [Test]
    [DisplayName("同じ辺は二重に張れない")]
    public async Task Connect_RejectsDuplicateEdge()
    {
        var (graph, a, b, _) = Chain();
        await Assert.That(graph.CanConnect(a.Id, b.Id)).IsEqualTo(ConnectionCheck.Duplicate).Because("重複の判定");
    }

    [Test]
    [DisplayName("循環する接続は拒否される")]
    public async Task Connect_RejectsCycle()
    {
        var (graph, a, _, c) = Chain();
        await Assert.That(graph.CanConnect(c.Id, a.Id)).IsEqualTo(ConnectionCheck.WouldCreateCycle).Because("循環の判定");
        await Assert.That(graph.Connect(c.Id, a.Id) is null).IsTrue().Because("循環は作られない");
        await Assert.That(graph.HasCycle()).IsFalse().Because("グラフは DAG のまま");
    }

    [Test]
    [DisplayName("合流（複数の親）は許される")]
    public async Task Connect_AllowsMultipleParents()
    {
        var graph = new TodoGraph(new TodoProject());
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        var merged = graph.AddNode(new TodoNode { Title = "合流" });
        await Assert.That(graph.Connect(a.Id, merged.Id) is not null).IsTrue().Because("A から合流へ");
        await Assert.That(graph.Connect(b.Id, merged.Id) is not null).IsTrue().Because("B から合流へ");
        await Assert.That(graph.IncomingOf(merged.Id).Count).IsEqualTo(2).Because("親の数");
    }

    [Test]
    [DisplayName("ノードを消すと繋がっていた辺も消える")]
    public async Task RemoveNode_AlsoRemovesConnectedEdges()
    {
        var (graph, _, b, _) = Chain();
        graph.RemoveNode(b.Id);
        await Assert.That(graph.NodeCount).IsEqualTo(2).Because("残ったノード数");
        await Assert.That(graph.Edges.Count).IsEqualTo(0).Because("残った辺の数");
    }

    [Test]
    [DisplayName("途中のノードを消すと前後が繋ぎ直される")]
    public async Task RemoveNodeAndBridge_ReconnectsNeighbors()
    {
        var (graph, a, b, c) = Chain();
        graph.RemoveNodeAndBridge(b.Id);
        await Assert.That(graph.NodeCount).IsEqualTo(2).Because("残ったノード数");
        await Assert.That(graph.ChildrenOf(a.Id).Any(n => n.Id == c.Id)).IsTrue().Because("A と C が直結している");
    }

    [Test]
    [DisplayName("Rebuild で壊れた辺が捨てられる")]
    public async Task Rebuild_DropsDanglingEdges()
    {
        var project = new TodoProject();
        var node = new TodoNode { Title = "A" };
        project.Nodes.Add(node);
        project.Edges.Add(new TodoEdge { FromId = node.Id, ToId = Guid.NewGuid() });
        var graph = new TodoGraph(project);
        await Assert.That(graph.Edges.Count).IsEqualTo(0).Because("存在しない先を指す辺は消える");
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
