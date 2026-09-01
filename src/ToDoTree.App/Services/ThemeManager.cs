using System.Windows;
using System.Windows.Media;

namespace ToDoTree.App.Services;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// 配色の入れ替え。Application.Resources のマージ辞書の先頭を、
/// Palette.Light / Palette.Dark ごと差し替えるだけ。
/// 画面側は色をすべて DynamicResource で引いているので、それだけで追随する。
/// </summary>
public static class ThemeManager
{
    /// <summary>マージ辞書のうち、配色が入っている位置（App.xaml の並び順と揃える）。</summary>
    private const int PaletteSlot = 0;

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>色を入れ替えるたびに増える通し番号。自前で描いている層のキャッシュ判定に使う。</summary>
    public static int Generation { get; private set; }

    public static bool IsDark => Current == AppTheme.Dark;

    /// <summary>色が入れ替わった。バインディング経由でない描画は、これを受けて描き直す。</summary>
    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var name = theme == AppTheme.Dark ? "Dark" : "Light";
        var palette = new ResourceDictionary
        {
            Source = new Uri($"Themes/Palette.{name}.xaml", UriKind.Relative),
        };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > PaletteSlot)
        {
            merged[PaletteSlot] = palette;
        }
        else
        {
            merged.Insert(PaletteSlot, palette);
        }

        Current = theme;
        Generation++;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static AppTheme Toggle()
    {
        Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        return Current;
    }

    /// <summary>いまのテーマから色を 1 つ取り出す。見つからなければ透明。</summary>
    public static Brush BrushOf(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
}
