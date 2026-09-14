using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<MainViewModel, TodoProject, string, Guid>? BranchMoved;
    public event Action<MainViewModel, ProjectLink>? ProjectLinkRequested;
    private ICommand? _moveBranchCommand, _openProjectLinkCommand;
    public ICommand MoveBranchCommand => _moveBranchCommand ??= new RelayCommand(RequestBranchMove,
        () => IsNormalTodo && !IsNaming && SelectedNode is { IsProjectEntrance: false });
    public ICommand OpenProjectLinkCommand => _openProjectLinkCommand ??= new RelayCommand(
        () => { if (SelectedNode is { } node) OpenProjectEntrance(node); },
        () => SelectedNode?.IsProjectEntrance == true);

    public bool OpenProjectEntrance(NodeViewModel node)
    {
        if (node.Model.ProjectLink is not { } link) return false;
        SelectOnly(node);
        if (ProjectLinkRequested is null) StatusMessage = "移動先はプロジェクトのワークスペースから開いてください。";
        else ProjectLinkRequested.Invoke(this, link.Clone());
        return true;
    }

    private void RequestBranchMove()
    {
        if (!MoveBranchCommand.CanExecute(null) || SelectedNode is not { } root) return;
        try
        {
            var count = BranchMoveService.Collect(_project, root.Id).Count;
            var summary = $"「{root.DisplayTitle}」から先の {count} 件を、新しいプロジェクトへ移します。\n\n"
                + "合流先も対象です。所属ブロックは全体を移すため、同じブロックの他の枝とその下流も含まれます。\n"
                + "元の接続先には入口が残り、ダブルクリックで移動先を開けます。\n\n"
                + "次の画面で、新しい保存先を指定してください。";
            if (MessageBox.Show(summary, "枝の引っ越し", MessageBoxButton.OKCancel,
                MessageBoxImage.Information) != MessageBoxResult.OK) return;
            var dialog = new SaveFileDialog
            {
                Filter = _store.FileFilter, DefaultExt = _store.DefaultExtension, AddExtension = true, OverwritePrompt = false,
                Title = $"枝の引っ越し — {count} 件の新しい保存先",
                FileName = SanitizeFileName(root.DisplayTitle) + _store.DefaultExtension,
            };
            if (dialog.ShowDialog() == true) MoveBranchTo(dialog.FileName);
        }
        catch (Exception ex) { StatusMessage = $"引っ越しできませんでした: {ex.Message}"; }
    }

    /// <summary>先に移動先、次に元文書を保存する。保存失敗で元の枝や履歴を失わない。</summary>
    internal bool MoveBranchTo(string path)
    {
        if (IsProcedure || SelectedNode is not { IsProjectEntrance: false } root) return false;
        CommitPendingBlockEdit();
        foreach (var node in Nodes) node.CommitNotes();
        EndEdit();
        var destinationSaved = false;
        BranchMoveResult move;
        try
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path) || CanSaveToPath?.Invoke(path) == false
                || (!string.IsNullOrEmpty(_filePath) && string.Equals(Path.GetFullPath(_filePath), path, StringComparison.OrdinalIgnoreCase))
                || string.Equals(Path.GetFullPath(RecoveryFilePath), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("既存のプロジェクトには上書きできません。新しい保存先を選んでください。");
            var name = Path.GetFileName(path);
            if (name.EndsWith(_store.DefaultExtension, StringComparison.OrdinalIgnoreCase))
                name = name[..^_store.DefaultExtension.Length];
            else name = Path.GetFileNameWithoutExtension(name);
            move = BranchMoveService.Create(_project, root.Id, path, name);
            _store.Save(path, move.Destination);
            destinationSaved = true;
            _store.Save(string.IsNullOrEmpty(_filePath) ? RecoveryFilePath : _filePath, move.Source);
        }
        catch (Exception ex)
        {
            StatusMessage = $"引っ越しできませんでした。元の枝は残っています。{ex.Message}"
                + (destinationSaved ? $" 移動先にはコピーを保存済みです: {path}" : string.Empty);
            return false;
        }

        PushUndo();
        LoadProject(move.Source, _filePath);
        IsDirty = string.IsNullOrEmpty(_filePath);
        SelectOnly(_byId[move.EntranceId]);
        StatusMessage = $"{move.MovedCount} 件を「{move.Destination.Name}」へ引っ越しました。入口のダブルクリックで開けます。"
            + " Ctrl+Z で元の枝を復元できます（移動先は残ります）。";
        try
        {
            DocumentStateChanged?.Invoke(this, EventArgs.Empty);
            BranchMoved?.Invoke(this, move.Destination, path, root.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"引っ越しは保存済みですが、移動先のタブを開けませんでした: {ex.Message}";
        }
        return true;
    }

    internal void RevealProjectLinkTarget(Guid nodeId)
    {
        if (!_byId.TryGetValue(nodeId, out var node)) return;
        // 完了一覧からのジャンプと同じく、検索や折りたたみに隠れた対象も表示する。
        FocusCompletedTaskCommand.Execute(node);
    }
}
