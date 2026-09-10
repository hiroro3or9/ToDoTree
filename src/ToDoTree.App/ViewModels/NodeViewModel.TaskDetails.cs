using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class NodeViewModel
{
    private ObservableCollection<ChecklistItemViewModel>? _checklistItems;
    private string _newChecklistTitle = string.Empty;
    private ICommand? _addChecklistCommand;
    private ICommand? _toggleBlockCommand;

    public bool CanBlock => !Model.IsSettled;
    public bool IsManuallyBlocked
    {
        get => Model.IsManuallyBlocked && !Model.IsSettled;
        set
        {
            if (!CanBlock || IsManuallyBlocked == value) return;
            owner.PushUndo();
            Model.IsManuallyBlocked = value;
            Touch();
            owner.RefreshAll();
        }
    }
    public string BlockReason
    {
        get => Model.BlockReason;
        set
        {
            if (Model.BlockReason == value) return;
            owner.PushUndo($"blockReason:{Id}");
            Model.BlockReason = value ?? string.Empty;
            Touch();
            owner.RefreshAll();
        }
    }
    public string BlockToggleLabel => IsManuallyBlocked ? "ブロックを解除" : "作業をブロックする";
    public ICommand ToggleBlockCommand => _toggleBlockCommand ??= new RelayCommand(
        () => IsManuallyBlocked = !IsManuallyBlocked, () => CanBlock);

    public IReadOnlyList<BlockingCauseViewModel> BlockingCauses => BlockerAnalysis.Find(owner.Graph, Id)
        .Select(c => new BlockingCauseViewModel(owner.Nodes.Single(n => n.Id == c.Node.Id), c.Reason)).ToList();
    public string BlockingSummary
    {
        get
        {
            if (Model.IsSettled) return "この作業は完了または取り消し済みです。";
            var causes = BlockerAnalysis.Find(owner.Graph, Id);
            return causes.Count == 0 ? "妨げる前提はありません。作業を進められます。"
                : $"ブロック中 {causes.Count(c => c.Node.IsManuallyBlocked)} 件・未完了の前提 {causes.Count(c => !c.Node.IsManuallyBlocked)} 件";
        }
    }

    public ObservableCollection<ChecklistItemViewModel> ChecklistItems => _checklistItems ??=
        new(Model.Checklist.Select(item => new ChecklistItemViewModel(item, this, owner)));
    public string ChecklistSummary => $"チェック {Model.Checklist.Count(i => i.IsChecked)}/{Model.Checklist.Count}";
    public string NewChecklistTitle
    {
        get => _newChecklistTitle;
        set => SetProperty(ref _newChecklistTitle, value);
    }
    public ICommand AddChecklistCommand => _addChecklistCommand ??= new RelayCommand(() =>
    {
        var title = NewChecklistTitle.Trim();
        if (title.Length == 0) return;
        // 先にVM一覧を確定し、モデル追加後の初期化で二重登録しない。
        var items = ChecklistItems;
        owner.PushUndo();
        var item = new ChecklistItem { Title = title };
        Model.Checklist.Add(item);
        items.Add(new(item, this, owner));
        NewChecklistTitle = string.Empty;
        ChecklistChanged();
    }, () => !string.IsNullOrWhiteSpace(NewChecklistTitle));

    internal void RemoveChecklistItem(ChecklistItemViewModel item)
    {
        if (!Model.Checklist.Contains(item.Model)) return;
        owner.PushUndo();
        Model.Checklist.Remove(item.Model);
        ChecklistItems.Remove(item);
        ChecklistChanged();
    }
    internal void ChecklistChanged()
    {
        Touch();
        OnPropertyChanged(nameof(ChecklistSummary), nameof(MetaText), nameof(HasMeta), nameof(CardTooltip));
    }
}

public sealed record BlockingCauseViewModel(NodeViewModel Node, string Reason);

public sealed class ChecklistItemViewModel(ChecklistItem model, NodeViewModel node, MainViewModel owner) : ObservableObject
{
    private ICommand? _deleteCommand;
    public ChecklistItem Model { get; } = model;
    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value) return;
            owner.PushUndo($"checklist:{node.Id}:{Model.Id}");
            Model.Title = value ?? string.Empty;
            OnPropertyChanged();
            node.ChecklistChanged();
        }
    }
    public bool IsChecked
    {
        get => Model.IsChecked;
        set
        {
            if (Model.IsChecked == value) return;
            owner.PushUndo();
            Model.IsChecked = value;
            OnPropertyChanged();
            node.ChecklistChanged();
        }
    }
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(() => node.RemoveChecklistItem(this));
}
