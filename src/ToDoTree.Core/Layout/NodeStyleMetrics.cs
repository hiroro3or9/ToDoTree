namespace ToDoTree.Core.Layout;

/// <summary>ステップを画面にどう出すか。</summary>
public enum NodeStyle
{
    /// <summary>状態・種別・期限・見積り・タグまで載せたカード。</summary>
    Card,

    /// <summary>丸ひとつと名前だけ。</summary>
    Minimal,
}

/// <summary>
/// 表示モードごとの箱の大きさと、自動整列の間隔。
///
/// 間隔は「箱の大きさ ＋ 余白」で決める。流れる向きが変わると、レイヤ間・同レイヤ内の
/// どちらに幅が効くかが入れ替わるので、向きも受け取って計算する。
/// 数値を直接書くと、上→下に切り替えたときに箱同士が重なる（以前がそうだった）。
/// </summary>
public static class NodeStyleMetrics
{
    public static double WidthOf(NodeStyle style) => style == NodeStyle.Minimal ? 200 : 224;

    public static double HeightOf(NodeStyle style) => style == NodeStyle.Minimal ? 28 : 88;

    /// <summary>流れる向きの余白。ここに線が引かれるので、矢印が見えるだけの幅を空ける。</summary>
    public static double FlowGapOf(NodeStyle style) => style == NodeStyle.Minimal ? 40 : 66;

    /// <summary>流れと直交する向きの余白。詰まって見えない程度に空ける。</summary>
    public static double CrossGapOf(NodeStyle style) => style == NodeStyle.Minimal ? 20 : 40;

    /// <summary>レイヤ（世代）が進む向きの間隔。</summary>
    public static double LayerSpacingOf(NodeStyle style, LayoutDirection direction) =>
        (direction == LayoutDirection.LeftToRight ? WidthOf(style) : HeightOf(style)) + FlowGapOf(style);

    /// <summary>同じレイヤに並ぶステップ同士の間隔。</summary>
    public static double NodeSpacingOf(NodeStyle style, LayoutDirection direction) =>
        (direction == LayoutDirection.LeftToRight ? HeightOf(style) : WidthOf(style)) + CrossGapOf(style);

    /// <summary>いまの表示モードと向きに合った、自動整列の設定を作る。</summary>
    public static LayoutOptions LayoutFor(NodeStyle style, LayoutDirection direction) => new()
    {
        Direction = direction,
        LayerSpacing = LayerSpacingOf(style, direction),
        NodeSpacing = NodeSpacingOf(style, direction),
    };
}
