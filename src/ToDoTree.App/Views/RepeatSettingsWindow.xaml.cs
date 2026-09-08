using System.Windows;
using System.Windows.Controls;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Views;

/// <summary>
/// 「何回で完了にするか」を決める画面。設定・編集・回数の訂正で共通に使う。
///
/// 入力しているあいだモデルには触れない。「適用」で目標と達成回数をまとめて反映し、
/// Esc とキャンセルは何も変えない。矛盾する組み合わせは丸めずに「適用」を無効にする。
/// </summary>
public partial class RepeatSettingsWindow : Window
{
    private NodeStatus _current = NodeStatus.NotStarted;
    private RepeatState _before;
    private bool _loading;

    public RepeatSettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { TargetBox.Focus(); TargetBox.SelectAll(); };
    }

    /// <summary>適用する目標回数。</summary>
    public int Target { get; private set; }

    /// <summary>適用する達成回数。</summary>
    public int Completed { get; private set; }

    /// <summary>「繰り返しを解除」で閉じた。</summary>
    public bool ClearRequested { get; private set; }

    /// <summary>開く前に、対象の名前と現在の回数・状態を渡す。</summary>
    public void Configure(string title, RepeatState before, NodeStatus current, int target, int completed, bool isNew)
    {
        _loading = true;
        _current = current;
        _before = before;

        Title = isNew ? "繰り返しを設定" : "繰り返しを編集";
        HeaderText.Text = isNew
            ? $"「{title}」を何回で完了にしますか"
            : $"「{title}」の回数を直します";
        HintText.Text = isNew && current == NodeStatus.Done
            ? "すでに完了しているので、達成済みとして設定します。回数を減らすと完了が外れます。"
            : "最後の1回を達成したときに完了になり、後続の待ちが解けます。";
        TargetRangeText.Text = $"{RepeatProgress.MinTarget} 〜 {RepeatProgress.MaxTarget} の整数";

        TargetBox.Text = target.ToString();
        CompletedBox.Text = completed.ToString();
        ClearPanel.Visibility = before.IsRepeating ? Visibility.Visible : Visibility.Collapsed;

        _loading = false;
        UpdatePreview();
    }

    private void OnCountChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) UpdatePreview();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        CompletedBox.Text = "0";
        CompletedBox.Focus();
        CompletedBox.SelectAll();
    }

    private void UpdatePreview()
    {
        BeforeText.Text = _before.IsRepeating
            ? $"変更前： {_before.Completed} / {_before.Target} 回・{Labels.Of(_before.Status)}"
            : $"変更前： 回数なし・{Labels.Of(_current)}";

        if (!TryRead(TargetBox, out var target) || !TryRead(CompletedBox, out var completed))
        {
            AfterText.Text = "適用後： —";
            NoteText.Text = "半角の整数で入力してください。";
            ApplyButton.IsEnabled = false;
            return;
        }

        if (RepeatService.ValidateInput(target, completed) is { } error)
        {
            AfterText.Text = "適用後： —";
            NoteText.Text = error;
            ApplyButton.IsEnabled = false;
            return;
        }

        var status = RepeatService.PreviewStatus(_current, target, completed);
        AfterText.Text = $"適用後： {completed} / {target} 回・{Labels.Of(status)}";
        NoteText.Text = Note(status, target, completed);
        ClearText.Text = _before.IsRepeating
            ? $"解除すると回数の記録（{_before.Completed} / {_before.Target} 回）が消え、"
              + $"{Labels.Of(_before.Status)}のまま通常のステップに戻ります。"
            : string.Empty;
        ApplyButton.IsEnabled = true;
    }

    private string Note(NodeStatus status, int target, int completed) => status switch
    {
        NodeStatus.Cancelled => "取り消し中なので、適用しても取り消しのままです。再開すると回数どおりの状態になります。",
        NodeStatus.Done when _current == NodeStatus.Done => "完了のままです。完了日時は変わりません。",
        NodeStatus.Done => "この操作で完了になります。後続の待ちが解けます。",
        _ when _current == NodeStatus.Done => "完了が外れます。未着手の後続はまた待ちになります。",
        _ => $"あと {target - completed} 回で完了します。",
    };

    private static bool TryRead(TextBox box, out int value) =>
        int.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(TargetBox, out var target) || !TryRead(CompletedBox, out var completed)) return;
        if (RepeatService.ValidateInput(target, completed) is not null) return;

        Target = target;
        Completed = completed;
        DialogResult = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ClearRequested = true;
        DialogResult = true;
    }
}
