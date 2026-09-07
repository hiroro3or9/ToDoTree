using System.Windows;
using System.Windows.Media;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Services;

/// <summary>
/// ブロックと線に付けた個別色（ColorId）を、いま適用中のテーマのブラシへ解決する。
///
/// キーは「既定のキー + . + id」で揃えてある（例: Block.Fill.red）。
/// 引けなければ既定のキーへ落ちるので、知らない色・キーの入れ忘れ・
/// 片側のテーマだけの定義漏れが、すべて「既定色で描く」に収束する。落ちない。
///
/// 選択中の濃い背景と、先行が片付いた線の薄い色はパレットに持たず、
/// ここで不透明度から作る。色ごとに 4 キーで済ませ、増やさないため。
/// 作ったブラシはテーマの世代ごとに覚えておく（描くたびに作らない）。
/// </summary>
public static class ColorPalette
{
    private static readonly Dictionary<string, Brush> Cache = [];
    private static int _generation = -1;

    /// <summary>描画に使う色 id。知らない色は既定色として扱う（モデルの値はそのまま残す）。</summary>
    public static string? Effective(string? colorId) =>
        ColorPresets.IsKnown(colorId) ? colorId : null;

    /// <summary>色付きのキーを引き、無ければ既定のキーへ落ちる。</summary>
    public static Brush BrushOf(string baseKey, string? colorId)
    {
        if (Effective(colorId) is not { } id)
        {
            return ThemeManager.BrushOf(baseKey);
        }

        return Application.Current?.TryFindResource($"{baseKey}.{id}") as Brush
               ?? ThemeManager.BrushOf(baseKey);
    }

    /// <summary>同じ色を濃くする。選択中のブロックの背景に使う。</summary>
    public static Brush Emphasize(string baseKey, string? colorId, double factor) =>
        WithAlpha(baseKey, colorId, factor, "emphasize");

    /// <summary>同じ色を薄くする。先行が片付いた線に使う。</summary>
    public static Brush Fade(string baseKey, string? colorId, double factor) =>
        WithAlpha(baseKey, colorId, factor, "fade");

    private static Brush WithAlpha(string baseKey, string? colorId, double factor, string tag)
    {
        Sweep();

        var key = $"{tag}:{factor:0.###}:{baseKey}.{Effective(colorId) ?? string.Empty}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var source = BrushOf(baseKey, colorId);
        var result = source;
        if (source is SolidColorBrush solid)
        {
            var color = solid.Color;
            var alpha = (byte)Math.Clamp(Math.Round(color.A * factor), 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            result = brush;
        }

        Cache[key] = result;
        return result;
    }

    /// <summary>配色が入れ替わったら、作り置きを捨てる。</summary>
    private static void Sweep()
    {
        if (_generation == ThemeManager.Generation)
        {
            return;
        }

        _generation = ThemeManager.Generation;
        Cache.Clear();
    }
}
