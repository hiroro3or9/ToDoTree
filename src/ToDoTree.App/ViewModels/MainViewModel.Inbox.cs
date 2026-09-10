using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public const string InboxDragFormat = "ToDoTree.InboxItem";
    public ObservableCollection<InboxItem> InboxItems { get; } = [];
    private string _inboxTitle = string.Empty;
    private ICommand? _addInboxCommand, _placeInboxCommand, _deleteInboxCommand, _focusBlockingCauseCommand;
    public string InboxTitle
    {
        get => _inboxTitle;
        set => SetProperty(ref _inboxTitle, value);
    }
    public ICommand AddInboxCommand => _addInboxCommand ??= new RelayCommand(() =>
    {
        var title = InboxTitle.Trim();
        if (title.Length == 0) return;
        PushUndo();
        var item = new InboxItem { Title = title };
        _project.Inbox.Add(item);
        InboxItems.Add(item);
        InboxTitle = string.Empty;
        MarkDirty();
        StatusMessage = "受信箱に保存しました。キャンバスへドラッグするか、配置ボタンで整理できます。";
    }, () => !string.IsNullOrWhiteSpace(InboxTitle));
    public ICommand DeleteInboxCommand => _deleteInboxCommand ??= new RelayCommand(value =>
    {
        if (value is not InboxItem item || !_project.Inbox.Contains(item)) return;
        PushUndo();
        _project.Inbox.Remove(item);
        InboxItems.Remove(item);
        MarkDirty();
        StatusMessage = "受信箱から削除しました。Ctrl+Zで戻せます。";
    });
    public ICommand PlaceInboxCommand => _placeInboxCommand ??= new RelayCommand(value =>
    {
        if (value is InboxItem item) PlaceInboxItem(item.Id);
    });
    public bool PlaceInboxItem(Guid itemId, double? x = null, double? y = null)
    {
        var item = _project.Inbox.FirstOrDefault(i => i.Id == itemId);
        if (item is null || IsNaming || IsConnecting) return false;
        if (x is { } px && !double.IsFinite(px) || y is { } py && !double.IsFinite(py)) return false;
        PushUndo();
        var model = new TodoNode { Title = item.Title, CreatedAt = item.CreatedAt };
        if (x is { } xx && y is { } yy)
        {
            model.X = xx; model.Y = yy;
            NudgeUntilFree(model);
        }
        else PlaceNear(model, null, false);
        _project.Inbox.Remove(item);
        InboxItems.Remove(item);
        _graph.AddNode(model);
        if (_focusedBlockId is not null) ToggleFocus();
        _focusId = null;
        SelectedTag = null;
        SearchText = string.Empty;
        var vm = new NodeViewModel(model, this);
        Nodes.Add(vm);
        _byId.Add(model.Id, vm);
        SelectedNode = vm;
        MarkDirty();
        RefreshAll();
        EnsureVisibleRequested?.Invoke(this, vm);
        StatusMessage = "受信箱から配置しました。接続点をドラッグして前後の作業と繋げられます。";
        return true;
    }
    public ICommand FocusBlockingCauseCommand => _focusBlockingCauseCommand ??= new RelayCommand(value =>
    {
        if (value is not NodeViewModel node || !_byId.ContainsKey(node.Id)) return;
        if (_focusedBlockId is not null) ToggleFocus();
        _collapsed.Clear();
        _focusId = null;
        SearchText = string.Empty;
        SelectedTag = null;
        HideCompleted = false;
        SelectedNode = node;
        RefreshVisibility();
        NotifyVisualsChanged();
        CenterOnRequested?.Invoke(this, node);
    });
    private void LoadInbox()
    {
        InboxItems.Clear();
        foreach (var item in _project.Inbox) InboxItems.Add(item);
    }
}

public sealed record InboxDragData(Guid DocumentId, Guid ItemId);
