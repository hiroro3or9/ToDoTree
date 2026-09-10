using System.Text.Json;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>Every operation returns a candidate; the caller adopts it only after durable save succeeds.</summary>
public static class ProcedureService
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    public static GraphSnapshot Normalize(GraphSnapshot source)
    {
        var result = source.Clone();
        foreach (var node in result.Nodes)
        {
            node.Status = NodeStatus.NotStarted;
            node.CompletedAt = null;
            node.Due = null;
            node.IsManuallyBlocked = false;
            node.BlockReason = "";
            if (node.Repeat is { } repeat) repeat.CompletedCount = 0;
            foreach (var item in node.Checklist) item.IsChecked = false;
            node.CreatedAt = node.UpdatedAt = DateTimeOffset.UnixEpoch;
        }
        return result;
    }

    public static TodoProject Create(TodoProject fragment) => new()
    {
        Name = fragment.Name, DocumentKind = DocumentKind.Procedure,
        Procedure = new() { Definition = Normalize(GraphSnapshot.Capture(fragment)) },
    };

    public static bool IsComplete(RunState state) => state.Graph.Nodes.Count > 0 &&
        state.Graph.Nodes.All(n => n.Status == NodeStatus.Done && n.Checklist.All(c => c.IsChecked));

    public static ProcedureData Start(ProcedureData source, string name, IEnumerable<ProjectVariable> variables,
        DateTimeOffset? due, DateTimeOffset now)
    {
        Require(!source.Runs.Any(r => r.Status is RunStatus.Running or RunStatus.Paused), "未完了の実施があります。続きから再開してください。");
        var data = source.Clone();
        var graph = Normalize(data.Definition);
        graph.Variables = [.. variables.Select(v => v.Clone())];
        Require(graph.Nodes.Count > 0, "手順には1件以上のステップが必要です。");
        foreach (var node in graph.Nodes) { node.Due = due; node.CreatedAt = node.UpdatedAt = now; }
        var run = new ProcedureRun
        {
            Name = string.IsNullOrWhiteSpace(name) ? now.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : name.Trim(),
            Revision = data.Revision, StartedAt = now, UpdatedAt = now,
            Definition = Normalize(graph), Current = new() { Graph = graph },
        };
        data.Runs.Add(run);
        AddEvent(run, RunEventKind.Started, now);
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static ProcedureData ChangeStep(ProcedureData source, Guid runId, Guid nodeId,
        Action<TodoNode, RunState> change, DateTimeOffset now, RunEventKind kind = RunEventKind.StepChanged)
    {
        var data = source.Clone();
        var run = Find(data, runId);
        Require(run.Status == RunStatus.Running, "実施中の作業だけを変更できます。");
        var node = run.Current.Graph.Nodes.Single(n => n.Id == nodeId);
        var before = Serialize(run.Current);
        change(node, run.Current);
        if (before == Serialize(run.Current)) return data;
        node.UpdatedAt = now;
        AddEvent(run, kind, now, nodeId, before, Serialize(run.Current));
        CompleteIfReady(run, now);
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static void SetStatus(TodoNode node, NodeStatus status, DateTimeOffset now)
    {
        if (node.Repeat is { } repeat)
        {
            if (status == NodeStatus.Done) throw new InvalidOperationException("回数項目は1回達成または回数訂正で完了してください。");
            node.Status = status;
            if (status == NodeStatus.NotStarted) repeat.CompletedCount = 0;
            if (status == NodeStatus.InProgress && repeat.CompletedCount == repeat.TargetCount)
                node.Status = NodeStatus.Done;
        }
        else node.Status = status;
        node.CompletedAt = node.Status == NodeStatus.Done ? node.CompletedAt ?? now : null;
    }

    public static ProcedureData Pause(ProcedureData source, Guid id, Guid nodeId, string note, DateTimeOffset now)
    {
        var data = source.Clone(); var run = Find(data, id);
        Require(run.Status == RunStatus.Running, "実施中の作業だけを中断できます。");
        Require(run.Current.Graph.Nodes.Any(n => n.Id == nodeId), "再開するステップを選んでください。");
        run.Current.Bookmark = new() { NodeId = nodeId, Note = note };
        run.Status = RunStatus.Paused;
        AddEvent(run, RunEventKind.Paused, now);
        Checkpoint(run, now);
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static ProcedureData Resume(ProcedureData source, Guid id, DateTimeOffset now)
    {
        var data = source.Clone(); var run = Find(data, id);
        Require(run.Status == RunStatus.Paused, "中断中の作業だけを再開できます。");
        run.Status = RunStatus.Running;
        AddEvent(run, RunEventKind.Resumed, now);
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static ProcedureData Abandon(ProcedureData source, Guid id, string reason, DateTimeOffset now)
    {
        var data = source.Clone(); var run = Find(data, id);
        Require(run.Status is RunStatus.Running or RunStatus.Paused, "未完了の作業だけを打ち切れます。");
        run.Status = RunStatus.Abandoned; run.AbandonedAt = now; run.AbandonReason = reason;
        AddEvent(run, RunEventKind.Abandoned, now, after: reason);
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static ProcedureData Correct(ProcedureData source, Guid id, Guid nodeId, DateTimeOffset now)
    {
        var data = source.Clone(); var run = Find(data, id);
        Require(run.Status == RunStatus.Completed && data.Runs[^1].Id == id, "次回がまだない最新の完了記録だけを訂正できます。");
        var node = run.Current.Graph.Nodes.Single(n => n.Id == nodeId);
        // Retain the completed state and its original completion time before reopening.
        Checkpoint(run, run.CompletedAt!.Value);
        var before = Serialize(run.Current);
        SetStatus(node, NodeStatus.NotStarted, now); node.UpdatedAt = now;
        run.Status = RunStatus.Running; run.CompletedAt = null;
        AddEvent(run, RunEventKind.Corrected, now, nodeId, before, Serialize(run.Current));
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static ProcedureData RestoreEdit(ProcedureData source, Guid id, RunState state, bool redo, DateTimeOffset now)
    {
        var data = source.Clone(); var run = Find(data, id);
        Require(run.Status == RunStatus.Running, "実施中の操作だけを取り消せます。");
        var before = Serialize(run.Current);
        run.Current = state.Clone();
        AddEvent(run, redo ? RunEventKind.Redo : RunEventKind.Undo, now, before: before, after: Serialize(run.Current));
        CompleteIfReady(run, now);
        ProcedureValidation.ValidateData(data);
        return data;
    }

    public static ProcedureRun Find(ProcedureData data, Guid id) => data.Runs.Single(r => r.Id == id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void CompleteIfReady(ProcedureRun run, DateTimeOffset now)
    {
        if (!IsComplete(run.Current)) return;
        run.Status = RunStatus.Completed; run.CompletedAt = now;
        AddEvent(run, RunEventKind.Completed, now);
    }
    private static void AddEvent(ProcedureRun run, RunEventKind kind, DateTimeOffset now,
        Guid? nodeId = null, string before = "", string after = "")
    {
        run.UpdatedAt = now;
        run.Events.Add(new() { Sequence = run.Events.Count + 1, Kind = kind, At = now, NodeId = nodeId, Before = before, After = after });
    }
    private static void Checkpoint(ProcedureRun run, DateTimeOffset at) => run.Checkpoints.Add(new()
    { Sequence = run.Events.Count, At = at, Status = run.Status, State = run.Current.Clone() });
}
