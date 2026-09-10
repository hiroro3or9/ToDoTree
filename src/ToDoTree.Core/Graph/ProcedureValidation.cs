using ToDoTree.Core.Models;
using System.Diagnostics.CodeAnalysis;

namespace ToDoTree.Core.Graph;

public static class ProcedureValidation
{
    public static void ValidateDocument(TodoProject project, int declaredVersion)
    {
        Check(Enum.IsDefined(project.DocumentKind), "文書種別が不正です。");
        Check(declaredVersion >= 11 || (project.Procedure is null && project.DocumentKind == DocumentKind.Todo),
            "形式11より前のファイルに作業手順が入っています。");
        if (project.DocumentKind == DocumentKind.Todo)
        {
            Check(project.Procedure is null, "通常プロジェクトに作業手順のデータがあります。");
            return;
        }
        Check(project.Procedure is not null, "手順データがありません。");
        Check(project.Nodes.Count == 0 && project.Edges.Count == 0 && project.Blocks.Count == 0 &&
            project.Variables.Count == 0 && project.Inbox.Count == 0 && project.Bookmark is null,
            "手順文書に通常プロジェクトのデータが混在しています。");
        ValidateData(project.Procedure!);
    }

    public static void ValidateData(ProcedureData data)
    {
        Check(data.Id != Guid.Empty && data.Revision > 0 && data.Runs is not null, "手順のID・改訂番号・実施一覧が不正です。");
        ValidateGraph(data.Definition);
        Check(ProcedureService.Serialize(data.Definition) == ProcedureService.Serialize(ProcedureService.Normalize(data.Definition)),
            "手順の定義に実績または日時が入っています。");
        Check(data.Runs!.All(r => r is not null) && data.Runs.Select(r => r.Id).Distinct().Count() == data.Runs.Count,
            "実施記録のIDが重複、または空の要素があります。");
        Check(data.Runs.Count(r => r.Status is RunStatus.Running or RunStatus.Paused) <= 1, "未完了の実施が複数あります。");
        foreach (var run in data.Runs)
        {
            Check(run.Id != Guid.Empty && !string.IsNullOrWhiteSpace(run.Name) && Enum.IsDefined(run.Status) &&
                run.Revision > 0 && run.Revision <= data.Revision, "実施のID・名前・状態・改訂番号が不正です。");
            Check((run.Status == RunStatus.Completed) == run.CompletedAt.HasValue &&
                (run.Status == RunStatus.Abandoned) == run.AbandonedAt.HasValue, "実施の状態と終了日時が一致しません。");
            if (run.Status is RunStatus.Running or RunStatus.Paused)
                Check(run.Id == data.Runs[^1].Id, "未完了の実施は最新の記録である必要があります。");
            ValidateGraph(run.Definition);
            Check(ProcedureService.Serialize(run.Definition) == ProcedureService.Serialize(ProcedureService.Normalize(run.Definition)),
                "開始時の手順定義に実績が入っています。");
            ValidateState(run.Current, run.Definition);
            Check(run.Status != RunStatus.Completed || ProcedureService.IsComplete(run.Current), "未実施のステップがある完了記録です。");
            Check(run.Status is not (RunStatus.Running or RunStatus.Paused) || !ProcedureService.IsComplete(run.Current), "全件完了した実施の状態が不正です。");
            Check(run.Status != RunStatus.Paused || run.Current.Bookmark is not null, "中断記録の再開場所がありません。");
            Check(run.Events is not null && run.Events.Count > 0 && run.Events.All(e => e is not null), "実施イベントがありません。");
            Check(run.Events!.Select(e => e.Id).Distinct().Count() == run.Events.Count, "実施イベントのIDが重複しています。");
            var ids = run.Current.Graph.Nodes.Select(n => n.Id).ToHashSet();
            for (var i = 0; i < run.Events.Count; i++)
            {
                var entry = run.Events[i];
                Check(entry.Id != Guid.Empty && entry.Sequence == i + 1 && Enum.IsDefined(entry.Kind) &&
                    (entry.NodeId is null || ids.Contains(entry.NodeId.Value)), "実施イベントの連番・種別・参照が不正です。");
            }
            Check(run.Events[0].Kind == RunEventKind.Started && run.Events[0].At == run.StartedAt &&
                run.Events[^1].At == run.UpdatedAt, "実施イベントと開始・更新日時が一致しません。");
            var last = run.Events[^1];
            Check(run.Status switch
            {
                RunStatus.Paused => last.Kind == RunEventKind.Paused,
                RunStatus.Completed => last.Kind == RunEventKind.Completed && last.At == run.CompletedAt,
                RunStatus.Abandoned => last.Kind == RunEventKind.Abandoned && last.At == run.AbandonedAt,
                _ => last.Kind is not (RunEventKind.Paused or RunEventKind.Completed or RunEventKind.Abandoned),
            }, "実施の状態と最後のイベントが一致しません。");
            Check(run.Checkpoints is not null && run.Checkpoints.All(c => c is not null), "中断記録が不正です。");
            Check(run.Checkpoints!.Select(c => c.Id).Distinct().Count() == run.Checkpoints.Count, "中断記録のIDが重複しています。");
            Check(run.Checkpoints.Select(c => c.Sequence).Distinct().Count() == run.Checkpoints.Count, "同じイベントの保存状態が重複しています。");
            Check(run.Events.Where(e => e.Kind == RunEventKind.Paused)
                .All(e => run.Checkpoints.Any(c => c.Sequence == e.Sequence && c.Status == RunStatus.Paused)), "中断イベントの保存状態がありません。");
            foreach (var checkpoint in run.Checkpoints)
            {
                Check(checkpoint.Id != Guid.Empty && checkpoint.Sequence > 0 && checkpoint.Sequence <= run.Events.Count &&
                    checkpoint.Status is RunStatus.Paused or RunStatus.Completed, "中断記録の参照・状態が不正です。");
                var entry = run.Events[checkpoint.Sequence - 1];
                Check(entry.At == checkpoint.At && entry.Kind == (checkpoint.Status == RunStatus.Paused ? RunEventKind.Paused : RunEventKind.Completed),
                    "中断記録とイベントが一致しません。");
                ValidateState(checkpoint.State, run.Definition);
                Check(checkpoint.Status != RunStatus.Completed || ProcedureService.IsComplete(checkpoint.State), "訂正前の完了記録が不正です。");
                Check(checkpoint.Status != RunStatus.Paused || checkpoint.State.Bookmark is not null, "中断記録にしおりがありません。");
            }
        }
    }

    public static void ValidateGraph(GraphSnapshot graph)
    {
        Check(graph is not null && graph.Nodes is not null && graph.Edges is not null && graph.Blocks is not null && graph.Variables is not null,
            "手順のグラフがありません。");
        Check(graph!.Nodes.All(n => n is not null) && graph.Edges.All(e => e is not null) &&
            graph.Blocks.All(b => b is not null) && graph.Variables.All(v => v is not null), "グラフに空の要素があります。");
        Check(graph.Nodes.All(n => n.Tags is not null && n.Checklist.All(c => c is not null)), "項目のタグまたはチェックリストが不正です。");
        BranchTemplate.Validate(graph.ToProject("作業手順"));
        Check(graph.Nodes.All(n => n.Id != Guid.Empty && Enum.IsDefined(n.Status) && Enum.IsDefined(n.Kind) &&
            ((n.Status == NodeStatus.Done) == n.CompletedAt.HasValue)), "項目の状態・日時・IDが不正です。");
    }

    private static void ValidateState(RunState state, GraphSnapshot definition)
    {
        Check(state is not null && state.Notes is not null, "実施状態または実施メモがありません。");
        ValidateGraph(state!.Graph);
        var ids = state.Graph.Nodes.Select(n => n.Id).ToHashSet();
        Check(state.Notes!.Keys.All(ids.Contains) && state.Notes.Values.All(n => n is not null) &&
            (state.Bookmark is null || ids.Contains(state.Bookmark.NodeId)), "メモまたはしおりの参照先・内容が不正です。");
        var normalized = ProcedureService.Normalize(state.Graph);
        // The run's definition includes its chosen values; these remain frozen throughout the run.
        Check(ProcedureService.Serialize(normalized) == ProcedureService.Serialize(ProcedureService.Normalize(definition)),
            "実施中の手順構造が開始時の定義と異なります。");
        Check(state.Graph.Variables.Select(v => v.Name).SequenceEqual(definition.Variables.Select(v => v.Name)), "実施の変数名が定義と異なります。");
    }

    private static void Check([DoesNotReturnIf(false)] bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
}
