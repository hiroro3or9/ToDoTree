namespace ToDoTree.Core.Models;

/// <summary>
/// 個別色の 1 つ。色の値（RGB）はここに持たない。
/// 実体は App 側の Themes/Palette.Light.xaml / Palette.Dark.xaml にある。
/// </summary>
public sealed record ColorPreset(string Id, string DisplayName);

/// <summary>
/// ブロックと線に付けられる色の一覧。
///
/// <see cref="ColorPreset.Id"/> はリソースキーの末尾に足して使うので、英小文字だけにする
/// （例: Block.Fill + "." + "red" → Block.Fill.red）。
/// 表示名は選ぶときの目安であり、アプリ側で意味は持たせない。
/// </summary>
public static class ColorPresets
{
    public static IReadOnlyList<ColorPreset> All { get; } =
    [
        new("slate", "灰"),
        new("red", "赤"),
        new("orange", "橙"),
        new("amber", "黄"),
        new("green", "緑"),
        new("teal", "青緑"),
        new("blue", "青"),
        new("violet", "紫"),
        new("pink", "桃"),
    ];

    private static readonly HashSet<string> KnownIds = [.. All.Select(p => p.Id)];

    /// <summary>用意してある色か。未知の id は既定色として描くだけで、値は捨てない。</summary>
    public static bool IsKnown(string? id) => id is not null && KnownIds.Contains(id);

    /// <summary>状態メッセージに使う表示名。未知の id では null。</summary>
    public static string? DisplayNameOf(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id)?.DisplayName;
}
