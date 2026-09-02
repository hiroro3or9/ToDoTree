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

    [Test]
    [DisplayName("線のあいだにステップを挟むと、流れが繋がったままになる")]
    public async Task InsertBetween_KeepsTheChainConnected()
    {
        var (graph, a, b, _) = Chain();
        var inserted = EdgeInserter.InsertBetween(graph, a.Id, b.Id, new TodoNode { Title = "途中" });

        await Assert.That(inserted is null).IsFalse().Because("挟めた");
        await Assert.That(graph.ChildrenOf(a.Id).Any(n => n.Id == inserted!.Id)).IsTrue().Because("A の次が新しいステップ");
        await Assert.That(graph.ChildrenOf(inserted!.Id).Any(n => n.Id == b.Id)).IsTrue().Because("新しいステップの次が B");
        await Assert.That(graph.ChildrenOf(a.Id).Any(n => n.Id == b.Id)).IsFalse().Because("元の A→B は外れている");
        await Assert.That(graph.HasCycle()).IsFalse().Because("グラフは DAG のまま");
    }

    [Test]
    [DisplayName("挟んだステップは 2 つの中点に置かれる")]
    public async Task InsertBetween_PlacesTheNodeAtTheMidpoint()
    {
        var (graph, a, b, _) = Chain();
        a.X = 100;
        a.Y = 40;
        b.X = 400;
        b.Y = 240;

        var inserted = EdgeInserter.InsertBetween(graph, a.Id, b.Id, new TodoNode { Title = "途中" });

        await Assert.That(inserted!.X).IsEqualTo(250d).Because("X は中点");
        await Assert.That(inserted.Y).IsEqualTo(140d).Because("Y は中点");
    }

    [Test]
    [DisplayName("線のラベルは、後ろ半分に引き継がれる")]
    public async Task InsertBetween_MovesTheLabelToTheSecondHalf()
    {
        var graph = new TodoGraph(new TodoProject());
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        graph.Connect(a.Id, b.Id, "レビュー後");

        var inserted = EdgeInserter.InsertBetween(graph, a.Id, b.Id, new TodoNode { Title = "途中" });

        await Assert.That(graph.OutgoingOf(a.Id)[0].Label is null).IsTrue().Because("前半にラベルは付かない");
        await Assert.That(graph.OutgoingOf(inserted!.Id)[0].Label).IsEqualTo("レビュー後").Because("後半が引き継ぐ");
    }

    [Test]
    [DisplayName("繋がっていない 2 つのあいだには挟めない")]
    public async Task InsertBetween_DoesNothingWithoutAnEdge()
    {
        var (graph, a, _, c) = Chain();
        var before = graph.NodeCount;

        var inserted = EdgeInserter.InsertBetween(graph, a.Id, c.Id, new TodoNode { Title = "途中" });

        await Assert.That(inserted is null).IsTrue().Because("挟めない");
        await Assert.That(graph.NodeCount).IsEqualTo(before).Because("ノードは増えない");
    }
}
