using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public const string InboxDragFormat = "ToDoTree.InboxItem";
    public ObservableCollection<InboxItemViewModel> InboxItems { get; } = [];
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
        InboxItems.Add(new InboxItemViewModel(item, this));
        InboxTitle = string.Empty;
        MarkDirty();
        StatusMessage = "受信箱に保存しました。キャンバスへドラッグするか、配置ボタンで整理できます。";
    }, () => !string.IsNullOrWhiteSpace(InboxTitle));
    public ICommand DeleteInboxCommand => _deleteInboxCommand ??= new RelayCommand(value =>
    {
        if (value is not InboxItemViewModel row || !_project.Inbox.Contains(row.Model)) return;
        PushUndo();
        _project.Inbox.Remove(row.Model);
        InboxItems.Remove(row);
        MarkDirty();
        StatusMessage = "受信箱から削除しました。Ctrl+Zで戻せます。";
    });
    public ICommand PlaceInboxCommand => _placeInboxCommand ??= new RelayCommand(value =>
    {
        if (value is InboxItemViewModel row) PlaceInboxItem(row.Id);
    });
    public bool PlaceInboxItem(Guid itemId, double? x = null, double? y = null)
    {
        var row = InboxItems.FirstOrDefault(i => i.Id == itemId);
        if (row is null || !_project.Inbox.Contains(row.Model) || IsNaming || IsConnecting) return false;
        if (x is { } px && !double.IsFinite(px) || y is { } py && !double.IsFinite(py)) return false;
        PushUndo();

        // 受信箱に書いた原文をそのまま引き継ぐ。展開後の文字列を持ち込むと、
        // 値を直したときにキャンバス側だけが古い名前のまま取り残される。
        var item = row.Model;
        var model = new TodoNode { Title = item.Title, CreatedAt = item.CreatedAt };
        if (x is { } xx && y is { } yy)
        {
            model.X = xx; model.Y = yy;
            NudgeUntilFree(model);
        }
        else PlaceNear(model, null, false);
        _project.Inbox.Remove(item);
        InboxItems.Remove(row);
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
        foreach (var item in _project.Inbox) InboxItems.Add(new InboxItemViewModel(item, this));
    }
}

public sealed record InboxDragData(Guid DocumentId, Guid ItemId);

/// <summary>受信箱の 1 行。原文を持ったまま、一覧には展開後の名前を出す。</summary>
public sealed class InboxItemViewModel(InboxItem model, MainViewModel owner) : ObservableObject
{
    public InboxItem Model { get; } = model;

    public Guid Id => Model.Id;

    /// <summary>保存してある原文。配置したときはこれがステップのタイトルになる。</summary>
    public string Title => Model.Title;

    public string DisplayTitle => owner.VariableResolver.Expand(Model.Title);

    public bool HasUndefinedVariables => owner.VariableResolver.Scan(Model.Title).HasUndefined;

    public string Tooltip
    {
        get
        {
            var scan = owner.VariableResolver.Scan(Model.Title);
            if (!scan.HasSubstitution && !scan.HasUndefined) return "キャンバスへドラッグして配置";

            var lines = new List<string> { $"原文：{Model.Title}" };
            if (scan.HasUndefined) lines.Add("未定義: " + string.Join("、", scan.UndefinedNames));
            lines.Add("キャンバスへドラッグして配置");
            return string.Join("\n", lines);
        }
    }

    internal void RefreshDisplay() =>
        OnPropertyChanged(nameof(DisplayTitle), nameof(HasUndefinedVariables), nameof(Tooltip));
}
