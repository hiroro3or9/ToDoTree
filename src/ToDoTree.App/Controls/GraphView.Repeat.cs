using System.Windows;
using ToDoTree.App.ViewModels;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;

namespace ToDoTree.App.Controls;

/// <summary>回数で完了する項目の設定画面を開くところ。</summary>
public partial class GraphView
{
    private void OpenRepeatSettings(NodeViewModel node, bool isNew)
    {
        if (_viewModel is not { } viewModel) return;

        var (target, completed) = viewModel.RepeatSeed(node);
        var dialog = new RepeatSettingsWindow();
        if (Window.GetWindow(this) is { } owner) dialog.Owner = owner;
        dialog.Configure(node.Title, RepeatState.Of(node.Model), node.Model.Status, target, completed, isNew);

        // 取り消しは「接続に失敗した」でも「設定した」でもない。何も変えずに戻る。
        if (dialog.ShowDialog() != true)
        {
            viewModel.StatusMessage = isNew ? "繰り返しの設定をやめました。" : "回数の変更をやめました。";
            FocusCanvas();
            return;
        }

        if (dialog.ClearRequested) viewModel.ClearRepeat(node, confirm: false);
        else viewModel.ApplyRepeat(node, dialog.Target, dialog.Completed);

        FocusCanvas();
    }
}
