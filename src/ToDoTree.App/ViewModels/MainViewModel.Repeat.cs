using System.Windows;
using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// 回数で完了する項目の操作。
///
/// 回数・状態・完了日時は必ず <see cref="RepeatService"/> でまとめて動かし、
/// ここは履歴・再描画・案内・完了演出の受け持ちに徹する。
/// 何も変わらなかった操作では履歴を残さない（Ctrl+Z が空振りしないように）。
/// </summary>
public sealed partial class MainViewModel
{
    private ICommand? _advanceRepeatCommand;
    private ICommand? _stepBackRepeatCommand;
    private ICommand? _configureRepeatCommand;
    private ICommand? _adjustRepeatCommand;
    private ICommand? _clearRepeatCommand;
    private ICommand? _beginRepeatCommand;
    private ICommand? _toggleRepeatCancelCommand;

    /// <summary>設定画面を開いてほしい。第2引数が true なら「新しく設定する」。</summary>
    public event Action<NodeViewModel, bool>? RepeatSettingsRequested;

    // ---- コマンド ----

    /// <summary>1回達成する。</summary>
    public ICommand AdvanceRepeatCommand => _advanceRepeatCommand ??= new RelayCommand(
        () => AdvanceRepeat(SelectedNode), () => RepeatService.CanAdvance(SelectedNode?.Model));

    /// <summary>1回戻す。誤操作を減らすため、カードには常設しない。</summary>
    public ICommand StepBackRepeatCommand => _stepBackRepeatCommand ??= new RelayCommand(
        () => StepBackRepeat(SelectedNode), () => RepeatService.CanStepBack(SelectedNode?.Model));

    /// <summary>繰り返しを設定する / 編集する。</summary>
    public ICommand ConfigureRepeatCommand => _configureRepeatCommand ??= new RelayCommand(
        () => RequestRepeatSettings(SelectedNode), () => SelectedNode is not null);

    /// <summary>回数を訂正する。入口は違うが、開く画面は設定と同じ。</summary>
    public ICommand AdjustRepeatCommand => _adjustRepeatCommand ??= new RelayCommand(
        () => RequestRepeatSettings(SelectedNode), () => SelectedNode?.Model.Repeat is not null);

    /// <summary>繰り返しを解除する。</summary>
    public ICommand ClearRepeatCommand => _clearRepeatCommand ??= new RelayCommand(
        () => ClearRepeat(SelectedNode), () => SelectedNode?.Model.Repeat is not null);

    /// <summary>着手する。回数は増やさない。</summary>
    public ICommand BeginRepeatCommand => _beginRepeatCommand ??= new RelayCommand(
        () => BeginRepeat(SelectedNode),
        () => SelectedNode?.Model is { Repeat: not null, Status: NodeStatus.NotStarted });

    /// <summary>取り消す / 再開する。</summary>
    public ICommand ToggleRepeatCancelCommand => _toggleRepeatCancelCommand ??= new RelayCommand(
        () => ToggleRepeatCancel(SelectedNode), () => SelectedNode?.Model.Repeat is not null);

    // ---- メニューの文言 ----

    /// <summary>設定済みなら「編集」。同じ入口を、状態に合わせて言い換える。</summary>
    public string RepeatConfigureLabel =>
        SelectedNode?.Model.Repeat is not null ? "繰り返しを編集…" : "繰り返しを設定…";

    public string RepeatCancelLabel =>
        SelectedNode?.Model.Status == NodeStatus.Cancelled ? "再開する" : "取り消す";

    /// <summary>選択に回数つきの項目が入っている。</summary>
    public bool HasRepeatInSelection => SelectedNodes.Any(n => n.Model.Repeat is not null);

    public bool IsSelectedRepeating => SelectedNode?.Model.Repeat is not null;

    /// <summary>
    /// Space と右クリックの「完了」に出す文言。混在選択では何が起きるかを明記する。
    /// </summary>
    public string ToggleDoneLabel
    {
        get
        {
            var targets = SelectedNodes;
            var repeats = targets.Count(n => n.Model.Repeat is not null);
            if (repeats == 0) return "完了 / 未着手";
            return repeats == targets.Count
                ? targets.Count == 1 ? "1回達成" : "まとめて1回達成"
                : "通常項目を完了・繰り返しを1回達成";
        }
    }

    public string ToggleDoneHint
    {
        get
        {
            var targets = SelectedNodes;
            var repeats = targets.Count(n => n.Model.Repeat is not null);
            if (repeats == 0) return "選択したステップを完了にします（すべて完了済みなら未着手に戻します）。";
            return repeats == targets.Count
                ? "回数で完了する項目です。最後の1回でだけ完了になります。"
                : "通常の未完了項目を完了にし、繰り返しの未完了項目を1回ずつ加算します。完了済みは変わりません。";
        }
    }

    /// <summary>回数まわりのメニューの可否と文言を、まとめて出し直す。</summary>
    internal void NotifyRepeatCommandStates() => OnPropertyChanged(
        nameof(RepeatConfigureLabel),
        nameof(RepeatCancelLabel),
        nameof(HasRepeatInSelection),
        nameof(IsSelectedRepeating),
        nameof(ToggleDoneLabel),
        nameof(ToggleDoneHint));

    // ---- 単独の操作 ----

    public void AdvanceRepeat(NodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Model.Repeat is null) return;

        // 完了予告はいまのグラフで計算する。加算したあとでは先行の状態が変わっている。
        var impact = RepeatService.IsFinalNext(node.Model)
            ? CompletionImpact.Calculate(_graph, [node.Id]) : null;

        if (!RunRepeat(node, RepeatService.Advance, out var result)) return;

        if (result.BecameDone)
        {
            StatusMessage = $"{result.After.Completed} / {result.After.Target} 回。最後の1回を達成して完了しました。";
            if (impact is not null) PlayCompletion(impact);
            AnnounceUnlocked(node);
            return;
        }

        StatusMessage = $"{result.After.Completed} / {result.After.Target} 回達成しました。";
    }

    public void StepBackRepeat(NodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Model.Repeat is null) return;
        if (!RunRepeat(node, RepeatService.StepBack, out var result)) return;

        StatusMessage = result.LeftDone
            ? $"{result.After.Completed} / {result.After.Target} 回に戻しました。完了を取り消したので、後続がまた待ちになります。"
            : $"{result.After.Completed} / {result.After.Target} 回に戻しました。";
    }

    public void BeginRepeat(NodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Model.Repeat is null) return;
        if (!RunRepeat(node, RepeatService.Begin, out _)) return;
        StatusMessage = "着手しました。回数は増えていません。";
    }

    public void ToggleRepeatCancel(NodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node?.Model.Repeat is null) return;

        var resuming = node.Model.Status == NodeStatus.Cancelled;
        var impact = resuming && node.Model.Repeat.IsFull
            ? CompletionImpact.Calculate(_graph, [node.Id]) : null;

        Func<TodoNode, DateTimeOffset, RepeatResult> operation = resuming ? RepeatService.Resume : RepeatService.Cancel;
        if (!RunRepeat(node, operation, out var result)) return;

        if (result.BecameDone && impact is not null) PlayCompletion(impact);
        StatusMessage = resuming
            ? $"再開しました。{result.After.Completed} / {result.After.Target} 回・{Labels.Of(result.After.Status)}。"
            : $"取り消しました。{result.After.Completed} / {result.After.Target} 回は残しています。";
    }

    /// <summary>
    /// 繰り返しを解除する。回数の記録は消えるので、消える前に何が起きるかを見せる。
    /// 設定画面から呼ぶときは、画面上ですでに結果を示しているので確認しない。
    /// </summary>
    public void ClearRepeat(NodeViewModel? node, bool confirm = true)
    {
        node ??= SelectedNode;
        if (node?.Model.Repeat is null) return;

        var before = RepeatState.Of(node.Model);
        if (confirm)
        {
            var answer = MessageBox.Show(
                $"「{node.Title}」の繰り返しを解除します。\n\n"
                + $"回数の記録（{before.Completed} / {before.Target} 回）が消え、"
                + $"{Labels.Of(before.Status)}のまま通常のステップに戻ります。\n"
                + "解除したあとでも Ctrl+Z で元に戻せます。",
                "ToDoTree",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK)
            {
                StatusMessage = "解除をやめました。";
                return;
            }
        }

        if (!RunRepeat(node, RepeatService.Clear, out _)) return;
        StatusMessage = $"繰り返しを解除しました（{before.Completed} / {before.Target} 回の記録は消えます）。"
            + $"状態は{Labels.Of(node.Model.Status)}のままです。";
    }

    // ---- 設定画面 ----

    /// <summary>設定画面の初期値。まだ設定していない完了済み項目は、完了のまま扱えるようにする。</summary>
    public (int Target, int Completed) RepeatSeed(NodeViewModel node)
    {
        if (node.Model.Repeat is { } repeat) return (repeat.TargetCount, repeat.CompletedCount);
        return node.Model.Status == NodeStatus.Done
            ? (RepeatProgress.DefaultTarget, RepeatProgress.DefaultTarget)
            : (RepeatProgress.DefaultTarget, 0);
    }

    /// <summary>設定画面を開く。自分自身への接続からもここへ来る。</summary>
    public void RequestRepeatSettings(NodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null) return;

        if (SelectedNode != node) SelectedNode = node;
        RepeatSettingsRequested?.Invoke(node, node.Model.Repeat is null);
    }

    /// <summary>
    /// 自分自身へ戻した接続を、繰り返しの設定要求として扱う。
    /// 依存線は作らないので、Core の循環拒否はそのまま生きている。
    /// </summary>
    public void RequestRepeatFromSelfConnection(Guid id)
    {
        // ブロック全体の周回は初版では扱わない。案内だけ出して何も変えない。
        if (Blocks.Any(b => b.Id == id))
        {
            StatusMessage = "ブロック全体の繰り返しは未対応です。中のステップには設定できます。";
            return;
        }

        if (_byId.TryGetValue(id, out var node)) RequestRepeatSettings(node);
    }

    /// <summary>設定画面の「適用」。目標と達成回数は同じ操作でまとめて反映する。</summary>
    public void ApplyRepeat(NodeViewModel node, int target, int completed)
    {
        ArgumentNullException.ThrowIfNull(node);

        var willComplete = RepeatService.PreviewStatus(node.Model.Status, target, completed) == NodeStatus.Done
            && node.Model.Status != NodeStatus.Done;
        var impact = willComplete ? CompletionImpact.Calculate(_graph, [node.Id]) : null;

        if (!RunRepeat(node, (model, now) => RepeatService.Configure(model, target, completed, now), out var result))
        {
            return;
        }

        // 訂正・目標変更では達成演出を再生しない。新しく完了になったときだけ後続を知らせる。
        if (result.BecameDone && impact is not null) PlayCompletion(impact);

        StatusMessage = result.Before.IsRepeating
            ? $"{result.After.Completed} / {result.After.Target} 回・{Labels.Of(result.After.Status)}に直しました。"
            : $"繰り返しを設定しました。{result.After.Completed} / {result.After.Target} 回・{Labels.Of(result.After.Status)}。";
    }

    // ---- 複数選択 ----

    /// <summary>
    /// 通常の未完了項目を完了にし、繰り返しの未完了項目を1回ずつ加算する。
    /// 完了済みは変えず、取り消し中の繰り返しは対象にしない。全件完了でも巻き戻さない。
    /// 全体で Undo 1回。
    /// </summary>
    public void AdvanceSelection(IReadOnlyList<NodeViewModel> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var plain = targets.Where(n => n.Model.Repeat is null && n.Model.Status != NodeStatus.Done).ToList();
        var repeats = targets.Where(n => RepeatService.CanAdvance(n.Model)).ToList();

        if (plain.Count == 0 && repeats.Count == 0)
        {
            StatusMessage = "変更するステップがありません（完了済み、または取り消し中です）。";
            return;
        }

        // 完了予告に渡すのは、今回ほんとうに完了へ移るものだけ。
        var completing = plain.Select(n => n.Id)
            .Concat(repeats.Where(n => RepeatService.IsFinalNext(n.Model)).Select(n => n.Id))
            .ToList();
        var impact = CompletionImpact.Calculate(_graph, completing);

        var now = DateTimeOffset.Now;
        PushUndo();

        foreach (var node in plain)
        {
            node.Model.Status = NodeStatus.Done;
            node.Model.CompletedAt = now;
            node.Model.UpdatedAt = now;
        }

        var results = repeats.Select(n => RepeatService.Advance(n.Model, now)).ToList();

        MarkDirty();
        RefreshAll();

        StatusMessage = (plain.Count, results.Count) switch
        {
            (0, 1) => results[0].BecameDone
                ? $"{results[0].After.Completed} / {results[0].After.Target} 回。最後の1回を達成して完了しました。"
                : $"{results[0].After.Completed} / {results[0].After.Target} 回達成しました。",
            (0, var repeated) => $"{repeated} 件を 1 回ずつ達成しました。",
            (1, 0) => "完了にしました。",
            (var done, 0) => $"{done} 件を完了にしました。",
            var (done, repeated) => $"通常 {done} 件を完了、繰り返し {repeated} 件を 1 回達成しました。",
        };

        PlayCompletion(impact);
        if (targets.Count == 1 && targets[0].Model.Status == NodeStatus.Done) AnnounceUnlocked(targets[0]);
    }

    /// <summary>
    /// 汎用の状態指定を、回数つきの項目でも矛盾しない操作へ読み替える。
    /// 「3 回中 1 回なのに完了」を作る経路を残さないための受け皿。
    /// </summary>
    public void ApplyRepeatStatus(NodeViewModel node, NodeStatus status)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Model.Repeat is null) return;

        switch (status)
        {
            case NodeStatus.Done:
                AdvanceRepeat(node);
                return;
            case NodeStatus.InProgress:
                BeginRepeat(node);
                return;
            case NodeStatus.Cancelled:
                if (node.Model.Status != NodeStatus.Cancelled) ToggleRepeatCancel(node);
                return;
            default:
                if (node.Model.Status == NodeStatus.Cancelled) ToggleRepeatCancel(node);
                else ApplyRepeat(node, node.Model.Repeat.TargetCount, 0);
                return;
        }
    }

    // ---- 履歴の面倒を見る土台 ----

    /// <summary>
    /// 履歴を積んでから操作を試し、何も変わらなければ履歴ごと取り消す。
    /// 空振りで Ctrl+Z が 1 回素通りしたり、Ctrl+Y が消えたりしないようにする。
    /// </summary>
    private bool RunRepeat(NodeViewModel node, Func<TodoNode, DateTimeOffset, RepeatResult> operation, out RepeatResult result)
    {
        var redo = _redo.ToArray();
        PushUndo();

        result = operation(node.Model, DateTimeOffset.Now);
        if (!result.Applied)
        {
            DropLastUndo();
            _redo.Clear();
            _redo.AddRange(redo);
            if (result.Error is { } error) StatusMessage = error;
            return false;
        }

        MarkDirty();
        RefreshAll();
        return true;
    }
}
