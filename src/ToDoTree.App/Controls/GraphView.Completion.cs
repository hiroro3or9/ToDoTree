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

        // 回数つきの項目で、まだ最終回でないときは完了予告を出さない。
        // 途中の1回では後続は動かないので、解放される件数を見せると嘘になる。
        if (node.Model.Repeat is { } repeat && !RepeatService.IsFinalNext(node.Model))
        {
            EdgeRenderer.SetCompletionPreview(null);
            CompletionHintText.Text = $"次の1回：{repeat.CompletedCount + 1} / {repeat.TargetCount} 回";
            CompletionHint.Visibility = Visibility.Visible;
            return;
        }

        var impact = CompletionImpact.Calculate(_viewModel.Graph, [node.Id]);
        EdgeRenderer.SetCompletionPreview(impact);
        var hidden = _viewModel.Nodes.Count(n => impact.Unlocked.Contains(n.Id) && !n.IsVisible);

        // 最終回だけ、通常の完了と同じ予告を出す。
        var lead = node.Model.Repeat is null ? "これが終わったら" : "次の1回で完了";
        CompletionHintText.Text = impact.Unlocked.Count == 0
            ? $"{lead}：新しく着手できるステップはありません"
            : $"{lead}：{impact.Unlocked.Count} 件に着手できます"
                + (hidden > 0 ? $"（うち {hidden} 件は非表示）" : "");
        CompletionHint.Visibility = Visibility.Visible;
    }

    private void OnCompletionRequested(CompletionImpact impact)
    {
        SetCompletionHover(null);
        EdgeRenderer.PlayCompletion(impact);
    }
}
