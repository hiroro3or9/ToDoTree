using System.Windows.Input;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// プロジェクト変数の受け渡し。設定画面は下書きを編集し、保存のときだけここへ戻ってくる。
///
/// リゾルバーはこのタブの定義のスナップショットから作る。静的な「いまのプロジェクト」を
/// 持たせないので、別タブの値が混ざらない。Undo／Redo でモデルが差し替わったときは
/// <see cref="RebuildVariableResolver"/> で作り直す。
/// </summary>
public sealed partial class MainViewModel
{
    private ProjectVariableResolver _variables = ProjectVariableResolver.Empty;
    private ICommand? _openProjectVariablesCommand;
    private ICommand? _copyDisplayTitleCommand;
    private ICommand? _copyDisplayNotesCommand;

    /// <summary>設定画面を開いてほしい。ウィンドウの用意はビュー側が持つ。</summary>
    public event EventHandler? ProjectVariablesRequested;

    /// <summary>いまの定義から作った、表示専用のリゾルバー。</summary>
    public ProjectVariableResolver VariableResolver => _variables;

    /// <summary>この画面が持つ変数の数。メニューの見出しに出す。</summary>
    public int VariableCount => _project.Variables.Count;

    public string ProjectVariablesLabel =>
        VariableCount == 0 ? "プロジェクト変数…" : $"プロジェクト変数…（{VariableCount}）";

    public ICommand OpenProjectVariablesCommand => _openProjectVariablesCommand ??= new RelayCommand(() =>
    {
        // 進行中の通常編集を先に確定する。下書きの比較対象が編集途中の原文になると、
        // 使用箇所と改名の計画がその場かぎりの文字列を指してしまう。
        CommitPendingBlockEdit();
        EndEdit();
        ProjectVariablesRequested?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>
    /// 展開後のタイトルをコピーする。入力欄のコピーは選んだ原文のままにしておき、
    /// 「値が入ったほうの文字列がほしい」ときはこちらを使う。
    /// </summary>
    public ICommand CopyDisplayTitleCommand => _copyDisplayTitleCommand ??= new RelayCommand(
        () => CopyText(SelectedNode?.DisplayTitle, "表示名"), () => SelectedNode is not null);

    public ICommand CopyDisplayNotesCommand => _copyDisplayNotesCommand ??= new RelayCommand(
        () => CopyText(SelectedNode?.DisplayNotesDraft, "表示メモ"), () => SelectedNode is not null);

    private void CopyText(string? text, string label)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = $"{label}をコピーしました。";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            StatusMessage = "コピーできませんでした。";
        }
    }

    /// <summary>設定画面に渡す、いまの定義の複製。画面側で書き換えても本体に響かない。</summary>
    public List<ProjectVariable> CopyVariables() => [.. _project.Variables.Select(v => v.Clone())];

    /// <summary>
    /// 設定画面の結果をまとめて反映する。検証を通ってから 1 回の Undo 単位で適用し、
    /// 変わっていなければ履歴も未保存の印も増やさない。戻り値は拒否した理由。
    /// </summary>
    public string? ApplyProjectVariables(
        IReadOnlyList<ProjectVariable> definitions, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(renames);

        if (ProjectVariableService.Validate(definitions) is { } error) return error;

        // 参照の書き換えは、変更前の定義で読んだ結果に対して行う。
        // 名前を入れ替えたときに、途中の結果をもう一度読んで連鎖させないため。
        var rewrites = ProjectVariableService.PlanRename(_project, _variables, renames);
        var sameDefinitions = definitions.Count == _project.Variables.Count
            && definitions.Zip(_project.Variables).All(pair =>
                string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
                && string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal));

        if (sameDefinitions && rewrites.Count == 0)
        {
            StatusMessage = "プロジェクト変数に変更はありませんでした。";
            return null;
        }

        PushUndo();
        ProjectVariableService.ApplyRename(rewrites);
        _project.Variables = [.. definitions.Select(v => v.Clone())];
        RebuildVariableResolver();
        MarkDirty();
        RefreshAll();

        var renamed = renames.Count == 0 ? string.Empty : $"・{rewrites.Count} 箇所の参照を書き換え";
        StatusMessage = $"プロジェクト変数を保存しました（{definitions.Count} 件{renamed}）。Ctrl+Z で戻せます。";
        return null;
    }

    /// <summary>定義が差し替わったときに、リゾルバーとメニューの見出しを作り直す。</summary>
    internal void RebuildVariableResolver()
    {
        _variables = ProjectVariableResolver.From(_project);
        OnPropertyChanged(nameof(VariableResolver), nameof(VariableCount), nameof(ProjectVariablesLabel));
    }

    /// <summary>
    /// カード以外の表示を更新する。カードは <see cref="NodeViewModel.RefreshDerived"/> が
    /// 同じ値を含むので、ここでは二重に通知しない。
    /// 原文・接続・レイアウト・選択には触らない（値を直すたびに図を作り直さないため）。
    /// </summary>
    internal void RefreshVariableDisplays()
    {
        foreach (var item in InboxItems) item.RefreshDisplay();
    }
}
