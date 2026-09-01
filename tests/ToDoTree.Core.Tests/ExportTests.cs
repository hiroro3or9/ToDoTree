using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

public static class ExportTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9));

    public static void Register()
    {
        // ---- 完了予測 ----

        MiniTest.Case("残りが無ければ完了見込みは出ない", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            graph.AddNode(new TodoNode { Title = "済", Status = NodeStatus.Done, EstimateMinutes = 60 });

            var forecast = ForecastService.Compute(graph, Today);
            MiniTest.False(forecast.HasWork, "残りが無い");
            MiniTest.True(forecast.EstimatedFinish is null, "見込み日も出ない");
        });

        MiniTest.Case("履歴が無ければ既定のペースで見積もる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            graph.AddNode(new TodoNode { Title = "残り1", EstimateMinutes = 240 });
            graph.AddNode(new TodoNode { Title = "残り2", EstimateMinutes = 240 });

            var forecast = ForecastService.Compute(graph, Today);
            MiniTest.False(forecast.PaceFromHistory, "履歴は使われない");
            MiniTest.Equal(240d, forecast.MinutesPerDay, "1 日 4 時間");
            MiniTest.Equal(2, forecast.RemainingDays, "2 日かかる");
            MiniTest.Equal(3, forecast.EstimatedFinish!.Value.Day, "9/3 completion");
        });

        MiniTest.Case("完了の記録があれば実際のペースを使う", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            graph.AddNode(new TodoNode
            {
                Title = "済1",
                Status = NodeStatus.Done,
                EstimateMinutes = 240,
                CompletedAt = Today.AddDays(-4),
            });
            graph.AddNode(new TodoNode
            {
                Title = "済2",
                Status = NodeStatus.Done,
                EstimateMinutes = 240,
                CompletedAt = Today,
            });
            graph.AddNode(new TodoNode { Title = "残り", EstimateMinutes = 480 });

            var forecast = ForecastService.Compute(graph, Today);
            MiniTest.True(forecast.PaceFromHistory, "履歴が使われる");
            MiniTest.Equal(120d, forecast.MinutesPerDay, "4 日で 480 分＝1 日 120 分");
            MiniTest.Equal(4, forecast.RemainingDays, "480 分 ÷ 120 分で 4 日");
        });

        MiniTest.Case("ゴールの期限との余裕が出る", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            graph.AddNode(new TodoNode { Title = "残り", EstimateMinutes = 240 });
            graph.AddNode(new TodoNode
            {
                Title = "公開する",
                Kind = NodeKind.Goal,
                EstimateMinutes = 0,
                Due = Today.AddDays(10),
            });

            var forecast = ForecastService.Compute(graph, Today);
            MiniTest.True(forecast.Deadline is not null, "締切が拾える");
            MiniTest.Equal(9, forecast.SlackDays, "9 日の余裕");
            MiniTest.False(forecast.IsLate, "間に合う");
        });

        MiniTest.Case("間に合わないときは不足として出る", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            graph.AddNode(new TodoNode { Title = "重い残り", EstimateMinutes = 240 * 10 });
            graph.AddNode(new TodoNode
            {
                Title = "公開する",
                Kind = NodeKind.Goal,
                EstimateMinutes = 0,
                Due = Today.AddDays(3),
            });

            var forecast = ForecastService.Compute(graph, Today);
            MiniTest.True(forecast.IsLate, "間に合わない");
            MiniTest.True(forecast.SlackDays < 0, "不足日数がマイナス");
        });

        // ---- 書き出し ----

        MiniTest.Case("Mermaid に全ステップと全依存が出る", () =>
        {
            var project = SampleProject.Create();
            var mermaid = GraphExporter.ToMermaid(project);

            MiniTest.True(mermaid.StartsWith("graph LR"), "向きの宣言");
            MiniTest.Equal(project.Edges.Count, CountOccurrences(mermaid, "-->"), "辺の数");

            foreach (var node in project.Nodes)
            {
                MiniTest.True(mermaid.Contains(node.Title), $"{node.Title} が含まれる");
            }
        });

        MiniTest.Case("Mermaid で意味を持つ記号が逃がされる", () =>
        {
            var project = new TodoProject();
            project.Nodes.Add(new TodoNode { Title = "彼は \"重要\" と #言った" });

            var mermaid = GraphExporter.ToMermaid(project);
            MiniTest.True(mermaid.Contains("#quot;"), "引用符が逃がされる");
            MiniTest.True(mermaid.Contains("#35;"), "シャープが逃がされる");
            MiniTest.False(mermaid.Contains("\"重要\""), "生の引用符が残らない");
        });

        MiniTest.Case("Markdown にチェックボックスと図が出る", () =>
        {
            var project = SampleProject.Create();
            var markdown = GraphExporter.ToMarkdown(project, Today);

            MiniTest.True(markdown.Contains("# " + project.Name), "見出し");
            MiniTest.True(markdown.Contains("```mermaid"), "図が埋め込まれる");
            MiniTest.True(markdown.Contains("- [x] "), "完了のチェックボックス");
            MiniTest.True(markdown.Contains("- [ ] "), "未完了のチェックボックス");
            MiniTest.True(markdown.Contains("## 次にやること"), "次にやること");
        });

        MiniTest.Case("Markdown のステップは先行から並ぶ", () =>
        {
            var project = new TodoProject();
            var graph = new TodoGraph(project);
            var first = graph.AddNode(new TodoNode { Title = "さきにやる" });
            var second = graph.AddNode(new TodoNode { Title = "あとでやる" });
            graph.Connect(first.Id, second.Id);

            var markdown = GraphExporter.ToMarkdown(project, Today, includeDiagram: false);
            var steps = markdown[markdown.IndexOf("## ステップ", StringComparison.Ordinal)..];
            MiniTest.True(
                steps.IndexOf("さきにやる", StringComparison.Ordinal) < steps.IndexOf("あとでやる", StringComparison.Ordinal),
                "先行が先に並ぶ");
            MiniTest.True(steps.Contains("先行: さきにやる"), "先行が書かれる");
        });

        MiniTest.Case("直近で終えたことがまとまる", () =>
        {
            var project = new TodoProject();
            project.Nodes.Add(new TodoNode { Title = "きのう終えた", Status = NodeStatus.Done, CompletedAt = Today.AddDays(-1) });
            project.Nodes.Add(new TodoNode { Title = "ずっと前に終えた", Status = NodeStatus.Done, CompletedAt = Today.AddDays(-40) });

            var markdown = GraphExporter.ToMarkdown(project, Today, includeDiagram: false);
            var section = markdown[markdown.IndexOf("直近", StringComparison.Ordinal)..];
            MiniTest.True(section.Contains("きのう終えた"), "直近のものは載る");
            MiniTest.False(section[..section.IndexOf("## ステップ", StringComparison.Ordinal)].Contains("ずっと前"), "古いものは載らない");
        });

        // ---- ステップの分割 ----

        MiniTest.Case("分割すると間に子ステップが挟まる", () =>
        {
            var (graph, a, b, c) = GraphTests.Chain();
            var items = OutlineParser.Parse("下ごしらえ\n仕上げ", Today);
            var created = StepSplitter.Split(graph, b.Id, items);

            MiniTest.Equal(2, created.Count, "作られた数");
            MiniTest.True(graph.ChildrenOf(b.Id).Any(n => n.Id == created[0].Id), "B の下に入る");
            MiniTest.False(graph.ChildrenOf(b.Id).Any(n => n.Id == c.Id), "B と C の直結は外れる");
            MiniTest.True(graph.ParentsOf(c.Id).Any(n => n.Id == created[1].Id), "末端が C に繋がる");
            MiniTest.True(graph.ParentsOf(b.Id).Any(n => n.Id == a.Id), "B の先行はそのまま");
            MiniTest.False(graph.HasCycle(), "DAG のまま");
        });

        MiniTest.Case("入れ子で分割すると末端だけが後続に繋がる", () =>
        {
            var (graph, _, b, c) = GraphTests.Chain();
            var items = OutlineParser.Parse("調べる\n  資料を集める\n  読む", Today);
            var created = StepSplitter.Split(graph, b.Id, items);

            MiniTest.Equal(3, created.Count, "作られた数");
            MiniTest.Equal(2, graph.ParentsOf(c.Id).Count(), "末端 2 つが C に繋がる");
            MiniTest.False(graph.ChildrenOf(created[0].Id).Any(n => n.Id == c.Id), "途中のものは C に繋がらない");
        });

        MiniTest.Case("後続の無いステップも分割できる", () =>
        {
            var graph = new TodoGraph(new TodoProject());
            var goal = graph.AddNode(new TodoNode { Title = "ゴール", Kind = NodeKind.Goal });
            var created = StepSplitter.Split(graph, goal.Id, OutlineParser.Parse("準備\n実行", Today));

            MiniTest.Equal(2, created.Count, "作られた数");
            MiniTest.False(graph.HasCycle(), "DAG のまま");
        });
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
