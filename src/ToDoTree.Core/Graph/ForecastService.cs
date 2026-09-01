using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>「この調子だといつ終わるか」の見立て。</summary>
public sealed record Forecast(
    int RemainingMinutes,
    int CompletedMinutes,
    double MinutesPerDay,
    bool PaceFromHistory,
    int RemainingDays,
    DateTimeOffset? EstimatedFinish,
    DateTimeOffset? Deadline,
    int? SlackDays)
{
    public static readonly Forecast Empty = new(0, 0, 0, false, 0, null, null, null);

    public bool HasWork => RemainingMinutes > 0;

    /// <summary>いまのペースでは期限に間に合わない。</summary>
    public bool IsLate => SlackDays is < 0;
}

/// <summary>
/// 期限からの逆算（<see cref="ScheduleAnalysis"/>）と対になる、前向きの予測。
/// 残っている見積りを、これまで実際に片付けてきたペースで割る。
/// </summary>
public static class ForecastService
{
    public static Forecast Compute(TodoGraph graph, DateTimeOffset? now = null, ScheduleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var today = now ?? DateTimeOffset.Now;
        var settings = options ?? new ScheduleOptions();
        var defaultPerDay = Math.Max(30, settings.WorkingHoursPerDay * 60);

        var remaining = 0;
        var completed = 0;
        var timedCompleted = 0;
        var timedCount = 0;
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;

        foreach (var node in graph.Nodes)
        {
            if (node.Status == NodeStatus.Cancelled)
            {
                continue;
            }

            var estimate = node.EstimateMinutes ?? GraphAnalysis.DefaultEstimateMinutes;

            if (node.Status == NodeStatus.Done)
            {
                completed += estimate;

                if (node.CompletedAt is { } at)
                {
                    timedCompleted += estimate;
                    timedCount++;
                    first = first is { } f && f <= at ? f : at;
                    last = last is { } l && l >= at ? l : at;
                }
            }
            else
            {
                remaining += estimate;
            }
        }

        var perDay = defaultPerDay;
        var fromHistory = false;

        // 2 件以上の完了記録があれば、実際にかかった日数で割って本当のペースを使う。
        if (timedCount >= 2 && timedCompleted > 0 && first is { } start && last is { } end)
        {
            var span = Math.Max(1d, (end - start).TotalDays);
            perDay = Math.Clamp(timedCompleted / span, defaultPerDay * 0.25, defaultPerDay * 3);
            fromHistory = true;
        }

        var remainingDays = remaining <= 0 ? 0 : (int)Math.Ceiling(remaining / perDay);

        DateTimeOffset? finish = remaining <= 0
            ? null
            : new DateTimeOffset(today.Date.AddDays(remainingDays), today.Offset);

        var deadline = Deadline(graph);
        int? slack = finish is { } f2 && deadline is { } d
            ? (int)Math.Round((d.Date - f2.Date).TotalDays)
            : null;

        return new Forecast(remaining, completed, perDay, fromHistory, remainingDays, finish, deadline, slack);
    }

    /// <summary>プロジェクトの締切＝ゴールに付けられた期限のうち一番早いもの。</summary>
    private static DateTimeOffset? Deadline(TodoGraph graph)
    {
        DateTimeOffset? earliest = null;

        foreach (var node in graph.Nodes)
        {
            if (node.Kind != NodeKind.Goal || node.Due is not { } due || node.IsSettled)
            {
                continue;
            }

            earliest = earliest is { } current && current <= due ? current : due;
        }

        return earliest;
    }
}
