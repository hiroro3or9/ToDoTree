using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

public static class PlanningTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9));

    public static void Register()
    {
        // ---- アウトラインの読み取り ----

        MiniTest.Case("インデントが深さになる", () =>
        {
            var items = OutlineParser.Parse("親\n  子\n    孫\n  子2\n別の親", Today);
            MiniTest.Equal(5, items.Count, "行数");
            MiniTest.Equal(0, items[0].Depth, "親の深さ");
            MiniTest.Equal(1, items[1].Depth, "子の深さ");
            MiniTest.Equal(2, items[2].Depth, "孫の深さ");
            MiniTest.Equal(1, items[3].Depth, "子2 の深さ");
            MiniTest.Equal(0, items[4].Depth, "別の親の深さ");
        });

        MiniTest.Case("箇条書き記号と空行が落ちる", () =>
        {
            var items = OutlineParser.Parse("- 一つ目\n\n* 二つ目\n1. 三つ目\n・四つ目", Today);
            MiniTest.Equal(4, items.Count, "行数");
            MiniTest.Equal("一つ目", items[0].Title, "1 行目");
            MiniTest.Equal("三つ目", items[2].Title, "3 行目");
            MiniTest.Equal("四つ目", items[3].Title, "4 行目");
        });

        MiniTest.Case("タブとスペースが混ざっても深さが揃う", () =>
        {
            var items = OutlineParser.Parse("親\n\t子\n\t子2", Today);
            MiniTest.Equal(1, items[1].Depth, "タブの子");
            MiniTest.Equal(1, items[2].Depth, "タブの子2");
        });

        MiniTest.Case("期限・見積り・タグが取り出される", () =>
        {
            var items = OutlineParser.Parse("データ構造を決める @9/10 ~2h #設計 #重要", Today);
            var item = items[0];
            MiniTest.Equal("データ構造を決める", item.Title, "タイトルから記号が消える");
            MiniTest.Equal(120, item.EstimateMinutes, "見積り（分）");
            MiniTest.Equal(2, item.Tags.Count, "タグの数");
            MiniTest.True(item.Tags.Contains("設計"), "タグの中身");
            MiniTest.True(item.Due is { } due && due.Month == 9 && due.Day == 10, "期限");
        });

        MiniTest.Case("見積りは分でも書ける", () =>
        {
            var items = OutlineParser.Parse("軽い作業 ~90m\nもっと軽い ~15", Today);
            MiniTest.Equal(90, items[0].EstimateMinutes, "90m");
            MiniTest.Equal(15, items[1].EstimateMinutes, "単位なしは分");
        });

        MiniTest.Case("過ぎた月日は翌年とみなす", () =>
        {
            var items = OutlineParser.Parse("年をまたぐ作業 @1/5", Today);
            MiniTest.True(items[0].Due is { } due && due.Year == Today.Year + 1, "翌年になる");
        });

        // ---- 取り込み ----

        MiniTest.Case("アウトラインが親子として繋がる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var items = OutlineParser.Parse("要件\n  画面\n  データ\n実装", Today);
            var created = OutlineImporter.Import(graph, items);

            MiniTest.Equal(4, created.Count, "作られた数");
            var requirements = created[0];
            MiniTest.Equal(2, graph.ChildrenOf(requirements.Id).Count(), "要件の子の数");
            MiniTest.True(graph.ParentsOf(created[1].Id).Any(p => p.Id == requirements.Id), "画面の親は要件");
            MiniTest.Equal(0, graph.IncomingOf(created[3].Id).Count, "実装は根のまま");
        });

        MiniTest.Case("選択中のステップの続きとして取り込める", () =>
        {
            var (graph, _, _, c) = GraphTests.Chain();
            var items = OutlineParser.Parse("次の作業\n  その詳細", Today);
            var created = OutlineImporter.Import(graph, items, c.Id);

            MiniTest.True(graph.ChildrenOf(c.Id).Any(n => n.Id == created[0].Id), "アンカーにぶら下がる");
            MiniTest.True(graph.ParentsOf(created[1].Id).Any(p => p.Id == created[0].Id), "その下にぶら下がる");
            MiniTest.False(graph.HasCycle(), "DAG のまま");
        });

        // ---- 次にやること ----

        MiniTest.Case("待ちのステップは候補に出ない", () =>
        {
            var (graph, a, b, _) = GraphTests.Chain();
            var suggestions = NextActionPlanner.Suggest(graph, count: 0, now: Today);
            MiniTest.True(suggestions.Any(s => s.Node.Id == a.Id), "着手できる A は出る");
            MiniTest.False(suggestions.Any(s => s.Node.Id == b.Id), "待ちの B は出ない");
        });

        MiniTest.Case("多くを解放するステップが上に来る", () =>
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
            MiniTest.Equal(hub.Id, suggestions[0].Node.Id, "先頭は詰まりを解くステップ");
            MiniTest.True(suggestions[0].Score > suggestions.First(s => s.Node.Id == lonely.Id).Score, "スコアが上");
            MiniTest.True(suggestions[0].Reason.Length > 0, "理由が付く");
        });

        MiniTest.Case("期限超過は強く効く", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var overdue = graph.AddNode(new TodoNode { Title = "遅れている", Due = Today.AddDays(-3) });
            graph.AddNode(new TodoNode { Title = "ふつう" });

            var suggestions = NextActionPlanner.Suggest(graph, count: 1, now: Today);
            MiniTest.Equal(overdue.Id, suggestions[0].Node.Id, "遅れているものが先頭");
            MiniTest.True(suggestions[0].Reason.Contains("期限"), "理由に期限が出る");
        });

        MiniTest.Case("解放されるのは他の先行も片付いているときだけ", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var a = graph.AddNode(new TodoNode { Title = "A" });
            var b = graph.AddNode(new TodoNode { Title = "B" });
            var merged = graph.AddNode(new TodoNode { Title = "合流" });
            graph.Connect(a.Id, merged.Id);
            graph.Connect(b.Id, merged.Id);

            MiniTest.Equal(0, graph.NodesUnlockedBy(a.Id).Count, "B が残っているので解放されない");

            b.Status = NodeStatus.Done;
            MiniTest.Equal(1, graph.NodesUnlockedBy(a.Id).Count, "B が終われば A の完了で解放される");
        });

        // ---- 期限の逆算 ----

        MiniTest.Case("後続の期限が先行の締切になる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var first = graph.AddNode(new TodoNode { Title = "先", EstimateMinutes = 60 });
            var last = graph.AddNode(new TodoNode { Title = "後", EstimateMinutes = 60, Due = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(9)) });
            graph.Connect(first.Id, last.Id);

            var schedule = ScheduleAnalysis.Compute(graph, Today);
            MiniTest.Equal(10, schedule[last.Id].LatestStart!.Value.Day, "後は 9/10 に着手");
            MiniTest.Equal(9, schedule[first.Id].LatestFinish!.Value.Day, "先は 9/9 までに完了");
        });

        MiniTest.Case("見積りが長いほど開始日が前倒しになる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var heavy = graph.AddNode(new TodoNode
            {
                Title = "重い作業",
                EstimateMinutes = 4 * 60 * 3,
                Due = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.FromHours(9)),
            });

            var schedule = ScheduleAnalysis.Compute(graph, Today);
            MiniTest.Equal(3, schedule[heavy.Id].DurationDays, "3 日かかる");
            MiniTest.Equal(8, schedule[heavy.Id].LatestStart!.Value.Day, "9/8 には始める");
        });

        MiniTest.Case("開始日を過ぎていると危険として印が付く", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var late = graph.AddNode(new TodoNode
            {
                Title = "間に合わない",
                EstimateMinutes = 4 * 60 * 5,
                Due = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.FromHours(9)),
            });

            var schedule = ScheduleAnalysis.Compute(graph, Today);
            MiniTest.True(schedule[late.Id].AtRisk, "危険と判定される");
        });

        MiniTest.Case("期限がどこにも無ければ締切も出ない", () =>
        {
            var (graph, a, _, _) = GraphTests.Chain();
            var schedule = ScheduleAnalysis.Compute(graph, Today);
            MiniTest.True(schedule[a.Id].LatestFinish is null, "締切なし");
            MiniTest.False(schedule[a.Id].AtRisk, "危険でもない");
        });

        // ---- 矢印キーでの移動 ----

        MiniTest.Case("右のステップへ移動できる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var origin = graph.AddNode(new TodoNode { Title = "元", X = 0, Y = 0 });
            var right = graph.AddNode(new TodoNode { Title = "右", X = 300, Y = 0 });
            graph.AddNode(new TodoNode { Title = "左", X = -300, Y = 0 });

            MiniTest.Equal(right.Id, Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Right)!.Id, "右");
            MiniTest.Equal("左", Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Left)!.Title, "左");
            MiniTest.True(Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Up) is null, "上には無い");
        });

        MiniTest.Case("まっすぐ近いものが選ばれる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var origin = graph.AddNode(new TodoNode { Title = "元", X = 0, Y = 0 });
            var straight = graph.AddNode(new TodoNode { Title = "まっすぐ", X = 320, Y = 10 });
            graph.AddNode(new TodoNode { Title = "斜め", X = 280, Y = 400 });

            MiniTest.Equal(straight.Id, Navigation.FindNeighbor(graph, origin.Id, MoveDirection.Right)!.Id, "まっすぐが選ばれる");
        });
    }
}
