namespace ToDoTree.Core.Models;

/// <summary>保存単位。ノードと辺の入れ物。</summary>
public sealed class TodoProject
{
    /// <summary>
    /// 2 でブロック（<see cref="Blocks"/>）が加わった。
    /// 3 で作業のしおり（<see cref="Bookmark"/>）が加わった。
    /// 4 でブロックの折りたたみと、ブロックを端点にした接続が加わった。
    /// 5 でブロックと線の個別色（ColorId）が加わった。
    /// 6 で回数で完了する項目（<see cref="TodoNode.Repeat"/>）が加わった。
    /// 旧アプリは新しい形式を読み込み時に拒否するので、上げたぶんだけ古い版での上書きを防げる。
    /// </summary>
    public const int CurrentSchemaVersion = 6;

    private List<TodoNode> _nodes = [];
    private List<TodoEdge> _edges = [];
    private List<TodoBlock> _blocks = [];

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新しいプロジェクト";

    public string Description { get; set; } = string.Empty;

    /// <summary>次に再開するステップと、その時点のメモ。形式3で追加。</summary>
    public WorkBookmark? Bookmark { get; set; }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<TodoNode> Nodes { get => _nodes; set => _nodes = value ?? []; }

    public List<TodoEdge> Edges { get => _edges; set => _edges = value ?? []; }

    /// <summary>
    /// ステップをまとめた囲み。旧形式には無いので、読み込み時は空のまま扱う。
    /// 位置・幅・高さは持たない（所属ノードの座標から毎回計算する派生値）。
    /// </summary>
    public List<TodoBlock> Blocks { get => _blocks; set => _blocks = value ?? []; }

    public TodoProject DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Bookmark = Bookmark?.Clone(),
        SchemaVersion = SchemaVersion,
        Nodes = [.. Nodes.Select(n => n.Clone())],
        Edges = [.. Edges.Select(e => e.Clone())],
        Blocks = [.. Blocks.Select(b => b.Clone())],
    };
}
