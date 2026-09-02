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

    [Test]
    [DisplayName("左→右のカード表示は、これまでの間隔のまま")]
    public async Task Metrics_CardLeftToRight_KeepsPreviousSpacing()
    {
        var options = NodeStyleMetrics.LayoutFor(NodeStyle.Card, LayoutDirection.LeftToRight);
        await Assert.That(options.LayerSpacing).IsEqualTo(290d).Because("レイヤ間");
        await Assert.That(options.NodeSpacing).IsEqualTo(128d).Because("同レイヤ内");
    }

    [Test]
    [DisplayName("どの表示モード・向きでも、間隔が箱より広い")]
    public async Task Metrics_SpacingAlwaysExceedsBox()
    {
        foreach (var style in new[] { NodeStyle.Card, NodeStyle.Minimal })
        {
            foreach (var direction in new[] { LayoutDirection.LeftToRight, LayoutDirection.TopToBottom })
            {
                var options = NodeStyleMetrics.LayoutFor(style, direction);
                var horizontal = direction == LayoutDirection.LeftToRight;
                var along = horizontal ? NodeStyleMetrics.WidthOf(style) : NodeStyleMetrics.HeightOf(style);
                var across = horizontal ? NodeStyleMetrics.HeightOf(style) : NodeStyleMetrics.WidthOf(style);

                await Assert.That(options.LayerSpacing > along).IsTrue().Because($"{style} / {direction} のレイヤ間");
                await Assert.That(options.NodeSpacing > across).IsTrue().Because($"{style} / {direction} の同レイヤ内");
            }
        }
    }

    [Test]
    [DisplayName("ミニマル表示はカードより密になる")]
    public async Task Metrics_MinimalIsDenserThanCard()
    {
        var card = NodeStyleMetrics.LayoutFor(NodeStyle.Card, LayoutDirection.LeftToRight);
        var minimal = NodeStyleMetrics.LayoutFor(NodeStyle.Minimal, LayoutDirection.LeftToRight);

        await Assert.That(minimal.LayerSpacing < card.LayerSpacing).IsTrue().Because("レイヤ間が狭い");
        await Assert.That(minimal.NodeSpacing < card.NodeSpacing).IsTrue().Because("同レイヤ内が狭い");
        await Assert.That(NodeStyleMetrics.HeightOf(NodeStyle.Minimal) < NodeStyleMetrics.HeightOf(NodeStyle.Card))
            .IsTrue().Because("箱が低い");
    }

    [Test]
    [DisplayName("上→下に並べても、横に並んだ箱が重ならない")]
    public async Task Apply_TopToBottom_KeepsSiblingsApart()
    {
        var graph = new TodoGraph(new TodoProject());
        var root = graph.AddNode(new TodoNode { Title = "起点" });
        for (var i = 0; i < 4; i++)
        {
            var child = graph.AddNode(new TodoNode { Title = $"並行 {i}" });
            graph.Connect(root.Id, child.Id);
        }

        LayeredLayoutEngine.Apply(graph, NodeStyleMetrics.LayoutFor(NodeStyle.Card, LayoutDirection.TopToBottom));

        var siblings = graph.Nodes.Where(n => n.Id != root.Id).OrderBy(n => n.X).ToList();
        var width = NodeStyleMetrics.WidthOf(NodeStyle.Card);

        for (var i = 1; i < siblings.Count; i++)
        {
            var gap = siblings[i].X - siblings[i - 1].X;
            await Assert.That(gap >= width).IsTrue().Because($"{siblings[i].Title} が左隣に重ならない");
        }
    }
}
