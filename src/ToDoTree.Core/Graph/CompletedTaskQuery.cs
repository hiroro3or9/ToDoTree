using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>表示上の絞り込みに関係なく、指定日の完了実績を取得する。</summary>
public static class CompletedTaskQuery
{
    public static IReadOnlyList<TodoNode> ForDate(
        IEnumerable<TodoNode> nodes, DateOnly date, TimeZoneInfo timeZone) =>
        nodes.Where(node => node.Status == NodeStatus.Done
                && node.CompletedAt is { } completedAt
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(completedAt, timeZone).DateTime) == date)
            .OrderByDescending(node => node.CompletedAt)
            .ThenBy(node => node.Id)
            .ToArray();
}
