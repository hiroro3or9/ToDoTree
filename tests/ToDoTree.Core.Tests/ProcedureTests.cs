using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class ProcedureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 9, 0, 0, TimeSpan.FromHours(9));
    private static TodoProject Source() => new()
    {
        Name = "バックアップ",
        Variables = [new() { Name = "保存先", Value = "旧サーバー" }],
        Nodes = [new() { Title = "{保存先}へ転送", Notes = "転送先: {保存先}", X = 80, Y = 120,
            Checklist = [new() { Title = "容量を確認" }] },
            new() { Title = "復元確認", X = 400, Y = 120, Repeat = new() { TargetCount = 2 } }],
    };
    private static ProcedureData Started()
    {
        var source = Source();
        new TodoGraph(source).Connect(source.Nodes[0].Id, source.Nodes[1].Id);
        return ProcedureService.Start(ProcedureService.Create(source).Procedure!, "第1週", source.Variables, Now.AddDays(1), Now);
    }
    private static ProcedureData Finish(ProcedureData data)
    {
        foreach (var node in data.Runs[^1].Current.Graph.Nodes)
            data = ProcedureService.ChangeStep(data, data.Runs[^1].Id, node.Id, (n, _) =>
            {
                foreach (var item in n.Checklist) item.IsChecked = true;
                if (n.Repeat is { } repeat) RepeatService.Configure(n, repeat.TargetCount, repeat.TargetCount, Now.AddHours(1));
                else ProcedureService.SetStatus(n, NodeStatus.Done, Now.AddHours(1));
            }, Now.AddHours(1));
        return data;
    }
    private static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
        catch (InvalidDataException) { return true; }
    }

    [Test]
    public async Task CaptureAndStart_ClearExecutionOnlyWithoutChangingSource()
    {
        var source = Source(); var node = source.Nodes[0];
        node.Status = NodeStatus.Done; node.CompletedAt = Now; node.Due = Now;
        node.Checklist[0].IsChecked = true; source.Bookmark = new() { NodeId = node.Id, Note = "元のメモ" };
        var before = ProcedureService.Serialize(source);
        var doc = ProcedureService.Create(source);
        var data = ProcedureService.Start(doc.Procedure!, "今週", [new() { Name = "保存先", Value = "新サーバー" }], null, Now);
        var run = data.Runs.Single();
        await Assert.That(ProcedureService.Serialize(source)).IsEqualTo(before);
        await Assert.That(run.Current.Graph.Nodes.All(n => n.Status == NodeStatus.NotStarted && n.CompletedAt is null && n.Due is null)).IsTrue();
        await Assert.That(run.Current.Graph.Nodes[0].Checklist[0].IsChecked).IsFalse();
        await Assert.That(run.Current.Graph.Variables.Single().Value).IsEqualTo("新サーバー");
        await Assert.That(doc.Procedure!.Definition.Variables.Single().Value).IsEqualTo("旧サーバー");
        await Assert.That(run.Current.Bookmark is null).IsTrue();
        await Assert.That(Rejected(() => ProcedureService.Start(data, "重複", source.Variables, null, Now))).IsTrue();
    }

    [Test]
    public async Task Completion_RequiresChecklistAndRepeatAndDoesNotCountCancellation()
    {
        var data = Started(); var id = data.Runs[0].Id; var nodes = data.Runs[0].Current.Graph.Nodes;
        data = ProcedureService.ChangeStep(data, id, nodes[0].Id, (n, _) => ProcedureService.SetStatus(n, NodeStatus.Done, Now), Now);
        data = ProcedureService.ChangeStep(data, id, nodes[1].Id, (n, _) => RepeatService.Advance(n, Now), Now);
        await Assert.That(data.Runs[0].Status).IsEqualTo(RunStatus.Running);
        data = ProcedureService.ChangeStep(data, id, nodes[1].Id, (n, _) => RepeatService.Advance(n, Now), Now);
        await Assert.That(data.Runs[0].Status).IsEqualTo(RunStatus.Running);
        data = ProcedureService.ChangeStep(data, id, nodes[0].Id, (n, _) =>
        { n.Checklist[0].IsChecked = true; ProcedureService.SetStatus(n, NodeStatus.Cancelled, Now); }, Now);
        await Assert.That(data.Runs[0].Status).IsEqualTo(RunStatus.Running);
        data = ProcedureService.ChangeStep(data, id, nodes[0].Id, (n, _) => ProcedureService.SetStatus(n, NodeStatus.Done, Now.AddMinutes(3)), Now.AddMinutes(3));
        await Assert.That(data.Runs[0].Status).IsEqualTo(RunStatus.Completed);
        await Assert.That(data.Runs[0].CompletedAt).IsEqualTo(Now.AddMinutes(3));
        await Assert.That(data.Runs[0].Events[^1].Kind).IsEqualTo(RunEventKind.Completed);
        await Assert.That(Rejected(() => ProcedureService.ChangeStep(data, id, nodes[0].Id, (n, _) => n.Status = NodeStatus.NotStarted, Now))).IsTrue();
    }

    [Test]
    public async Task PauseResume_PreservesIndependentSnapshotsAndBookmark()
    {
        var data = Started(); var id = data.Runs[0].Id; var nodeId = data.Runs[0].Current.Graph.Nodes[0].Id;
        data = ProcedureService.ChangeStep(data, id, nodeId, (n, s) =>
        { n.IsManuallyBlocked = true; n.BlockReason = "環境待ち"; s.Notes[nodeId] = "転送済み"; n.Checklist[0].IsChecked = true; }, Now);
        data = ProcedureService.Pause(data, id, nodeId, "ここから再開", Now.AddMinutes(10));
        var snapshot = ProcedureService.Serialize(data.Runs[0].Checkpoints[0]);
        await Assert.That(Rejected(() => ProcedureService.ChangeStep(data, id, nodeId, (n, _) => n.Checklist[0].IsChecked = false, Now))).IsTrue();
        data = ProcedureService.Resume(data, id, Now.AddMinutes(20));
        data = ProcedureService.ChangeStep(data, id, nodeId, (_, s) => s.Notes[nodeId] = "確認開始", Now.AddMinutes(21));
        data = ProcedureService.Pause(data, id, nodeId, "二度目", Now.AddMinutes(22));
        await Assert.That(data.Runs[0].Checkpoints.Count).IsEqualTo(2);
        await Assert.That(ProcedureService.Serialize(data.Runs[0].Checkpoints[0])).IsEqualTo(snapshot);
        await Assert.That(data.Runs[0].Current.Bookmark!.Note).IsEqualTo("二度目");
        await Assert.That(data.Runs[0].Current.Graph.Nodes[0].BlockReason).IsEqualTo("環境待ち");
    }

    [Test]
    public async Task NextRun_KeepsHistoryAndUsesLatestRevision()
    {
        var data = Finish(Started()); var first = ProcedureService.Serialize(data.Runs[0]);
        data.Definition.Nodes[0].Title = "新手順"; data.Definition.Variables[0].Value = "今週の保存先"; data.Revision++;
        data = ProcedureService.Start(data, "第2週", data.Definition.Variables, null, Now.AddDays(7));
        await Assert.That(ProcedureService.Serialize(data.Runs[0])).IsEqualTo(first);
        await Assert.That(data.Runs[1].Revision).IsEqualTo(2);
        await Assert.That(data.Runs[1].Current.Graph.Nodes[0].Title).IsEqualTo("新手順");
        await Assert.That(data.Runs[1].Current.Graph.Nodes[1].Repeat!.CompletedCount).IsEqualTo(0);
        await Assert.That(data.Runs[1].Current.Graph.Nodes[0].Checklist[0].IsChecked).IsFalse();
        await Assert.That(data.Runs[1].Current.Notes.Count).IsEqualTo(0);
        await Assert.That(Rejected(() => ProcedureService.Correct(data, data.Runs[0].Id, data.Runs[0].Current.Graph.Nodes[0].Id, Now))).IsTrue();
    }

    [Test]
    public async Task CorrectionAndAbandon_RetainCompletedRecord()
    {
        var data = Finish(Started()); var run = data.Runs[0]; var repeatId = run.Current.Graph.Nodes[1].Id;
        data = ProcedureService.Correct(data, run.Id, repeatId, Now.AddHours(2));
        await Assert.That(data.Runs[0].Status).IsEqualTo(RunStatus.Running);
        await Assert.That(data.Runs[0].Current.Graph.Nodes[1].Repeat!.CompletedCount).IsEqualTo(0);
        await Assert.That(data.Runs[0].Checkpoints[0].State.Graph.Nodes.All(n => n.Status == NodeStatus.Done)).IsTrue();
        data = ProcedureService.Abandon(data, run.Id, "やり直す", Now.AddHours(3));
        await Assert.That(data.Runs[0].Status).IsEqualTo(RunStatus.Abandoned);
        await Assert.That(data.Runs[0].AbandonReason).IsEqualTo("やり直す");
        var next = ProcedureService.Start(data, "やり直し", data.Definition.Variables, null, Now.AddHours(4));
        await Assert.That(next.Runs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RejectedChanges_LeaveOriginalUntouchedAndProtectFrozenDefinition()
    {
        var data = Started(); var id = data.Runs[0].Id; var nodeId = data.Runs[0].Current.Graph.Nodes[0].Id;
        var original = ProcedureService.Serialize(data);
        await Assert.That(Rejected(() => ProcedureService.ChangeStep(data, id, nodeId, (n, _) => n.Title = "変更", Now))).IsTrue();
        await Assert.That(Rejected(() => ProcedureService.ChangeStep(data, id, nodeId, (_, s) => s.Graph.Variables[0].Value = "変更", Now))).IsTrue();
        await Assert.That(ProcedureService.Serialize(data)).IsEqualTo(original);
        var candidate = ProcedureService.ChangeStep(data, id, nodeId, (n, _) => n.Checklist[0].IsChecked = true, Now);
        await Assert.That(ProcedureService.Serialize(data)).IsEqualTo(original).Because("保存前の候補を破棄しても元の実績は維持する");
        candidate.Runs[0].Current.Graph.Nodes[0].Title = "別の参照";
        await Assert.That(data.Runs[0].Current.Graph.Nodes[0].Title).IsEqualTo("{保存先}へ転送");
    }

    [Test]
    public async Task UndoRedo_AddsEventsInsteadOfErasingHistory()
    {
        var data = Started(); var run = data.Runs[0]; var before = run.Current.Clone();
        data = ProcedureService.ChangeStep(data, run.Id, run.Current.Graph.Nodes[1].Id, (n, _) => RepeatService.Advance(n, Now), Now);
        var after = data.Runs[0].Current.Clone();
        data = ProcedureService.RestoreEdit(data, run.Id, before, false, Now.AddMinutes(-1));
        await Assert.That(data.Runs[0].Events[^1].Kind).IsEqualTo(RunEventKind.Undo);
        await Assert.That(data.Runs[0].Current.Graph.Nodes[1].Repeat!.CompletedCount).IsEqualTo(0);
        data = ProcedureService.RestoreEdit(data, run.Id, after, true, Now);
        await Assert.That(data.Runs[0].Events.Count).IsEqualTo(4);
        await Assert.That(data.Runs[0].Current.Graph.Nodes[1].Repeat!.CompletedCount).IsEqualTo(1);
    }

    [Test]
    public async Task FileRoundTripAndLegacy_ReconstructAllHistory()
    {
        var data = Started(); var run = data.Runs[0];
        data = ProcedureService.Pause(data, run.Id, run.Current.Graph.Nodes[0].Id, "あとで", Now.AddMinutes(2));
        var document = new TodoProject { Name = "保存検証", DocumentKind = DocumentKind.Procedure, Procedure = data };
        var path = Path.Combine(Path.GetTempPath(), $"procedure-{Guid.NewGuid():N}.json");
        var store = new JsonProjectStore();
        try
        {
            store.Save(path, document);
            var loaded = store.Load(path);
            await Assert.That(ProcedureService.Serialize(loaded.Procedure)).IsEqualTo(ProcedureService.Serialize(data));
            await Assert.That(loaded.Nodes.Count).IsEqualTo(0);
            loaded.SchemaVersion = 10;
            File.WriteAllText(path, JsonSerializer.Serialize(loaded, JsonProjectStore.SerializerOptions));
            await Assert.That(Rejected(() => store.Load(path))).IsTrue();
            File.WriteAllText(path, "{\"schemaVersion\":10,\"name\":\"従来のタスク\",\"nodes\":[]}");
            var legacy = store.Load(path);
            await Assert.That(legacy.DocumentKind).IsEqualTo(DocumentKind.Todo);
            await Assert.That(legacy.Procedure is null).IsTrue();
            await Assert.That(legacy.SchemaVersion).IsEqualTo(11);
        }
        finally { foreach (var file in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(file)) File.Delete(file); }
    }

    [Test]
    public async Task ResumeCancelledRepeat_DoesNotLoseRecordedAchievements()
    {
        var data = Started(); var runId = data.Runs[0].Id; var nodeId = data.Runs[0].Current.Graph.Nodes[1].Id;
        data = ProcedureService.ChangeStep(data, runId, nodeId, (n, _) => RepeatService.Configure(n, 2, 2, Now), Now);
        data = ProcedureService.ChangeStep(data, runId, nodeId, (n, _) => ProcedureService.SetStatus(n, NodeStatus.Cancelled, Now), Now);
        data = ProcedureService.ChangeStep(data, runId, nodeId, (n, _) => ProcedureService.SetStatus(n, NodeStatus.InProgress, Now.AddMinutes(1)), Now.AddMinutes(1));
        var node = data.Runs[0].Current.Graph.Nodes[1];
        await Assert.That(node.Repeat!.CompletedCount).IsEqualTo(2);
        await Assert.That(node.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(node.CompletedAt).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task Validation_RejectsDamagedHistoryAndMixedDocuments()
    {
        var data = Started(); var bad = data.Clone(); bad.Runs[0].Events[0].Sequence = 3;
        await Assert.That(Rejected(() => ProcedureValidation.ValidateData(bad))).IsTrue();
        bad = data.Clone(); bad.Runs[0].Current.Notes[Guid.NewGuid()] = "不在";
        await Assert.That(Rejected(() => ProcedureValidation.ValidateData(bad))).IsTrue();
        bad = data.Clone(); bad.Runs[0].Current.Graph.Nodes[1].Id = bad.Runs[0].Current.Graph.Nodes[0].Id;
        await Assert.That(Rejected(() => ProcedureValidation.ValidateData(bad))).IsTrue();
        bad = ProcedureService.Pause(data, data.Runs[0].Id, data.Runs[0].Current.Graph.Nodes[0].Id, "中断", Now);
        bad.Runs[0].Checkpoints.Clear();
        await Assert.That(Rejected(() => ProcedureValidation.ValidateData(bad))).IsTrue();
        var doc = new TodoProject { DocumentKind = DocumentKind.Procedure, Procedure = data, Nodes = [new() { Title = "混在" }] };
        await Assert.That(Rejected(() => ProcedureValidation.ValidateDocument(doc, 11))).IsTrue();
    }
}
