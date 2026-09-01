using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public static class LayoutTests
{
    public static void Register()
    {
        MiniTest.Case("レイヤは先行より必ず後ろになる", () =>
        {
            var project = SampleProject.Create();
            var graph = new TodoGraph(project);
            var layers = LayeredLayoutEngine.ComputeLayers(graph);

            foreach (var edge in graph.Edges)
            {
                MiniTest.True(layers[edge.FromId] < layers[edge.ToId], "先行のレイヤの方が小さい");
            }
        });

        MiniTest.Case("合流ノードは一番遅い先行の次に置かれる", () =>
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
            MiniTest.Equal(3, layers[merged.Id], "合流のレイヤ");
        });

        MiniTest.Case("左→右レイアウトで先行が左に来る", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            LayeredLayoutEngine.Apply(graph, new LayoutOptions { Direction = LayoutDirection.LeftToRight });
            MiniTest.True(a.X < b.X && b.X < c.X, "X が増えていく");
        });

        MiniTest.Case("上→下レイアウトで先行が上に来る", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            LayeredLayoutEngine.Apply(graph, new LayoutOptions { Direction = LayoutDirection.TopToBottom });
            MiniTest.True(a.Y < b.Y && b.Y < c.Y, "Y が増えていく");
        });

        MiniTest.Case("ピン留めしたノードは動かない", () =>
        {
            var (graph, _, b, _) = GraphTests.Chain();
            b.IsPinned = true;
            b.X = 1234;
            b.Y = 5678;
            LayeredLayoutEngine.Apply(graph);
            MiniTest.Equal(1234d, b.X, "X は保持される");
            MiniTest.Equal(5678d, b.Y, "Y は保持される");
        });

        MiniTest.Case("同じレイヤのノードが重ならない", () =>
        {
            var project = SampleProject.Create();
            var graph = new TodoGraph(project);
            LayeredLayoutEngine.Apply(graph);

            var byColumn = graph.Nodes.GroupBy(n => Math.Round(n.X));
            foreach (var column in byColumn)
            {
                var ys = column.Select(n => Math.Round(n.Y)).ToList();
                MiniTest.Equal(ys.Count, ys.Distinct().Count(), "同じ列で Y が重複しない");
            }
        });

        MiniTest.Case("座標が有限の値になる", () =>
        {
            var project = SampleProject.Create();
            var graph = new TodoGraph(project);
            LayeredLayoutEngine.Apply(graph);
            foreach (var node in graph.Nodes)
            {
                MiniTest.True(double.IsFinite(node.X) && double.IsFinite(node.Y), $"{node.Title} の座標が有限");
            }
        });
    }
}
