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
    public double LayerSpacing { get; set; } = 260;

    /// <summary>同じレイヤ内のステップ同士の距離。</summary>
    public double NodeSpacing { get; set; } = 110;

    public double OriginX { get; set; } = 80;

    public double OriginY { get; set; } = 80;

    /// <summary>ピン留めされたノードの座標を維持する。</summary>
    public bool RespectPinned { get; set; } = true;

    /// <summary>交差を減らすためのスイープ回数。</summary>
    public int CrossingSweeps { get; set; } = 8;
}
