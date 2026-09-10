using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<TodoProject?>? TemplateLibraryRequested;
    private ICommand? _saveTemplateCommand;
    private ICommand? _openTemplatesCommand;
    public ICommand SaveTemplateCommand => _saveTemplateCommand ??= new RelayCommand(() =>
    {
        IEnumerable<Guid> ids = SelectedBlock is { } block
            ? DescendantNodeIds(block.Id) : SelectedNodes.Select(n => n.Id);
        // 部品名は展開の対象外なので、ステップから候補を作るときは表示名を使う。
        // 原文をそのまま名前にすると、部品一覧に {製品名} が並んでしまう。
        var name = SelectedBlock?.Title ?? SelectedNode?.DisplayTitle ?? "新しい部品";
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
        // 足りない定義だけを補い、同名の定義は挿入先を優先する。既存の値を黙って
        // 上書きすると、もとから使っていたステップの表示まで変わってしまう。
        var added = ProjectVariableService.MissingDefinitions(_project, instance.Variables);

        PushUndo();
        _project.Variables.AddRange(added);
        if (added.Count > 0) RebuildVariableResolver();
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
        var definitions = added.Count == 0 ? string.Empty : $"・変数 {added.Count} 件を追加";
        StatusMessage = $"「{template.Name}」を {instance.Nodes.Count} 件のステップとして配置しました{definitions}。Ctrl+Z で戻せます。";
    }
}
