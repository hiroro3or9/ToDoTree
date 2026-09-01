using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>列挙値の日本語表示。</summary>
public static class Labels
{
    public static string Of(NodeStatus status) => status switch
    {
        NodeStatus.NotStarted => "未着手",
        NodeStatus.InProgress => "進行中",
        NodeStatus.Done => "完了",
        NodeStatus.Cancelled => "取り消し",
        _ => status.ToString(),
    };

    public static string Of(NodeKind kind) => kind switch
    {
        NodeKind.Start => "スタート",
        NodeKind.Step => "ステップ",
        NodeKind.Milestone => "節目",
        NodeKind.Goal => "ゴール",
        _ => kind.ToString(),
    };

    public static string Of(Readiness readiness) => readiness switch
    {
        Readiness.Ready => "着手できる",
        Readiness.InProgress => "進行中",
        Readiness.Blocked => "待ち",
        Readiness.Done => "完了",
        Readiness.Cancelled => "取り消し",
        _ => readiness.ToString(),
    };
}
