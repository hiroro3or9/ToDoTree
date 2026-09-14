using System.IO;
using System.Windows;
using System.Windows.Threading;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private void OpenMovedBranch(MainViewModel source, TodoProject project, string path, Guid nodeId)
    {
        var document = AddDocument(project, path);
        ActiveDocument = document;
        RevealMovedNode(document, nodeId);
        PersistSession();
    }

    private static void RevealMovedNode(MainViewModel document, Guid nodeId)
    {
        document.RevealProjectLinkTarget(nodeId);
        // タブ切り替えでGraphViewのDataContextが更新された後にも中央へ寄せる。
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => document.RevealProjectLinkTarget(nodeId)));
    }

    private void OpenLinkedProject(MainViewModel source, ProjectLink link) => TryOpenLinkedProject(source, link);

    internal bool TryOpenLinkedProject(MainViewModel source, ProjectLink link)
    {
        try
        {
            var path = Path.GetFullPath(link.FilePath,
                Path.GetDirectoryName(source.FilePath ?? source.RecoveryFilePath)!);
            var document = Documents.FirstOrDefault(d => !string.IsNullOrEmpty(d.FilePath)
                && string.Equals(Path.GetFullPath(d.FilePath), path, StringComparison.OrdinalIgnoreCase));
            // 開いている編集中の内容を優先する。ファイルの上書き・取り違えもIDで検知する。
            var project = document?.Graph.Project ?? _store.Load(path);
            if (project.Id != link.ProjectId)
                throw new InvalidDataException("保存先が別のプロジェクトに置き換わっています。");
            if (!project.Nodes.Any(n => n.Id == link.NodeId))
                throw new InvalidDataException("移動先のステップが削除されています。");
            if (document is null) document = AddDocument(project, path);
            ActiveDocument = document;
            RevealMovedNode(document, link.NodeId);
            document.StatusMessage = $"「{link.ProjectName}」の移動した枝を開きました。";
            PersistSession();
            return true;
        }
        catch (Exception ex)
        {
            source.StatusMessage = $"移動先を開けませんでした: {ex.Message}";
            return false;
        }
    }
}
