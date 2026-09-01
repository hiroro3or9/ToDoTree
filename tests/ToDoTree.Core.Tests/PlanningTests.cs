using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

public class PlanningTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));

    // ---- アウトラインの読み取り ----

    [Test]
    [DisplayName("インデントが深さになる")]
    public async Task Parse_IndentBecomesDepth()
    {
        var items = OutlineParser.Parse("親\n  子\n    孫\n  子2\n別の親", Today);
        await Assert.That(items.Count).IsEqualTo(5).Because("行数");
        await Assert.That(items[0].Depth).IsEqualTo(0).Because("親の深さ");
        await Assert.That(items[1].Depth).IsEqualTo(1).Because("子の深さ");
        await Assert.That(items[2].Depth).IsEqualTo(2).Because("孫の深さ");
        await Assert.That(items[3].Depth).IsEqualTo(1).Because("子2 の深さ");
        await Assert.That(items[4].Depth).IsEqualTo(0).Because("別の親の深さ");
    }

    [Test]
    [DisplayName("箇条書き記号と空行が落ちる")]
    public async Task Parse_StripsBulletsAndBlankLines()
    {
        var items = OutlineParser.Parse("- 一つ目\n\n* 二つ目\n1. 三つ目\n・四つ目", Today);
        await Assert.That(items.Count).IsEqualTo(4).Because("行数");
        await Assert.That(items[0].Title).IsEqualTo("一つ目").Because("1 行目");
        await Assert.That(items[2].Title).IsEqualTo("三つ目").Because("3 行目");
        await Assert.That(items[3].Title).IsEqualTo("四つ目").Because("4 行目");
    }

    [Test]
    [DisplayName("タブとスペースが混ざっても深さが揃う")]
    public async Task Parse_HandlesMixedTabsAndSpaces()
    {
        var items = OutlineParser.Parse("親\n\t子\n\t子2", Today);
        await Assert.That(items[1].Depth).IsEqualTo(1).Because("タブの子");
        await Assert.That(items[2].Depth).IsEqualTo(1).Because("タブの子2");
    }

    [Test]
    [DisplayName("期限・見積り・タグが取り出される")]
    public async Task Parse_ExtractsDueEstimateAndTags()
    {
        var items = OutlineParser.Parse("データ構造を決める @9/10 ~2h #設計 #重要", Today);
        var item = items[0];
        await Assert.That(item.Title).IsEqualTo("データ構造を決める").Because("タイトルから記号が消える");
        await Assert.That(item.EstimateMinutes).IsEqualTo(120).Because("見積り（分）");
        await Assert.That(item.Tags.Count).IsEqualTo(2).Because("タグの数");
        await Assert.That(item.Tags.Contains("設計")).IsTrue().Because("タグの中身");
        await Assert.That(item.Due is { } due && due.Month == 9 && due.Day == 10).IsTrue().Because("期限");
    }

    [Test]
    [DisplayName("見積りは分でも書ける")]
    public async Task Parse_AcceptsMinuteEstimates()
    {
        var items = OutlineParser.Parse("軽い作業 ~90m\nもっと軽い ~15", Today);
        await Assert.That(items[0].EstimateMinutes).IsEqualTo(90).Because("90m");
        await Assert.That(items[1].EstimateMinutes).IsEqualTo(15).Because("単位なしは分");
    }

    [Test]
    [DisplayName("過ぎた月日は翌年とみなす")]
    public async Task Parse_RollsPastDateToNextYear()
    {
        var items = OutlineParser.Parse("年をまたぐ作業 @1/5", Today);
        await Assert.That(items[0].Due is { } due && due.Year == Today.Year + 1).IsTrue().Because("翌年になる");
    }

    // ---- 取り込み ----

    [Test]
    [DisplayName("アウトラインが親子として繋がる")]
    public async Task Import_LinksOutlineAsParentChild()
    {
        var graph = new TodoGraph(new TodoProject());
        var items = OutlineParser.Parse("要件\n  画面\n  データ\n実装", Today);
        var created = OutlineImporter.Import(graph, items);

        await Assert.That(created.Count).IsEqualTo(4).Because("作られた数");
        var requirements = created[0];
        await Assert.That(graph.ChildrenOf(requirements.Id).Count()).IsEqualTo(2).Because("要件の子の数");
        await Assert.That(graph.ParentsOf(created[1].Id).Any(p => p.Id == requirements.Id)).IsTrue().Because("画面の親は要件");
        await Assert.That(graph.IncomingOf(created[3].Id).Count).IsEqualTo(0).Because("実装は根のまま");
    }

    [Test]
    [DisplayName("選択中のステップの続きとして取り込める")]
    public async Task Import_AttachesToAnchor()
    {
        var (graph, _, _, c) = GraphTests.Chain();
        var items = OutlineParser.Parse("次の作業\n  その詳細", Today);
        var created = OutlineImporter.Import(graph, items, c.Id);

        await Assert.That(graph.ChildrenOf(c.Id).Any(n => n.Id == created[0].Id)).IsTrue().Because("アンカーにぶら下がる");
        await Assert.That(graph.ParentsOf(created[1].Id).Any(p => p.Id == created[0].Id)).IsTrue().Because("その下にぶら下がる");
        await Assert.That(graph.HasCycle()).IsFalse().Because("DAG のまま");
    }

    // ---- 次にやること ----

    [Test]
    [DisplayName("待ちのステップは候補に出ない")]
    public async Task Suggest_SkipsBlockedSteps()
    {
        var (graph, a, b, _) = GraphTests.Chain();
        var suggestions = NextActionPlanner.Suggest(graph, count: 0, now: Today);
        await Assert.That(suggestions.Any(s => s.Node.Id == a.Id)).IsTrue().Because("着手できる A は出る");
        await Assert.That(suggestions.Any(s => s.Node.Id == b.Id)).IsFalse().Because("待ちの B は出ない");
    }

    [Test]
    [DisplayName("多くを解放するステップが上に来る")]
    public async Task Suggest_RanksUnblockingStepsHigher()
    {
        var graph = new TodoGraph(new TodoProject());
        var hub = graph.AddNode(new TodoNode { Title = "詰まりを解くステップ" });
        var lonely = graph.AddNode(new TodoNode { Title = "単発の作業" });
        for (var i = 0; i < 3; i++)
        {
            var child = graph.AddNode(new TodoNode { Title = $"後続{i}" });
            graph.Connect(hub.Id, child.Id);
        }

        var suggestions = NextActionPlanner.Suggest(graph, count: 2, now: Today);
        await Assert.That(suggestions[0].Node.Id).IsEqualTo(hub.Id).Because("先頭は詰まりを解くステップ");
        await Assert.That(suggestions[0].Score > suggestions.First(s => s.Node.Id == lonely.Id).Score).IsTrue().Because("スコアが上");
        await Assert.That(suggestions[0].Reason.Length > 0).IsTrue().Because("理由が付く");
    }

    [Test]
    [DisplayName("期限超過は強く効く")]
    public async Task Suggest_PrioritizesOverdue()
    {
        var graph = new TodoGraph(new TodoProject());
        var overdue = graph.AddNode(new TodoNode { Title = "遅れている", Due = Today.AddDays(-3) });
        graph.AddNode(new TodoNode { Title = "ふつう" });

        var suggestions = NextActionPlanner.Suggest(graph, count: 1, now: Today);
        await Assert.That(suggestions[0].Node.Id).IsEqualTo(overdue.Id).Because("遅れているものが先頭");
        await Assert.That(suggestions[0].Reason.Contains("期限")).IsTrue().Because("理由に期限が出る");
    }

    [Test]
    [DisplayName("解放されるのは他の先行も片付いているときだけ")]
    public async Task NodesUnlockedBy_RequiresAllPredecessorsDone()
    {
        var graph = new TodoGraph(new TodoProject());
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        var merged = graph.AddNode(new TodoNode { Title = "合流" });
        graph.Connect(a.Id, merged.Id);
        graph.Connect(b.Id, merged.Id);

        await Assert.That(graph.NodesUnlockedBy(a.Id).Count).IsEqualTo(0).Because("B が残っているので解放されない");

        b.Status = NodeStatus.Done;
        await Assert.That(graph.NodesUnlockedBy(a.Id).Count).IsEqualTo(1).Because("B が終われば A の完了で解放される");
    }

    // ---- 期限の逆算 ----

    [Test]
    [DisplayName("後続の期限が先行の締切になる")]
    public async Task Schedule_PropagatesDueToPredecessor()
    {
        var graph = new TodoGraph(new TodoProject());
        var first = graph.AddNode(new TodoNode { Title = "先", EstimateMinutes = 60 });
        var last = graph.AddNode(new TodoNode { Title = "後", EstimateMinutes = 60, Due = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(9)) });
        graph.Connect(first.Id, last.Id);

        var schedule = ScheduleAnalysis.Compute(graph, Today);
        await Assert.That(schedule[last.Id].LatestStart!.Value.Day).IsEqualTo(10).Because("後は 9/10 に着手");
        await Assert.That(schedule[first.Id].LatestFinish!.Value.Day).IsEqualTo(9).Because("先は 9/9 までに完了");
    }

    [Test]
    [DisplayName("見積りが長いほど開始日が前倒しになる")]
    public async Task Schedule_LongerEstimateStartsEarlier()
    {
        var graph = new TodoGraph(new TodoProject());
        var heavy = graph.AddNode(new TodoNode
        {
            Title = "重い作業",
            EstimateMinutes = 4 * 60 * 3,
            Due = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(9)),
        });

        var schedule = ScheduleAnalysis.Compute(graph, Today);
        await Assert.That(schedule[heavy.Id].DurationDays).IsEqualTo(3).Because("3 日かかる");
        await Assert.That(schedule[heavy.Id].LatestStart!.Value.Day).IsEqualTo(8).Because("9/8 には始める");
    }

    [Test]
    [DisplayName("開始日を過ぎていると危険として印が付く")]
    public async Task Schedule_FlagsAtRiskWhenStartPassed()
    {
        var graph = new TodoGraph(new TodoProject());
        var late = graph.AddNode(new TodoNode
        {
            Title = "間に合わない",
            EstimateMinutes = 4 * 60 * 5,
            Due = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.FromHours(9)),
        });

        var schedule = ScheduleAnalysis.Compute(graph, Today);
        await Assert.That(schedule[late.Id].AtRisk).IsTrue().Because("危険と判定される");
    }

    [Test]
    [DisplayName("期限がどこにも無ければ締切も出ない")]
    public async Task Schedule_NoDeadlineWithoutDue()
    {
        var (graph, a, _, _) = GraphTests.Chain();
        var schedule = ScheduleAnalysis.Compute(graph, Today);
        await Assert.That(schedule[a.Id].LatestFinish is null).IsTrue().Because("締切なし");
        await Assert.That(schedule[a.Id].AtRisk).IsFalse().Because("危険でもない");
    }

    // ---- 矢印キーでの移動 ----

    [Test]
    [DisplayName("右のステップへ移動できる")]
    public async Task FindNeighbor_MovesRight()
    {
        var graph = new TodoGraph(new TodoProject());
        var origin = graph.AddNode(new TodoNode { Title = "元", X = 0, Y = 0 });
        var right = graph.AddNode(new TodoNode { Title = "右", X = 300, Y = 0 });
        graph.AddNode(new TodoNode { Title = "左", X = -300, Y = 0 });

        await Assert.That(Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Right)!.Id).IsEqualTo(right.Id).Because("右");
        await Assert.That(Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Left)!.Title).IsEqualTo("左").Because("左");
        await Assert.That(Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Up) is null).IsTrue().Because("上には無い");
    }

    [Test]
    [DisplayName("まっすぐ近いものが選ばれる")]
    public async Task FindNeighbor_PrefersStraightAhead()
    {
        var graph = new TodoGraph(new TodoProject());
        var origin = graph.AddNode(new TodoNode { Title = "元", X = 0, Y = 0 });
        var straight = graph.AddNode(new TodoNode { Title = "まっすぐ", X = 320, Y = 10 });
        graph.AddNode(new TodoNode { Title = "斜め", X = 280, Y = 400 });

        await Assert.That(Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Right)!.Id).IsEqualTo(straight.Id).Because("まっすぐが選ばれる");
    }
}
