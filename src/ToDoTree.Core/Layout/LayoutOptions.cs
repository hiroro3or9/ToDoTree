namespace ToDoTree.Core.Layout;

public enum LayoutDirection
{
    /// <summary>左から右へ流れる（Git のコミットグラフに近い見え方）。</summary>
    LeftToRight,

    /// <summary>上から下へ流れる。</summary>
    TopToBottom,
}

public sealed class LayoutOptions
{
    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;

    /// <summary>レイヤ（世代）間の距離。</summary>
    public double LayerSpacing { get; set; } = 290;

    /// <summary>同じレイヤ内のステップ同士の距離。</summary>
    public double NodeSpacing { get; set; } = 128;

    public double OriginX { get; set; } = 80;

    public double OriginY { get; set; } = 80;

    /// <summary>ピン留めされたノードの座標を維持する。</summary>
    public bool RespectPinned { get; set; } = true;

    /// <summary>交差を減らすためのスイープ回数。</summary>
    public int CrossingSweeps { get; set; } = 8;

    /// <summary>箱の大きさ。囲みを避けるときの当たり判定に使う。</summary>
    public double NodeWidth { get; set; } = 224;

    public double NodeHeight { get; set; } = 88;

    /// <summary>
    /// この整列では動かさないノード。ブロック所属ノードを一時的に固定するために使う。
    /// モデルの <c>IsPinned</c> は書き換えない（ユーザーが手で留めた印と混ざらないように）。
    /// </summary>
    public IReadOnlySet<Guid> FixedIds { get; set; } = new HashSet<Guid>();

    /// <summary>置かないでほしい領域。囲みの占有領域を渡す。</summary>
    public IReadOnlyList<BlockBounds> Obstacles { get; set; } = [];

    public bool IsFixed(Guid id) => FixedIds.Contains(id);
}
