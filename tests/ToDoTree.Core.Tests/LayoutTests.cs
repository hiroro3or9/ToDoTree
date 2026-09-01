using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class LayoutTests
{
    [Test]
    [DisplayName("レイヤは先行より必ず後ろになる")]
    public async Task ComputeLayers_PredecessorAlwaysEarlier()
    {
        var project = SampleProject.Create();
        var graph = new TodoGraph(project);
        var layers = LayeredLayoutEngine.ComputeLayers(graph);

        foreach (var edge in graph.Edges)
        {
            await Assert.That(layers[edge.FromId] < layers[edge.ToId]).IsTrue().Because("先行のレイヤの方が小さい");
        }
    }

    [Test]
    [DisplayName("合流ノードは一番遅い先行の次に置かれる")]
    public async Task ComputeLayers_MergeFollowsLatestPredecessor()
    {
        var graph = new TodoGraph(new TodoProject());
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        var c = graph.AddNode(new TodoNode { Title = "C" });
        var merged = graph.AddNode(new TodoNode { Title = "合流" });
        graph.Connect(a.Id, b.Id);
        graph.Connect(b.Id, c.Id);
        graph.Connect(c.Id, merged.Id);
        graph.Connect(a.Id, merged.Id);

        var layers = LayeredLayoutEngine.ComputeLayers(graph);
        await Assert.That(layers[merged.Id]).IsEqualTo(3).Because("合流のレイヤ");
    }

    [Test]
    [DisplayName("左→右レイアウトで先行が左に来る")]
    public async Task Apply_LeftToRight_PredecessorOnLeft()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        LayeredLayoutEngine.Apply(graph, new LayoutOptions { Direction = LayoutDirection.LeftToRight });
        await Assert.That(a.X < b.X && b.X < c.X).IsTrue().Because("X が増えていく");
    }

    [Test]
    [DisplayName("上→下レイアウトで先行が上に来る")]
    public async Task Apply_TopToBottom_PredecessorOnTop()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        LayeredLayoutEngine.Apply(graph, new LayoutOptions { Direction = LayoutDirection.TopToBottom });
        await Assert.That(a.Y < b.Y && b.Y < c.Y).IsTrue().Because("Y が増えていく");
    }

    [Test]
    [DisplayName("ピン留めしたノードは動かない")]
    public async Task Apply_KeepsPinnedNodesInPlace()
    {
        var (graph, _, b, _) = GraphTests.Chain();
        b.IsPinned = true;
        b.X = 1234;
        b.Y = 5678;
        LayeredLayoutEngine.Apply(graph);
        await Assert.That(b.X).IsEqualTo(1234d).Because("X は保持される");
        await Assert.That(b.Y).IsEqualTo(5678d).Because("Y は保持される");
    }

    [Test]
    [DisplayName("同じレイヤのノードが重ならない")]
    public async Task Apply_NoOverlapWithinColumn()
    {
        var project = SampleProject.Create();
        var graph = new TodoGraph(project);
        LayeredLayoutEngine.Apply(graph);

        var byColumn = graph.Nodes.GroupBy(n => Math.Round(n.X));
        foreach (var column in byColumn)
        {
            var ys = column.Select(n => Math.Round(n.Y)).ToList();
            await Assert.That(ys.Distinct().Count()).IsEqualTo(ys.Count).Because("同じ列で Y が重複しない");
        }
    }

    [Test]
    [DisplayName("座標が有限の値になる")]
    public async Task Apply_ProducesFiniteCoordinates()
    {
        var project = SampleProject.Create();
        var graph = new TodoGraph(project);
        LayeredLayoutEngine.Apply(graph);
        foreach (var node in graph.Nodes)
        {
            await Assert.That(double.IsFinite(node.X) && double.IsFinite(node.Y)).IsTrue().Because($"{node.Title} の座標が有限");
        }
    }
}
