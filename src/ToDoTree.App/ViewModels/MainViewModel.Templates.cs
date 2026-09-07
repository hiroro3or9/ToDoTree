using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<TodoProject?>? TemplateLibraryRequested;
    private ICommand? _saveTemplateCommand;
    private ICommand? _openTemplatesCommand;
    public ICommand SaveTemplateCommand => _saveTemplateCommand ??= new RelayCommand(() =>
    {
        var ids = SelectedBlock?.Model.NodeIds ?? [.. SelectedNodes.Select(n => n.Id)];
        var name = SelectedBlock?.Title ?? SelectedNode?.Title ?? "新しい部品";
        if (string.IsNullOrWhiteSpace(name)) name = "新しい部品";
        TemplateLibraryRequested?.Invoke(BranchTemplate.Capture(_project, ids, name));
    }, () => !IsNaming && (HasSelectedBlock || SelectionCount > 0));
    public ICommand OpenTemplatesCommand => _openTemplatesCommand ??= new RelayCommand(
        () => TemplateLibraryRequested?.Invoke(null), () => !IsNaming);

    public void InsertTemplate(TodoProject template, double x, double y)
    {
        if (IsNaming || IsConnecting) return;
        var instance = BranchTemplate.Instantiate(template, x, y);
        var right = instance.Nodes.Max(n => n.X) + NodeViewModel.CardWidth;
        var bottom = instance.Nodes.Max(n => n.Y) + NodeViewModel.CardHeight;
        // まとまりを壊さず、既存カードと重なる場合はその下へ逃がす。
        if (Nodes.Any(n => n.X < right + 24 && n.X + NodeViewModel.CardWidth + 24 > x
            && n.Y < bottom + 24 && n.Y + NodeViewModel.CardHeight + 24 > y))
        {
            instance = BranchTemplate.Instantiate(template, x, Nodes.Max(n => n.Y) + NodeViewModel.CardHeight + 80);
        }
        PushUndo();
        _project.Nodes.AddRange(instance.Nodes);
        _project.Edges.AddRange(instance.Edges);
        _project.Blocks.AddRange(instance.Blocks);
        _graph.Rebuild();
        foreach (var model in instance.Nodes)
        {
            var node = new NodeViewModel(model, this);
            Nodes.Add(node); _byId.Add(node.Id, node);
        }
        RebuildBlocks(); RebuildEdges();
        // 独立した部品が既存のフォーカス範囲外に隠れないようにする。
        if (_focusedBlockId is not null) ToggleFocus();
        _focusId = null;
        SelectNodes(instance.Nodes.Select(n => _byId[n.Id]));
        MarkDirty(); RefreshAll();
        EnsureVisibleRequested?.Invoke(this, _byId[instance.Nodes[0].Id]);
        StatusMessage = $"「{template.Name}」を {instance.Nodes.Count} 件のステップとして配置しました。Ctrl+Z で戻せます。";
    }
}
