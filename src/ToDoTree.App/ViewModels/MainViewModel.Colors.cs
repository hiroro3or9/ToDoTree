using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// ブロックと線に 1 つずつ色を付ける。
///
/// 色は見分けるための飾りであって、依存関係・着手可能判定・進捗・最長経路・完了予測には関わらない。
/// 状態色（選択・最長経路・強調）が付いているあいだは、そちらを優先して描く。
/// </summary>
public sealed partial class MainViewModel
{
    private IReadOnlyList<ColorChoiceViewModel>? _blockColorChoices;
    private IReadOnlyList<ColorChoiceViewModel>? _edgeColorChoices;

    public IReadOnlyList<ColorChoiceViewModel> BlockColorChoices =>
        _blockColorChoices ??= BuildColorChoices(forEdge: false);

    public IReadOnlyList<ColorChoiceViewModel> EdgeColorChoices =>
        _edgeColorChoices ??= BuildColorChoices(forEdge: true);

    /// <summary>選んでいるブロックの色を変える。同じ色を選び直したときは何もしない。</summary>
    public void ApplyBlockColor(string? colorId)
    {
        if (SelectedBlock is not { } block)
        {
            return;
        }

        if (string.Equals(block.Model.ColorId, colorId, StringComparison.Ordinal))
        {
            return;
        }

        // 名前を書き換えている途中なら、先に確定させてから色の履歴を積む。
        CommitPendingBlockEdit();

        // 色選びは 1 回ごとに独立した操作なので、キーを渡して短時間の連続をまとめたりしない。
        // 赤→青→緑と試したあとの Ctrl+Z は、青へ戻ってほしい。
        PushUndo();
        block.Model.ColorId = colorId;
        MarkDirty();

        block.RefreshBrushes();
        RefreshColorChoices();
        NotifyVisualsChanged();

        StatusMessage = colorId is null
            ? $"「{block.Title}」の色を既定に戻しました。"
            : $"「{block.Title}」を{NameOf(colorId)}にしました。";
    }

    /// <summary>
    /// 畳んだブロックへ集約された線は色を選べない。
    /// 同じ経路に複数本が重なって描かれるので、1 本だけ塗っても隣に上書きされて見えないため。
    /// </summary>
    public bool CanColorSelectedEdge => SelectedEdge is { IsAggregated: false };

    public string EdgeColorHint => SelectedEdge is { IsAggregated: true }
        ? "畳んだブロックへまとめた線です。ブロックを開いてから色を選べます。"
        : "この線の色を選びます";

    /// <summary>選んでいる線の色を変える。線には名前が無いので、相手は呼ばない。</summary>
    public void ApplyEdgeColor(string? colorId)
    {
        if (SelectedEdge is not { } edge)
        {
            return;
        }

        if (edge.IsAggregated)
        {
            StatusMessage = EdgeColorHint;
            return;
        }

        if (string.Equals(edge.Model.ColorId, colorId, StringComparison.Ordinal))
        {
            return;
        }

        PushUndo();
        edge.Model.ColorId = colorId;
        MarkDirty();

        RefreshColorChoices();
        NotifyVisualsChanged();

        StatusMessage = colorId is null
            ? "線の色を既定に戻しました。"
            : $"線の色を{NameOf(colorId)}にしました。";
    }

    /// <summary>配色の切り替えや選択の変更のあとに、色見本と印を引き直す。</summary>
    public void RefreshColorChoices()
    {
        Refresh(_blockColorChoices);
        Refresh(_edgeColorChoices);

        static void Refresh(IReadOnlyList<ColorChoiceViewModel>? choices)
        {
            if (choices is null)
            {
                return;
            }

            foreach (var choice in choices)
            {
                choice.Refresh();
            }
        }
    }

    private IReadOnlyList<ColorChoiceViewModel> BuildColorChoices(bool forEdge)
    {
        var choices = new List<ColorChoiceViewModel>(ColorPresets.All.Count + 1)
        {
            new(null, forEdge, this),
        };

        choices.AddRange(ColorPresets.All.Select(preset => new ColorChoiceViewModel(preset, forEdge, this)));
        return choices;
    }

    /// <summary>状態メッセージ用の呼び名。知らない色は名前を持たない。</summary>
    private static string NameOf(string colorId) =>
        ColorPresets.DisplayNameOf(colorId) ?? "指定の色";
}
