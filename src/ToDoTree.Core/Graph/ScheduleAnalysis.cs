using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>1 ステップの逆算結果。</summary>
public sealed record ScheduleInfo(
    DateTimeOffset? LatestFinish,
    DateTimeOffset? LatestStart,
    int DurationDays,
    bool AtRisk);

public sealed class ScheduleOptions
{
    /// <summary>1 日にこの作業へ充てられる時間。見積り（分）を日数に直すために使う。</summary>
    public double WorkingHoursPerDay { get; set; } = 4;
}

/// <summary>
/// 期限から逆算して「いつまでに終わらせないと間に合わないか」を出す。
/// 依存関係があるので、後ろのステップの期限が前のステップの締切になる。
/// </summary>
public static class ScheduleAnalysis
{
    public static IReadOnlyDictionary<Guid, ScheduleInfo> Compute(
        TodoGraph graph,
        DateTimeOffset? today = null,
        ScheduleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        options ??= new ScheduleOptions();
        var now = today ?? DateTimeOffset.Now;
        var result = new Dictionary<Guid, ScheduleInfo>(graph.NodeCount);

        var order = graph.TopologicalOrder();
        if (order is null)
        {
            return result;
        }

        // 後ろから前へ。後続の「開始しないといけない日」が、自分の締切になる。
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];
            var duration = DurationDays(node, options);

            DateTimeOffset? latestFinish = node.Due;

            foreach (var child in graph.ChildrenOf(node.Id))
            {
                if (!result.TryGetValue(child.Id, out var childInfo) || childInfo.LatestStart is not { } childStart)
                {
                    continue;
                }

                var limit = childStart.AddDays(-1);
                latestFinish = latestFinish is { } current && current <= limit ? current : limit;
            }

            var latestStart = latestFinish?.AddDays(-(duration - 1));
            var atRisk = latestStart is { } start && !node.IsSettled && start.Date < now.Date;

            result[node.Id] = new ScheduleInfo(latestFinish, latestStart, duration, atRisk);
        }

        return result;
    }

    public static int DurationDays(TodoNode node, ScheduleOptions? options = null)
    {
        options ??= new ScheduleOptions();
        var minutesPerDay = Math.Max(30, options.WorkingHoursPerDay * 60);
        var estimate = node.EstimateMinutes ?? GraphAnalysis.DefaultEstimateMinutes;
        return Math.Max(1, (int)Math.Ceiling(estimate / minutesPerDay));
    }
}
