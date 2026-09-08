namespace ToDoTree.Core.Models;

/// <summary>ゴールまでの 1 ステップ。</summary>
public sealed class TodoNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public NodeKind Kind { get; set; } = NodeKind.Step;

    public NodeStatus Status { get; set; } = NodeStatus.NotStarted;

    /// <summary>期限（任意）。</summary>
    public DateTimeOffset? Due { get; set; }

    /// <summary>完了日時（Status が Done になったときに設定）。</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>見積もり（分）。クリティカルパスと重み付き進捗で使う。</summary>
    public int? EstimateMinutes { get; set; }

    /// <summary>
    /// 回数で完了する項目の目標・達成回数。null は従来の通常項目。
    /// 目標に達したときだけ <see cref="Status"/> が Done になる（取り消し中は別）。
    /// </summary>
    public RepeatProgress? Repeat { get; set; }

    public List<string> Tags { get; set; } = [];

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>true の間は自動レイアウトで動かさない（ユーザーが手で置いた位置を守る）。</summary>
    public bool IsPinned { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>完了扱い（取り消しも「もう待たなくてよい」ので完了側に含める）。</summary>
    public bool IsSettled => Status is NodeStatus.Done or NodeStatus.Cancelled;

    public bool IsOverdue =>
        Due is { } due && Status is not (NodeStatus.Done or NodeStatus.Cancelled) && due < DateTimeOffset.Now;

    /// <summary>回数で完了する項目。</summary>
    public bool IsRepeating => Repeat is not null;

    public TodoNode Clone()
    {
        var copy = (TodoNode)MemberwiseClone();
        copy.Tags = [.. Tags];

        // MemberwiseClone は参照をそのまま写す。回数を共有したままだと、
        // 履歴・部品・プロジェクト複製の片方で加算したぶんが元にも乗ってしまう。
        copy.Repeat = Repeat?.Clone();
        return copy;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? "(無題)" : Title;
}
