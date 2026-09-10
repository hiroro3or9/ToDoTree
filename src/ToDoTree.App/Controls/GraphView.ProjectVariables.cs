using System.Windows;
using ToDoTree.App.Views;

namespace ToDoTree.App.Controls;

/// <summary>プロジェクト変数の設定画面を開くところ。</summary>
public partial class GraphView
{
    private void OpenProjectVariables(object? sender, EventArgs e)
    {
        if (_viewModel is not { } viewModel) return;

        var dialog = new ProjectVariablesWindow(viewModel.Graph.Project, viewModel.CopyVariables());
        if (Window.GetWindow(this) is { } owner) dialog.Owner = owner;

        if (dialog.ShowDialog() != true)
        {
            viewModel.StatusMessage = "プロジェクト変数の編集をやめました。";
            FocusCanvas();
            return;
        }

        if (viewModel.ApplyProjectVariables(dialog.Definitions, dialog.Renames) is { } error)
        {
            MessageBox.Show($"プロジェクト変数を保存できませんでした。\n\n{error}",
                "ToDoTree", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        FocusCanvas();
    }
}
