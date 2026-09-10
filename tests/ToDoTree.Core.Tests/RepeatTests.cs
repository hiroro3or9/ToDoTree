using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

/// <summary>
/// 回数で完了する項目。設計書「回数で完了する項目と自己ループ表示」の受け入れ条件をなぞる。
/// 時刻はすべて固定値を渡し、完了日時の付け外しまで確かめる。
/// </summary>
public class RepeatTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset T1 = T0.AddHours(1);

    private static (TodoGraph Graph, TodoNode A, TodoNode B, TodoNode C) Scene()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        RepeatService.Configure(b, 3, 0, T0);
        return (graph, a, b, c);
    }

    // ---- 1. 途中の回では後続が動かない ----

    [Test]
    [DisplayName("最終回だけ完了になり、後続の待ちが解ける")]
    public async Task Advance_CompletesOnlyOnFinalRepetition()
    {
        var (graph, a, b, c) = Scene();
        a.Status = NodeStatus.Done;
        a.CompletedAt = T0;

        foreach (var expected in new[] { 1, 2 })
        {
            var step = RepeatService.Advance(b, T0);
            await Assert.That(step.Applied).IsTrue();
            await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(expected);
            await Assert.That(b.Status).IsEqualTo(NodeStatus.InProgress);
            await Assert.That(b.CompletedAt is null).IsTrue().Because("完了以外では完了日時を持たない");
            await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Blocked).Because("途中の回では後続は待ち");
        }

        var last = RepeatService.Advance(b, T1);
        await Assert.That(last.BecameDone).IsTrue();
        await Assert.That(b.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(b.CompletedAt == T1).IsTrue().Because("完了へ移った時刻");
        await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Ready);
    }

    // ---- 2. 別の未完了先行があると、最終回でも着手できない ----

    [Test]
    [DisplayName("ほかに未完了の先行があると、最終回を達成しても後続は待ちのまま")]
    public async Task FinalRepetition_DoesNotUnlockWhileAnotherPredecessorIsOpen()
    {
        var (graph, _, b, c) = Scene();
        var other = graph.AddNode(new TodoNode { Title = "もうひとつの先行" });
        graph.Connect(other.Id, c.Id);

        var impact = CompletionImpact.Calculate(graph, [b.Id]);
        await Assert.That(impact.Unlocked.Count).IsEqualTo(0).Because("完了予告にも出さない");

        RepeatService.Configure(b, 3, 2, T0);
        RepeatService.Advance(b, T1);
        await Assert.That(b.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Blocked);
    }

    // ---- 3. 上限での再操作・0 からの減算・無効な設定は何も変えない ----

    [Test]
    [DisplayName("上限での加算・0 からの減算・範囲外の設定では、データも日時も動かない")]
    public async Task InvalidOperations_LeaveTheModelUntouched()
    {
        var (_, _, b, _) = Scene();
        RepeatService.Configure(b, 3, 3, T0);
        var completedAt = b.CompletedAt;

        var again = RepeatService.Advance(b, T1);
        await Assert.That(again.Applied).IsFalse();
        await Assert.That(again.Error).IsNull().Because("押しても何も起きないだけで、失敗ではない");
        await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(3);
        await Assert.That(b.CompletedAt == completedAt).IsTrue().Because("上限での再操作では日時も動かない");

        RepeatService.Configure(b, 3, 0, T0);
        var below = RepeatService.StepBack(b, T1);
        await Assert.That(below.Applied).IsFalse();
        await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(0);

        foreach (var (target, completed) in new[] { (1, 0), (10000, 0), (3, 4), (3, -1) })
        {
            var rejected = RepeatService.Configure(b, target, completed, T1);
            await Assert.That(rejected.Applied).IsFalse();
            await Assert.That(rejected.IsRejected).IsTrue();
            await Assert.That(b.Repeat!.TargetCount).IsEqualTo(3);
            await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(0);
        }

        await Assert.That(RepeatService.Validate(b)).IsNull();
    }

    // ---- 4. 完了から戻すと、未着手の後続だけがまた待ちになる ----

    [Test]
    [DisplayName("3/3 から 2/3 へ戻すと完了日時が消え、未着手の後続だけが待ちに返る")]
    public async Task StepBackFromDone_ClearsCompletionAndBlocksOnlyUntouchedSuccessors()
    {
        var (graph, a, b, c) = Scene();
        a.Status = NodeStatus.Done; a.CompletedAt = T0;
        RepeatService.Configure(b, 3, 3, T0);
        await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Ready);

        var started = graph.AddNode(new TodoNode { Title = "もう始めている後続", Status = NodeStatus.InProgress });
        graph.Connect(b.Id, started.Id);

        var back = RepeatService.StepBack(b, T1);
        await Assert.That(back.LeftDone).IsTrue();
        await Assert.That(b.Status).IsEqualTo(NodeStatus.InProgress);
        await Assert.That(b.CompletedAt is null).IsTrue().Because("完了以外では完了日時を持たない");
        await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Blocked);
        await Assert.That(started.Status).IsEqualTo(NodeStatus.InProgress).Because("進行中の後続は巻き戻さない");
    }

    // ---- 5. 取り消しと再開 ----

    [Test]
    [DisplayName("取り消しは回数を保ち、再開すると回数どおりの状態に戻る")]
    public async Task Cancel_KeepsCountsAndResumeRestoresTheMatchingStatus()
    {
        var (graph, _, b, c) = Scene();
        RepeatService.Advance(b, T0);

        var cancelled = RepeatService.Cancel(b, T1);
        await Assert.That(cancelled.Applied).IsTrue();
        await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(1).Because("水増しも切り捨てもしない");
        await Assert.That(b.CompletedAt is null).IsTrue().Because("完了以外では完了日時を持たない");
        await Assert.That(graph.ReadinessOf(c)).IsEqualTo(Readiness.Ready).Because("取り消しも後続の待ちを解く");

        await Assert.That(RepeatService.Advance(b, T1).IsRejected).IsTrue();
        await Assert.That(RepeatService.StepBack(b, T1).IsRejected).IsTrue();
        await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(1);

        // 取り消し中の設定変更は取り消しのまま。
        await Assert.That(RepeatService.Configure(b, 5, 5, T1).Applied).IsTrue();
        await Assert.That(b.Status).IsEqualTo(NodeStatus.Cancelled);
        await Assert.That(b.CompletedAt is null).IsTrue().Because("完了以外では完了日時を持たない");

        // 再開して完了になるときの完了日時は再開時刻。
        var resumed = RepeatService.Resume(b, T1);
        await Assert.That(resumed.BecameDone).IsTrue();
        await Assert.That(b.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(b.CompletedAt == T1).IsTrue().Because("完了へ移った時刻");

        RepeatService.Configure(b, 5, 0, T1);
        RepeatService.Cancel(b, T1);
        RepeatService.Resume(b, T1);
        await Assert.That(b.Status).IsEqualTo(NodeStatus.NotStarted);
    }

    // ---- 6. 目標と達成回数は同じ操作で反映する ----

    [Test]
    [DisplayName("完了のまま目標を増やすと進行中へ戻り、完了日時も外れる")]
    public async Task RaisingTheTarget_MovesADoneItemBackToInProgress()
    {
        var (_, _, b, _) = Scene();
        RepeatService.Configure(b, 3, 3, T0);
        await Assert.That(b.CompletedAt == T0).IsTrue().Because("完了へ移った時刻");

        var widened = RepeatService.Configure(b, 5, 3, T1);
        await Assert.That(widened.LeftDone).IsTrue();
        await Assert.That(b.Repeat!.TargetCount).IsEqualTo(5);
        await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(3);
        await Assert.That(b.Status).IsEqualTo(NodeStatus.InProgress);
        await Assert.That(b.CompletedAt is null).IsTrue().Because("完了以外では完了日時を持たない");

        // 完了を保ったままの編集では、もとの完了日時を書き換えない。
        RepeatService.Configure(b, 3, 3, T1);
        var stamp = b.CompletedAt;
        RepeatService.Configure(b, 4, 4, T1.AddDays(1));
        await Assert.That(b.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(b.CompletedAt == stamp).IsTrue().Because("完了を保つ編集では日時を変えない");
    }

    [Test]
    [DisplayName("解除しても、そのときの状態は通常の項目として残る")]
    public async Task Clear_KeepsTheCurrentStatus()
    {
        var (_, _, b, _) = Scene();
        RepeatService.Advance(b, T0);
        RepeatService.Clear(b, T1);

        await Assert.That(b.Repeat).IsNull();
        await Assert.That(b.Status).IsEqualTo(NodeStatus.InProgress);
        await Assert.That(RepeatService.Validate(b)).IsNull();
    }

    // ---- 7. 完了予告に渡すのは、今回完了へ移るものだけ ----

    [Test]
    [DisplayName("完了予告は最終回の項目だけを対象にする")]
    public async Task CompletionPreview_CountsOnlyItemsThatFinishNow()
    {
        var (graph, a, b, c) = Scene();
        var plain = graph.AddNode(new TodoNode { Title = "通常の先行" });
        graph.Connect(plain.Id, c.Id);
        a.Status = NodeStatus.Done;
        a.CompletedAt = T0;

        await Assert.That(RepeatService.IsFinalNext(b)).IsFalse();
        await Assert.That(CompletionImpact.Calculate(graph, [plain.Id]).Unlocked.Count).IsEqualTo(0);

        RepeatService.Configure(b, 3, 2, T0);
        await Assert.That(RepeatService.IsFinalNext(b)).IsTrue();
        var both = CompletionImpact.Calculate(graph, [b.Id, plain.Id]);
        await Assert.That(both.Unlocked.SetEquals([c.Id])).IsTrue();
    }

    // ---- 9. 保存・複製 ----

    [Test]
    [DisplayName("保存して読み直しても回数が保たれ、複製は回数を共有しない")]
    public async Task SaveLoadAndClone_KeepCountsIndependently()
    {
        var (graph, _, b, _) = Scene();
        RepeatService.Advance(b, T0);
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"todotree-repeat-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(path, graph.Project);
            await Assert.That(graph.Project.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);

            var loaded = store.Load(path);
            var restored = loaded.Nodes.First(n => n.Id == b.Id);
            await Assert.That(restored.Repeat!.TargetCount).IsEqualTo(3);
            await Assert.That(restored.Repeat!.CompletedCount).IsEqualTo(1);
            await Assert.That(restored.Status).IsEqualTo(NodeStatus.InProgress);

            var copy = loaded.DeepClone();
            RepeatService.Advance(copy.Nodes.First(n => n.Id == b.Id), T1);
            await Assert.That(restored.Repeat!.CompletedCount).IsEqualTo(1).Because("深いコピーで参照を共有しない");
        }
        finally
        {
            foreach (var candidate in new[] { path, path + ".bak", path + ".tmp" })
            {
                if (File.Exists(candidate)) File.Delete(candidate);
            }
        }
    }

    [Test]
    [DisplayName("形式6の不正な回数と、旧形式を名乗る回数入りデータは読み込みを拒否する")]
    public async Task Load_RejectsBrokenCountsAndBackdatedSchema()
    {
        var store = new JsonProjectStore();
        var directory = Path.Combine(Path.GetTempPath(), $"todotree-repeat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var broken = Build(6, node => { node.Repeat = new RepeatProgress { TargetCount = 3, CompletedCount = 3 }; node.Status = NodeStatus.InProgress; });
            var backdated = Build(5, node => node.Repeat = new RepeatProgress { TargetCount = 3, CompletedCount = 1 });

            foreach (var (name, project) in new[] { ("broken", broken), ("backdated", backdated) })
            {
                var path = Path.Combine(directory, $"{name}.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(project, JsonProjectStore.SerializerOptions));
                await Assert.That(() => store.Load(path)).Throws<InvalidDataException>();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static TodoProject Build(int schemaVersion, Action<TodoNode> setup)
        {
            var node = new TodoNode { Title = "練習" };
            setup(node);
            return new TodoProject { SchemaVersion = schemaVersion, Nodes = [node] };
        }
    }

    // ---- 10. 部品と書き出し ----

    [Test]
    [DisplayName("部品は目標だけを持ち出し、実績は 0 に戻す")]
    public async Task Template_KeepsTargetAndResetsProgress()
    {
        var (graph, a, b, _) = Scene();
        RepeatService.Advance(b, T0);

        var template = BranchTemplate.Capture(graph.Project, [a.Id, b.Id], "練習の型");
        var captured = template.Nodes.First(n => n.Title == b.Title);
        await Assert.That(captured.Repeat!.TargetCount).IsEqualTo(3);
        await Assert.That(captured.Repeat!.CompletedCount).IsEqualTo(0);
        await Assert.That(captured.Status).IsEqualTo(NodeStatus.NotStarted);
        await Assert.That(b.Repeat!.CompletedCount).IsEqualTo(1).Because("元のプロジェクトは変えない");

        var placed = BranchTemplate.Instantiate(template, 0, 0);
        var instance = placed.Nodes.First(n => n.Title == b.Title);
        await Assert.That(instance.Repeat!.CompletedCount).IsEqualTo(0);
        await Assert.That(instance.CompletedAt is null).IsTrue();
        instance.Repeat!.TargetCount = 9;
        await Assert.That(captured.Repeat!.TargetCount).IsEqualTo(3).Because("配置しても型は変わらない");
    }

    [Test]
    [DisplayName("書き出しに回数が入り、依存グラフには自己辺が混じらない")]
    public async Task Export_ShowsCountsWithoutSelfEdges()
    {
        var (graph, _, b, _) = Scene();
        RepeatService.Advance(b, T0);

        var mermaid = GraphExporter.ToMermaid(graph.Project);
        await Assert.That(mermaid.Contains("1 / 3 回")).IsTrue();
        await Assert.That(GraphExporter.ToMarkdown(graph.Project, T1).Contains("1 / 3 回")).IsTrue();
        await Assert.That(graph.Edges.Any(e => e.FromId == e.ToId)).IsFalse();
        await Assert.That(graph.Project.Edges.Count).IsEqualTo(2);
    }

    // ---- 分割は初版では対象外 ----

    [Test]
    [DisplayName("回数つきの項目は分割しない")]
    public async Task Split_SkipsRepeatingItems()
    {
        var (graph, _, b, _) = Scene();
        var created = StepSplitter.Split(graph, b.Id, OutlineParser.Parse("下ごしらえ\n本番"));
        await Assert.That(created.Count).IsEqualTo(0);
        await Assert.That(graph.NodeCount).IsEqualTo(3);
        await Assert.That(graph.Project.Edges.Count).IsEqualTo(2);
    }

    // ---- 循環の拒否は変わらない ----

    [Test]
    [DisplayName("自分への接続は依存線にならず、通常の循環も拒否したまま")]
    public async Task SelfConnection_IsNeverStoredAsAnEdge()
    {
        var (graph, a, b, c) = Scene();
        await Assert.That(graph.CanConnect(b.Id, b.Id)).IsEqualTo(ConnectionCheck.SameNode);
        await Assert.That(graph.Connect(b.Id, b.Id)).IsNull();
        await Assert.That(graph.CanConnect(c.Id, a.Id)).IsEqualTo(ConnectionCheck.WouldCreateCycle);
        await Assert.That(graph.Project.Edges.Count).IsEqualTo(2);
        await Assert.That(graph.HasCycle()).IsFalse();
    }
}
