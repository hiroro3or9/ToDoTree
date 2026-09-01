namespace ToDoTree.Core.Graph;

/// <summary>プロジェクト全体の進み具合。</summary>
public sealed record ProgressSummary(
    int Total,
    int Done,
    int InProgress,
    int Ready,
    int Blocked,
    int Cancelled,
    int Overdue,
    int EstimatedTotalMinutes,
    int EstimatedDoneMinutes)
{
    public static readonly ProgressSummary Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>件数ベースの進捗（％）。取り消し分は母数から外す。</summary>
    public double Percent => Total == 0 ? 0d : Done * 100d / Total;

    /// <summary>見積もり時間で重み付けした進捗（％）。</summary>
    public double WeightedPercent =>
        EstimatedTotalMinutes == 0 ? Percent : EstimatedDoneMinutes * 100d / EstimatedTotalMinutes;

    public int Remaining => Total - Done;
}
