namespace ToDoTree.Core.Models;

/// <summary>ノードの種別。</summary>
public enum NodeKind
{
    /// <summary>出発点。</summary>
    Start,

    /// <summary>通常のステップ。</summary>
    Step,

    /// <summary>節目（中間目標）。</summary>
    Milestone,

    /// <summary>ゴール。</summary>
    Goal,
}

/// <summary>ユーザーが直接指定する状態。</summary>
public enum NodeStatus
{
    NotStarted,
    InProgress,
    Done,
    Cancelled,
}

/// <summary>グラフから導出される「いま着手できるか」の状態。保存はしない。</summary>
public enum Readiness
{
    /// <summary>先行がすべて片付いていて、いま着手できる。</summary>
    Ready,

    /// <summary>未完了の先行があるので待ち。</summary>
    Blocked,

    /// <summary>進行中。</summary>
    InProgress,

    /// <summary>完了済み。</summary>
    Done,

    /// <summary>取り消し済み。</summary>
    Cancelled,
}
