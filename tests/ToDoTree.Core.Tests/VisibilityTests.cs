using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public static class VisibilityTests
{
    public static void Register()
    {
        MiniTest.Case("折りたたむとその先が隠れる", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            var result = VisibilityService.Compute(graph, new VisibilityOptions
            {
                Collapsed = new HashSet<Guid> { a.Id },
            });

            MiniTest.True(result.IsVisible(a.Id), "折りたたんだ本人は見える");
            MiniTest.False(result.IsVisible(b.Id), "先は隠れる");
            MiniTest.False(result.IsVisible(c.Id), "その先も隠れる");
            MiniTest.Equal(2, result.HiddenBehind(a.Id), "隠れた数");
        });

        MiniTest.Case("別の枝からも辿れるステップは隠れない", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var left = graph.AddNode(new TodoNode { Title = "左" });
            var right = graph.AddNode(new TodoNode { Title = "右" });
            var merged = graph.AddNode(new TodoNode { Title = "合流" });
            var after = graph.AddNode(new TodoNode { Title = "合流のあと" });
            graph.Connect(left.Id, merged.Id);
            graph.Connect(right.Id, merged.Id);
            graph.Connect(merged.Id, after.Id);

            var result = VisibilityService.Compute(graph, new VisibilityOptions
            {
                Collapsed = new HashSet<Guid> { left.Id },
            });

            MiniTest.True(result.IsVisible(merged.Id), "右からも辿れるので残る");
            MiniTest.True(result.IsVisible(after.Id), "その先も残る");
            MiniTest.Equal(0, result.HiddenBehind(left.Id), "隠れたものは無い");
        });

        MiniTest.Case("両方の枝を折りたたむと合流も隠れる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var left = graph.AddNode(new TodoNode { Title = "左" });
            var right = graph.AddNode(new TodoNode { Title = "右" });
            var merged = graph.AddNode(new TodoNode { Title = "合流" });
            graph.Connect(left.Id, merged.Id);
            graph.Connect(right.Id, merged.Id);

            var result = VisibilityService.Compute(graph, new VisibilityOptions
            {
                Collapsed = new HashSet<Guid> { left.Id, right.Id },
            });

            MiniTest.False(result.IsVisible(merged.Id), "両方閉じたので隠れる");
        });

        MiniTest.Case("絞り込むと関係ないステップが隠れる", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            var unrelated = graph.AddNode(new TodoNode { Title = "無関係" });

            var result = VisibilityService.Compute(graph, new VisibilityOptions { FocusId = b.Id });

            MiniTest.True(result.IsVisible(a.Id), "上流は残る");
            MiniTest.True(result.IsVisible(b.Id), "本人は残る");
            MiniTest.True(result.IsVisible(c.Id), "下流は残る");
            MiniTest.False(result.IsVisible(unrelated.Id), "無関係は隠れる");
        });

        MiniTest.Case("完了したステップを隠せる", () =>
        {
            var (graph, a, _, _) = GraphTests.Chain();
            a.Status = NodeStatus.Done;

            var result = VisibilityService.Compute(graph, new VisibilityOptions { HideCompleted = true });
            MiniTest.False(result.IsVisible(a.Id), "完了は隠れる");
            MiniTest.Equal(2, result.Visible.Count, "残りは 2 件");
        });

        MiniTest.Case("何も指定しなければ全部見える", () =>
        {
            var (graph, _, _, _) = GraphTests.Chain();
            var result = VisibilityService.Compute(graph);
            MiniTest.Equal(3, result.Visible.Count, "全部見える");
        });

        MiniTest.Case("入れ子で折りたたんでも数が正しい", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var root = graph.AddNode(new TodoNode { Title = "根" });
            var mid = graph.AddNode(new TodoNode { Title = "中" });
            var leaf1 = graph.AddNode(new TodoNode { Title = "葉1" });
            var leaf2 = graph.AddNode(new TodoNode { Title = "葉2" });
            graph.Connect(root.Id, mid.Id);
            graph.Connect(mid.Id, leaf1.Id);
            graph.Connect(mid.Id, leaf2.Id);

            var result = VisibilityService.Compute(graph, new VisibilityOptions
            {
                Collapsed = new HashSet<Guid> { root.Id },
            });

            MiniTest.Equal(3, result.HiddenBehind(root.Id), "中と葉 2 つ");
            MiniTest.Equal(1, result.Visible.Count, "見えるのは根だけ");
        });
    }
}
