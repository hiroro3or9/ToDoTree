using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed record TaskBreadcrumb(string Title, ICommand OpenCommand, bool IsCurrent = false)
{
    public string Separator => IsCurrent ? string.Empty : "›";
}

public sealed partial class MainViewModel
{
    private Guid? _currentTaskId;
    private ICommand? _enterTaskCommand, _leaveTaskCommand;
    public Guid? CurrentTaskId => _currentTaskId;
    public bool IsInsideTask => _currentTaskId is not null;
    public ObservableCollection<TaskBreadcrumb> TaskBreadcrumbs { get; } = [];
    public string TaskScopeHint => IsInsideTask
        ? "この作業の内部です。Enter または右クリックでステップを追加できます。名前の変更は F2。"
        : "カードをダブルクリックすると、その作業の内部へ入れます。名前の変更は F2。";
    public string ScopeProgressTitle => IsInsideTask ? "この作業の進捗" : "プロジェクトの進捗";
    public string TaskScopeTitle => _currentTaskId is { } id && _byId.TryGetValue(id, out var node)
        ? node.DisplayTitle + " の内部" : "ゴールへの道のり";

    public ICommand EnterTaskCommand => _enterTaskCommand ??= new RelayCommand(
        () => { if (SelectedNode is { } node) EnterTask(node); }, () => CanEnterTask(SelectedNode));
    public ICommand LeaveTaskCommand => _leaveTaskCommand ??= new RelayCommand(
        () => NavigateTask(_currentTaskId is { } id ? _graph.Find(id)?.ParentTaskId : null), () => IsInsideTask);

    public bool IsInTaskScope(NodeViewModel node) => node.Model.ParentTaskId == _currentTaskId;
    private TodoGraph ScopeGraph() => new(TaskHierarchy.Scope(_project, _currentTaskId));

    private bool CanEnterTask(NodeViewModel? node) => IsNormalTodo
        && node is { Model.Repeat: null, Model.ProjectLink: null };

    public bool EnterTask(NodeViewModel node)
    {
        if (!CanEnterTask(node))
        {
            StatusMessage = "内部ステップは通常の作業カードに定義できます。回数の項目は繰り返しを解除してから開いてください。";
            return false;
        }
        NavigateTask(node.Id);
        return true;
    }

    /// <summary>入口は別プロジェクトを開く。通常の作業は同じ文書の内部を開く。</summary>
    public void ActivateTask(NodeViewModel node)
    {
        if (OpenProjectEntrance(node)) return;
        if (IsProcedure) BeginEdit(node);
        else EnterTask(node);
    }

    public void NavigateTask(Guid? parentId)
    {
        if (parentId is { } id && !_byId.ContainsKey(id)) return;
        CommitPendingBlockEdit();
        foreach (var node in Nodes) node.CommitNotes();
        EndEdit();
        var previous = _currentTaskId;
        if (_focusedBlockId is not null) ToggleFocus();
        _currentTaskId = parentId;
        _graph.NewNodeParentId = parentId;
        _collapsed.Clear();
        _focusId = null;
        SearchText = string.Empty;
        SelectedTag = null;
        HideCompleted = false;
        SelectOnly(null);
        RefreshAll();
        // 一段戻ったときは、今まで中を見ていた親カードを選ぶ。
        if (previous is { } old && _byId.TryGetValue(old, out var owner) && IsInTaskScope(owner))
        {
            RevealBlockPath(owner.Id);
            RefreshVisibility();
            SelectOnly(owner);
        }
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
        StatusMessage = TaskScopeHint;
    }

    internal void RevealTaskScope(Guid nodeId)
    {
        if (_byId.TryGetValue(nodeId, out var node) && node.Model.ParentTaskId != _currentTaskId)
            NavigateTask(node.Model.ParentTaskId);
    }

    internal bool CanChangeTaskStatus(Guid id)
    {
        if (!TaskHierarchy.Children(_project, id).Any()) return true;
        StatusMessage = "この作業の状態は内部ステップから自動集計します。ダブルクリックで内部を開いてください。";
        return false;
    }

    private void RefreshTaskNavigation()
    {
        if (_currentTaskId is { } id && !_byId.ContainsKey(id)) _currentTaskId = null;
        _graph.NewNodeParentId = _currentTaskId;
        TaskBreadcrumbs.Clear();
        TaskBreadcrumbs.Add(new TaskBreadcrumb(ProjectName, new RelayCommand(() => NavigateTask(null)), _currentTaskId is null));
        if (_currentTaskId is { } current && _byId.TryGetValue(current, out var node))
        {
            foreach (var parent in TaskHierarchy.Ancestors(_project, current).Reverse().Append(node.Model))
            {
                var target = parent.Id;
                TaskBreadcrumbs.Add(new TaskBreadcrumb(VariableResolver.Expand(parent.Title), new RelayCommand(() => NavigateTask(target)), target == current));
            }
        }
        OnPropertyChanged(nameof(CurrentTaskId), nameof(IsInsideTask), nameof(TaskScopeHint), nameof(TaskScopeTitle), nameof(ScopeProgressTitle));
        CommandManager.InvalidateRequerySuggested();
    }
}