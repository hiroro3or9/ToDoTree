using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

public static class TaskDetailsValidation
{
    public static string? Validate(TodoProject project, int version)
    {
        if (project.Nodes.Any(n => n is null)) return "ステップに空の要素があります。";
        if (version < 9 && (project.Inbox.Count > 0 || project.Nodes.Any(n =>
            n.Checklist.Count > 0 || n.IsManuallyBlocked || !string.IsNullOrEmpty(n.BlockReason))))
            return "受信箱・チェックリスト・手動ブロックは形式9以降で保存してください。";
        if (project.Inbox.Any(i => i is null || i.Id == Guid.Empty || string.IsNullOrWhiteSpace(i.Title))
            || project.Inbox.Select(i => i.Id).Distinct().Count() != project.Inbox.Count)
            return "受信箱の項目が不正です。";
        foreach (var node in project.Nodes)
            if (node.Checklist.Any(i => i is null || i.Id == Guid.Empty || i.Title is null)
                || node.Checklist.Select(i => i.Id).Distinct().Count() != node.Checklist.Count)
                return "チェックリストの項目が不正です。";
        return null;
    }
}
