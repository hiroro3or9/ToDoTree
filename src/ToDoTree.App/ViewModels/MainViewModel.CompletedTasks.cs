using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Graph;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    private ICommand? _focusCompletedTaskCommand;
    private DateOnly _completedTasksDate;
    private bool _isCompletedTasksExpanded;

    public ObservableCollection<NodeViewModel> CompletedTodayNodes { get; } = [];
    public string CompletedTodayHeader => $"今日完了したタスク（{CompletedTodayNodes.Count} 件）";
    public string CompletedTodayDescription => $"{_completedTasksDate:yyyy/MM/dd} ・ 完了時刻の新しい順";
    public bool HasNoCompletedToday => CompletedTodayNodes.Count == 0;

    public bool IsCompletedTasksExpanded
    {
        get => _isCompletedTasksExpanded;
        set
        {
            if (SetProperty(ref _isCompletedTasksExpanded, value) && value) RefreshCompletedTasks();
        }
    }

    public ICommand FocusCompletedTaskCommand => _focusCompletedTaskCommand ??= new RelayCommand(value =>
    {
        if (value is not NodeViewModel item || !_byId.TryGetValue(item.Id, out var node)) return;
        if (_focusedBlockId is not null) ToggleFocus();
        _collapsed.Clear();
        _focusId = null;
        SearchText = string.Empty;
        SelectedTag = null;
        HideCompleted = false;
        RevealBlockPath(node.Id);
        SelectedNode = node;
        RefreshVisibility();
        NotifyVisualsChanged();
        CenterOnRequested?.Invoke(this, node);
    });

    /// <summary>保存済みの完了日時から再構築する。共通タイマーでも呼び、日付変更に追従する。</summary>
    public void RefreshCompletedTasks(DateTimeOffset? now = null)
    {
        _completedTasksDate = DateOnly.FromDateTime((now ?? DateTimeOffset.Now).LocalDateTime);
        var desired = CompletedTaskQuery.ForDate(_project.Nodes, _completedTasksDate, TimeZoneInfo.Local)
            .Select(node => _byId[node.Id]).ToList();
        var desiredSet = desired.ToHashSet();
        for (var i = CompletedTodayNodes.Count - 1; i >= 0; i--)
            if (!desiredSet.Contains(CompletedTodayNodes[i])) CompletedTodayNodes.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var current = CompletedTodayNodes.IndexOf(desired[i]);
            if (current < 0) CompletedTodayNodes.Insert(i, desired[i]);
            else if (current != i) CompletedTodayNodes.Move(current, i);
        }
        OnPropertyChanged(nameof(CompletedTodayHeader), nameof(CompletedTodayDescription), nameof(HasNoCompletedToday));
    }
}
