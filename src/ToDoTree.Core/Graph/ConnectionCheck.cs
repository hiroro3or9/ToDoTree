namespace ToDoTree.Core.Graph;

/// <summary>辺を張れるかどうかの判定結果。</summary>
public enum ConnectionCheck
{
    Ok,

    /// <summary>同じノード同士。</summary>
    SameNode,

    /// <summary>ノードが見つからない。</summary>
    NodeNotFound,

    /// <summary>すでに繋がっている。</summary>
    Duplicate,

    /// <summary>繋ぐと循環する（DAG が壊れる）。</summary>
    WouldCreateCycle,
}

public static class ConnectionCheckExtensions
{
    public static bool IsOk(this ConnectionCheck check) => check == ConnectionCheck.Ok;

    public static string ToMessage(this ConnectionCheck check) => check switch
    {
        ConnectionCheck.Ok => "接続できます。",
        ConnectionCheck.SameNode => "同じステップ同士は繋げません。",
        ConnectionCheck.NodeNotFound => "ステップが見つかりません。",
        ConnectionCheck.Duplicate => "すでに繋がっています。",
        ConnectionCheck.WouldCreateCycle => "循環してしまうため繋げません（ゴールに辿り着けなくなります）。",
        _ => "接続できません。",
    };
}
