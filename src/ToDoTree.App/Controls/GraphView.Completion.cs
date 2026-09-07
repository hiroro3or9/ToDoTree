using System.Windows;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;

namespace ToDoTree.App.Controls;

public partial class GraphView
{
    private NodeViewModel? _completionHover;

    private void SetCompletionHover(NodeViewModel? node)
    {
        _completionHover = node;
        if (_viewModel is null || node is null || node.Model.IsSettled || node.IsEditing
            || !node.IsVisible || !_viewModel.Nodes.Contains(node) || _viewModel.IsConnecting)
        {
            EdgeRenderer.SetCompletionPreview(null);
            CompletionHint.Visibility = Visibility.Collapsed;
            return;
        }
        var impact = CompletionImpact.Calculate(_viewModel.Graph, [node.Id]);
        EdgeRenderer.SetCompletionPreview(impact);
        var hidden = _viewModel.Nodes.Count(n => impact.Unlocked.Contains(n.Id) && !n.IsVisible);
        CompletionHintText.Text = impact.Unlocked.Count == 0
            ? "これが終わったら：新しく着手できるステップはありません"
            : $"これが終わったら：{impact.Unlocked.Count} 件に着手できます"
                + (hidden > 0 ? $"（うち {hidden} 件は非表示）" : "");
        CompletionHint.Visibility = Visibility.Visible;
    }

    private void OnCompletionRequested(CompletionImpact impact)
    {
        SetCompletionHover(null);
        EdgeRenderer.PlayCompletion(impact);
    }
}
