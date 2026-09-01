using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

public class ExportTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9));

    // ---- 完了予測 ----

    [Test]
    [DisplayName("残りが無ければ完了見込みは出ない")]
    public async Task Forecast_NoWorkNoEstimate()
    {
        var graph = new TodoGraph(new TodoProject());
        graph.AddNode(new TodoNode { Title = "済", Status = NodeStatus.Done, EstimateMinutes = 60 });

        var forecast = ForecastService.Compute(graph, Today);
        await Assert.That(forecast.HasWork).IsFalse().Because("残りが無い");
        await Assert.That(forecast.EstimatedFinish is null).IsTrue().Because("見込み日も出ない");
    }

    [Test]
    [DisplayName("履歴が無ければ既定のペースで見積もる")]
    public async Task Forecast_UsesDefaultPaceWithoutHistory()
    {
        var graph = new TodoGraph(new TodoProject());
        graph.AddNode(new TodoNode { Title = "残り1", EstimateMinutes = 240 });
        graph.AddNode(new TodoNode { Title = "残り2", EstimateMinutes = 240 });

        var forecast = ForecastService.Compute(graph, Today);
        await Assert.That(forecast.PaceFromHistory).IsFalse().Because("履歴は使われない");
        await Assert.That(forecast.MinutesPerDay).IsEqualTo(240d).Because("1 日 4 時間");
        await Assert.That(forecast.RemainingDays).IsEqualTo(2).Because("2 日かかる");
        await Assert.That(forecast.EstimatedFinish!.Value.Day).IsEqualTo(3).Because("9/3 completion");
    }

    [Test]
    [DisplayName("完了の記録があれば実際のペースを使う")]
    public async Task Forecast_UsesHistoricalPace()
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
        await Assert.That(forecast.PaceFromHistory).IsTrue().Because("履歴が使われる");
        await Assert.That(forecast.MinutesPerDay).IsEqualTo(120d).Because("4 日で 480 分＝1 日 120 分");
        await Assert.That(forecast.RemainingDays).IsEqualTo(4).Because("480 分 ÷ 120 分で 4 日");
    }

    [Test]
    [DisplayName("ゴールの期限との余裕が出る")]
    public async Task Forecast_ReportsSlackAgainstGoalDue()
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
        await Assert.That(forecast.Deadline is not null).IsTrue().Because("締切が拾える");
        await Assert.That(forecast.SlackDays).IsEqualTo(9).Because("9 日の余裕");
        await Assert.That(forecast.IsLate).IsFalse().Because("間に合う");
    }

    [Test]
    [DisplayName("間に合わないときは不足として出る")]
    public async Task Forecast_ReportsNegativeSlackWhenLate()
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
        await Assert.That(forecast.IsLate).IsTrue().Because("間に合わない");
        await Assert.That(forecast.SlackDays < 0).IsTrue().Because("不足日数がマイナス");
    }

    // ---- 書き出し ----

    [Test]
    [DisplayName("Mermaid に全ステップと全依存が出る")]
    public async Task ToMermaid_IncludesAllNodesAndEdges()
    {
        var project = SampleProject.Create();
        var mermaid = GraphExporter.ToMermaid(project);

        await Assert.That(mermaid.StartsWith("graph LR")).IsTrue().Because("向きの宣言");
        await Assert.That(CountOccurrences(mermaid, "-->")).IsEqualTo(project.Edges.Count).Because("辺の数");

        foreach (var node in project.Nodes)
        {
            await Assert.That(mermaid.Contains(node.Title)).IsTrue().Because($"{node.Title} が含まれる");
        }
    }

    [Test]
    [DisplayName("Mermaid で意味を持つ記号が逃がされる")]
    public async Task ToMermaid_EscapesSpecialCharacters()
    {
        var project = new TodoProject();
        project.Nodes.Add(new TodoNode { Title = "彼は \"重要\" と #言った" });

        var mermaid = GraphExporter.ToMermaid(project);
        await Assert.That(mermaid.Contains("#quot;")).IsTrue().Because("引用符が逃がされる");
        await Assert.That(mermaid.Contains("#35;")).IsTrue().Because("シャープが逃がされる");
        await Assert.That(mermaid.Contains("\"重要\"")).IsFalse().Because("生の引用符が残らない");
    }

    [Test]
    [DisplayName("Markdown にチェックボックスと図が出る")]
    public async Task ToMarkdown_HasCheckboxesAndDiagram()
    {
        var project = SampleProject.Create();
        var markdown = GraphExporter.ToMarkdown(project, Today);

        await Assert.That(markdown.Contains("# " + project.Name)).IsTrue().Because("見出し");
        await Assert.That(markdown.Contains("```mermaid")).IsTrue().Because("図が埋め込まれる");
        await Assert.That(markdown.Contains("- [x] ")).IsTrue().Because("完了のチェックボックス");
        await Assert.That(markdown.Contains("- [ ] ")).IsTrue().Because("未完了のチェックボックス");
        await Assert.That(markdown.Contains("## 次にやること")).IsTrue().Because("次にやること");
    }

    [Test]
    [DisplayName("Markdown のステップは先行から並ぶ")]
    public async Task ToMarkdown_OrdersStepsByPredecessor()
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);
        var first = graph.AddNode(new TodoNode { Title = "さきにやる" });
        var second = graph.AddNode(new TodoNode { Title = "あとでやる" });
        graph.Connect(first.Id, second.Id);

        var markdown = GraphExporter.ToMarkdown(project, Today, includeDiagram: false);
        var steps = markdown[markdown.IndexOf("## ステップ", StringComparison.Ordinal)..];
        await Assert.That(
                steps.IndexOf("さきにやる", StringComparison.Ordinal) < steps.IndexOf("あとでやる", StringComparison.Ordinal))
            .IsTrue().Because("先行が先に並ぶ");
        await Assert.That(steps.Contains("先行: さきにやる")).IsTrue().Because("先行が書かれる");
    }

    [Test]
    [DisplayName("直近で終えたことがまとまる")]
    public async Task ToMarkdown_SummarizesRecentlyCompleted()
    {
        var project = new TodoProject();
        project.Nodes.Add(new TodoNode { Title = "きのう終えた", Status = NodeStatus.Done, CompletedAt = Today.AddDays(-1) });
        project.Nodes.Add(new TodoNode { Title = "ずっと前に終えた", Status = NodeStatus.Done, CompletedAt = Today.AddDays(-40) });

        var markdown = GraphExporter.ToMarkdown(project, Today, includeDiagram: false);
        var section = markdown[markdown.IndexOf("直近", StringComparison.Ordinal)..];
        await Assert.That(section.Contains("きのう終えた")).IsTrue().Because("直近のものは載る");
        await Assert.That(section[..section.IndexOf("## ステップ", StringComparison.Ordinal)].Contains("ずっと前"))
            .IsFalse().Because("古いものは載らない");
    }

    // ---- ステップの分割 ----

    [Test]
    [DisplayName("分割すると間に子ステップが挟まる")]
    public async Task Split_InsertsChildStepsInBetween()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        var items = OutlineParser.Parse("下ごしらえ\n仕上げ", Today);
        var created = StepSplitter.Split(graph, b.Id, items);

        await Assert.That(created.Count).IsEqualTo(2).Because("作られた数");
        await Assert.That(graph.ChildrenOf(b.Id).Any(n => n.Id == created[0].Id)).IsTrue().Because("B の下に入る");
        await Assert.That(graph.ChildrenOf(b.Id).Any(n => n.Id == c.Id)).IsFalse().Because("B と C の直結は外れる");
        await Assert.That(graph.ParentsOf(c.Id).Any(n => n.Id == created[1].Id)).IsTrue().Because("末端が C に繋がる");
        await Assert.That(graph.ParentsOf(b.Id).Any(n => n.Id == a.Id)).IsTrue().Because("B の先行はそのまま");
        await Assert.That(graph.HasCycle()).IsFalse().Because("DAG のまま");
    }

    [Test]
    [DisplayName("入れ子で分割すると末端だけが後続に繋がる")]
    public async Task Split_OnlyLeavesConnectToSuccessor()
    {
        var (graph, _, b, c) = GraphTests.Chain();
        var items = OutlineParser.Parse("調べる\n  資料を集める\n  読む", Today);
        var created = StepSplitter.Split(graph, b.Id, items);

        await Assert.That(created.Count).IsEqualTo(3).Because("作られた数");
        await Assert.That(graph.ParentsOf(c.Id).Count()).IsEqualTo(2).Because("末端 2 つが C に繋がる");
        await Assert.That(graph.ChildrenOf(created[0].Id).Any(n => n.Id == c.Id)).IsFalse().Because("途中のものは C に繋がらない");
    }

    [Test]
    [DisplayName("後続の無いステップも分割できる")]
    public async Task Split_WorksWithoutSuccessor()
    {
        var graph = new TodoGraph(new TodoProject());
        var goal = graph.AddNode(new TodoNode { Title = "ゴール", Kind = NodeKind.Goal });
        var created = StepSplitter.Split(graph, goal.Id, OutlineParser.Parse("準備\n実行", Today));

        await Assert.That(created.Count).IsEqualTo(2).Because("作られた数");
        await Assert.That(graph.HasCycle()).IsFalse().Because("DAG のまま");
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
