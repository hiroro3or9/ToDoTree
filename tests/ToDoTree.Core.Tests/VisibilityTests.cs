using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Tests;

public class VisibilityTests
{
    [Test]
    [DisplayName("折りたたむとその先が隠れる")]
    public async Task Collapse_HidesDownstream()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        var result = VisibilityService.Compute(graph, new VisibilityOptions
        {
            Collapsed = new HashSet<Guid> { a.Id },
        });

        await Assert.That(result.IsVisible(a.Id)).IsTrue().Because("折りたたんだ本人は見える");
        await Assert.That(result.IsVisible(b.Id)).IsFalse().Because("先は隠れる");
        await Assert.That(result.IsVisible(c.Id)).IsFalse().Because("その先も隠れる");
        await Assert.That(result.HiddenBehind(a.Id)).IsEqualTo(2).Because("隠れた数");
    }

    [Test]
    [DisplayName("別の枝からも辿れるステップは隠れない")]
    public async Task Collapse_KeepsNodesReachableFromOtherBranch()
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

        await Assert.That(result.IsVisible(merged.Id)).IsTrue().Because("右からも辿れるので残る");
        await Assert.That(result.IsVisible(after.Id)).IsTrue().Because("その先も残る");
        await Assert.That(result.HiddenBehind(left.Id)).IsEqualTo(0).Because("隠れたものは無い");
    }

    [Test]
    [DisplayName("両方の枝を折りたたむと合流も隠れる")]
    public async Task Collapse_HidesMergeWhenAllBranchesCollapsed()
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

        await Assert.That(result.IsVisible(merged.Id)).IsFalse().Because("両方閉じたので隠れる");
    }

    [Test]
    [DisplayName("絞り込むと関係ないステップが隠れる")]
    public async Task Focus_HidesUnrelatedNodes()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        var unrelated = graph.AddNode(new TodoNode { Title = "無関係" });

        var result = VisibilityService.Compute(graph, new VisibilityOptions { FocusId = b.Id });

        await Assert.That(result.IsVisible(a.Id)).IsTrue().Because("上流は残る");
        await Assert.That(result.IsVisible(b.Id)).IsTrue().Because("本人は残る");
        await Assert.That(result.IsVisible(c.Id)).IsTrue().Because("下流は残る");
        await Assert.That(result.IsVisible(unrelated.Id)).IsFalse().Because("無関係は隠れる");
    }

    [Test]
    [DisplayName("完了したステップを隠せる")]
    public async Task HideCompleted_HidesDoneNodes()
    {
        var (graph, a, _, _) = GraphTests.Chain();
        a.Status = NodeStatus.Done;

        var result = VisibilityService.Compute(graph, new VisibilityOptions { HideCompleted = true });
        await Assert.That(result.IsVisible(a.Id)).IsFalse().Because("完了は隠れる");
        await Assert.That(result.Visible.Count).IsEqualTo(2).Because("残りは 2 件");
    }

    [Test]
    [DisplayName("何も指定しなければ全部見える")]
    public async Task Default_ShowsEverything()
    {
        var (graph, _, _, _) = GraphTests.Chain();
        var result = VisibilityService.Compute(graph);
        await Assert.That(result.Visible.Count).IsEqualTo(3).Because("全部見える");
    }

    [Test]
    [DisplayName("入れ子で折りたたんでも数が正しい")]
    public async Task Collapse_CountsNestedHiddenNodes()
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

        await Assert.That(result.HiddenBehind(root.Id)).IsEqualTo(3).Because("中と葉 2 つ");
        await Assert.That(result.Visible.Count).IsEqualTo(1).Because("見えるのは根だけ");
    }
}
