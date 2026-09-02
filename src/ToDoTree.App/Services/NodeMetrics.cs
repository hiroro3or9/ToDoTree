using ToDoTree.Core.Layout;

namespace ToDoTree.App.Services;

/// <summary>
/// いま選ばれている表示モード。<see cref="ThemeManager"/> と同じで、アプリにひとつだけ持つ。
///
/// カードの寸法はここを通して配られる。<see cref="ViewModels.NodeViewModel.CardWidth"/> が
/// この値を返すので、切り替えると線の接続点・矩形選択・全体表示・ミニマップ・
/// 新しいステップの置き場所が、まとめて追随する。
/// </summary>
public static class NodeMetrics
{
    public static NodeStyle Style { get; private set; } = NodeStyle.Card;

    /// <summary>切り替えるたびに増える通し番号。自前で描いている層のキャッシュ判定に使う。</summary>
    public static int Generation { get; private set; }

    public static bool IsMinimal => Style == NodeStyle.Minimal;

    public static double Width => NodeStyleMetrics.WidthOf(Style);

    public static double Height => NodeStyleMetrics.HeightOf(Style);

    /// <summary>表示モードが変わった。バインディング経由でない描画は、これを受けて描き直す。</summary>
    public static event EventHandler? StyleChanged;

    public static void Apply(NodeStyle style)
    {
        if (Style == style)
        {
            return;
        }

        Style = style;
        Generation++;
        StyleChanged?.Invoke(null, EventArgs.Empty);
    }

    public static NodeStyle Toggle()
    {
        Apply(Style == NodeStyle.Minimal ? NodeStyle.Card : NodeStyle.Minimal);
        return Style;
    }

    /// <summary>いまのモードと向きに合った、自動整列の設定。</summary>
    public static LayoutOptions LayoutFor(LayoutDirection direction) =>
        NodeStyleMetrics.LayoutFor(Style, direction);
}
