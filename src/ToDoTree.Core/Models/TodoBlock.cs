namespace ToDoTree.Core.Models;

/// <summary>
/// 複数のステップを名前付きの囲みとしてまとめたもの。整理と移動の単位であって、タスクではない。
///
/// ブロックへの接続は全所属ステップの依存関係へ展開して計算する。
/// 所属の正本はこの <see cref="NodeIds"/> だけで、ノード側には所属を持たせない
/// （両方に持たせると、片方だけ更新される道が必ず生まれるため）。
/// </summary>
public sealed class TodoBlock
{
    /// <summary>名前を空にしたときに戻す既定名。</summary>
    public const string DefaultTitle = "新しいブロック";

    private List<Guid> _nodeIds = [];
    private List<BlockPort> _ports = [];

    public List<BlockPort> Ports { get => _ports; set => _ports = value ?? []; }

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>直接の親ブロック。null は最上位を表す。</summary>
    public Guid? ParentBlockId { get; set; }

    public string Title { get; set; } = DefaultTitle;

    public bool IsCollapsed { get; set; }

    /// <summary>
    /// 囲みの色。null は既定色。
    /// 値は <see cref="ColorPresets"/> の id で、色そのものは App 側のパレットにある。
    /// 見分けのための飾りであって、依存関係にも進捗にも関わらない。
    /// </summary>
    public string? ColorId { get; set; }

    /// <summary>直接所属するステップ。子ブロック内のステップは重複して持たない。</summary>
    public List<Guid> NodeIds { get => _nodeIds; set => _nodeIds = value ?? []; }

    public TodoBlock Clone()
    {
        var copy = (TodoBlock)MemberwiseClone();
        copy.NodeIds = [.. NodeIds];
        copy.Ports = [.. Ports];
        return copy;
    }

    public override string ToString() => $"{Title}（{NodeIds.Count} 件）";
}
