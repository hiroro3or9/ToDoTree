namespace ToDoTree.Core.Models;

/// <summary>
/// 複数のステップを名前付きの囲みとしてまとめたもの。整理と移動の単位であって、タスクではない。
///
/// 依存関係・着手可能判定・進捗・最長経路・完了予測には一切関わらない。
/// 所属の正本はこの <see cref="NodeIds"/> だけで、ノード側には所属を持たせない
/// （両方に持たせると、片方だけ更新される道が必ず生まれるため）。
/// </summary>
public sealed class TodoBlock
{
    /// <summary>名前を空にしたときに戻す既定名。</summary>
    public const string DefaultTitle = "新しいブロック";

    private List<Guid> _nodeIds = [];

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = DefaultTitle;

    /// <summary>所属するステップ。並び順は表示に使わない（境界は座標から計算する）。</summary>
    public List<Guid> NodeIds { get => _nodeIds; set => _nodeIds = value ?? []; }

    public TodoBlock Clone()
    {
        var copy = (TodoBlock)MemberwiseClone();
        copy.NodeIds = [.. NodeIds];
        return copy;
    }

    public override string ToString() => $"{Title}（{NodeIds.Count} 件）";
}
