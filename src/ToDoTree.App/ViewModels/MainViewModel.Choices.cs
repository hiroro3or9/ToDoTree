using System.Windows;
using System.Windows.Input;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    private ICommand? _addChoiceBranchCommand, _configureChoiceCommand, _captureSpecimenCommand, _openSpecimensCommand;
    public ICommand AddChoiceBranchCommand => _addChoiceBranchCommand ??= new RelayCommand(AddChoiceBranch,
        () => SelectedNode is not null && Procedure is null);
    public ICommand ConfigureChoiceCommand => _configureChoiceCommand ??= new RelayCommand(() =>
    {
        if (SelectedNode is not { } node) return;
        var dialog = new ChoiceWindow(this, node) { Owner = ActiveOwner() };
        dialog.ShowDialog();
    }, () => SelectedNode is { } n && Procedure is null && (_project.Edges.Count(e => e.FromId == n.Id) >= 2 || n.Model.IsChoice));
    public ICommand CaptureSpecimenCommand => _captureSpecimenCommand ??= new RelayCommand(() =>
    {
        if (SelectedNode is not { } node) return;
        var dialog = new CaptureSpecimenWindow(this, node) { Owner = ActiveOwner() };
        dialog.ShowDialog();
    }, () => Procedure is null && SelectedNode?.Model is { Kind: NodeKind.Goal, Status: NodeStatus.Done });
    public ICommand OpenSpecimensCommand => _openSpecimensCommand ??= new RelayCommand(() =>
        new SpecimenAlbumWindow(this) { Owner = ActiveOwner() }.ShowDialog(), () => Procedure is null);

    private static Window? ActiveOwner() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

    public void AddChoiceBranch()
    {
        if (SelectedNode is not { } source || Procedure is not null) return;
        PushUndo();
        source.Model.IsChoice = true;
        source.Model.SelectedChoiceEdgeId = null;
        foreach (var title in new[] { "自作する", "既製品を使う" })
        {
            var model = new TodoNode { Title = title };
            PlaceNear(model, source, sibling: false);
            _graph.AddNode(model);
            _graph.Connect(source.Id, model.Id);
            var node = new NodeViewModel(model, this);
            Nodes.Add(node); _byId.Add(node.Id, node);
        }
        RebuildEdges(); MarkDirty(); RefreshAll();
        StatusMessage = "選択肢を追加しました。名前を編集し、「進める道を選ぶ…」で一つ選んでください。";
    }

    public void ApplyChoice(Guid sourceId, bool enabled, Guid? selectedEdgeId, IReadOnlyDictionary<Guid, string> reasons)
    {
        var source = _graph.Find(sourceId) ?? throw new InvalidOperationException("分岐元が見つかりません。");
        var edges = _project.Edges.Where(e => e.FromId == sourceId).ToArray();
        if (enabled && edges.Length < 2) throw new InvalidOperationException("分岐には二つ以上の後続を接続してください。");
        if (enabled && selectedEdgeId is { } id && !edges.Any(e => e.Id == id))
            throw new InvalidOperationException("選択した道が見つかりません。");
        selectedEdgeId = enabled ? selectedEdgeId : null;
        if (source.IsChoice == enabled && source.SelectedChoiceEdgeId == selectedEdgeId
            && edges.All(e => e.DecisionReason == reasons.GetValueOrDefault(e.Id, e.DecisionReason))) return;
        PushUndo();
        source.IsChoice = enabled; source.SelectedChoiceEdgeId = selectedEdgeId;
        source.UpdatedAt = DateTimeOffset.Now;
        foreach (var edge in edges) edge.DecisionReason = reasons.GetValueOrDefault(edge.Id, edge.DecisionReason);
        MarkDirty(); RefreshAll();
        StatusMessage = enabled ? "進める道と判断理由を保存しました。Ctrl+Z で戻せます。" : "通常の並行分岐に戻しました。";
    }

    public AchievementSpecimen CaptureSpecimen(Guid goalId, string category, string reflection)
    {
        var specimen = SpecimenService.Capture(_graph, goalId, category, reflection);
        PushUndo(); _project.Specimens.Add(specimen); MarkDirty(); RefreshAll();
        StatusMessage = $"「{specimen.Title}」を標本帳に保存しました。";
        return specimen;
    }

    public void RemoveSpecimen(Guid id)
    {
        if (!_project.Specimens.Any(s => s.Id == id)) return;
        PushUndo(); _project.Specimens.RemoveAll(s => s.Id == id); MarkDirty(); RefreshAll();
        StatusMessage = "標本を外しました。Ctrl+Z で戻せます。";
    }

    public bool CanProgressBranch(Guid id)
    {
        if (!CanChangeTaskStatus(id)) return false;
        if (!TaskHierarchy.AncestorsReady(_graph, id))
        {
            StatusMessage = "親作業の先行ステップ、ブロック、または道の選択を先に解決してください。";
            return false;
        }
        if (_graph.BranchStateOf(id) == BranchState.Active) return true;
        StatusMessage = "この道は見送り、または選択待ちです。分岐元の「進める道を選ぶ…」から変更してください。";
        return false;
    }
}
