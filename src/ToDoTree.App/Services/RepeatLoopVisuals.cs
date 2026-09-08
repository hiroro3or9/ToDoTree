using System.Windows;
using System.Windows.Media;
using ToDoTree.Core.Layout;

namespace ToDoTree.App.Services;

/// <summary>カード上辺から出て同じカードへ戻る自己ループ。接続用のカード寸法とは分ける。</summary>
public static class RepeatLoopVisuals
{
    public const double Height = 56;
    public static double Width => NodeMetrics.IsMinimal ? 112 : NodeMetrics.Width;
    public static Thickness Margin => new((NodeMetrics.Width - Width) / 2, -Height, (NodeMetrics.Width - Width) / 2, 0);

    private static readonly Geometry CardLoop = Frozen("M 174,56 C 174,22 168,12 142,12 L 82,12 C 56,12 50,22 50,56");
    private static readonly Geometry CardArrow = Frozen("M 46,48 L 50,55 L 54,48");
    private static readonly Geometry MinimalLoop = Frozen("M 62,56 C 86,42 91,12 72,12 L 40,12 C 21,12 26,42 50,56");
    private static readonly Geometry MinimalArrow = Frozen("M 42,54 L 50,56 L 47,48");

    public static Geometry Path => NodeMetrics.IsMinimal ? MinimalLoop : CardLoop;
    public static Geometry Arrow => NodeMetrics.IsMinimal ? MinimalArrow : CardArrow;

    public static Rect Bounds(double x, double y, bool repeating) => repeating
        ? new Rect(x + (NodeMetrics.Width - Width) / 2, y - Height, Width, NodeMetrics.Height + Height)
        : new Rect(x, y, NodeMetrics.Width, NodeMetrics.Height);

    public static LayoutOptions LayoutFor(LayoutDirection direction, bool hasRepeats)
    {
        var options = NodeMetrics.LayoutFor(direction);
        if (!hasRepeats) return options;
        // 次の列・行のループが、手前のカードに重ならない余白を取る。
        var extraWidth = Width - NodeMetrics.Width;
        options.LayerSpacing += direction == LayoutDirection.LeftToRight ? extraWidth : Height;
        options.NodeSpacing += direction == LayoutDirection.LeftToRight ? Height : extraWidth;
        return options;
    }

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
