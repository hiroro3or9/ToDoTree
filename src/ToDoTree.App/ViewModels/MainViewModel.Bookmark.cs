using System.Windows.Input;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public ICommand SetBookmarkCommand { get; private set; } = null!;
    public ICommand ClearBookmarkCommand { get; private set; } = null!;
    public ICommand ResumeBookmarkCommand { get; private set; } = null!;

    public bool HasBookmark => _project.Bookmark is { } bookmark && _byId.ContainsKey(bookmark.NodeId);
    public string BookmarkTitle => HasBookmark ? _byId[_project.Bookmark!.NodeId].DisplayTitle : "しおりはありません";

    public string BookmarkNote
    {
        get => _project.Bookmark?.Note ?? string.Empty;
        set
        {
            if (_project.Bookmark is not { } bookmark || bookmark.Note == value) return;
            PushUndo($"bookmarkNote:{bookmark.NodeId}");
            bookmark.Note = value ?? string.Empty;
            MarkDirty();
            OnPropertyChanged();
        }
    }

    private void InitializeBookmark()
    {
        SetBookmarkCommand = new RelayCommand(() =>
        {
            if (IsProcedure) return;
            if (SelectedNode is not { } node || _project.Bookmark?.NodeId == node.Id) return;
            PushUndo();
            _project.Bookmark = new WorkBookmark { NodeId = node.Id };
            MarkDirty();
            RefreshAll();
            StatusMessage = $"「{node.DisplayTitle}」にしおりを置きました。再開メモを残せます。";
        }, () => !IsProcedure && SelectedNode is not null && _project.Bookmark?.NodeId != SelectedNode.Id);
        ClearBookmarkCommand = new RelayCommand(() =>
        {
            if (!HasBookmark) return;
            PushUndo();
            _project.Bookmark = null;
            MarkDirty();
            RefreshAll();
            StatusMessage = "作業のしおりを外しました。";
        }, () => HasBookmark);
        ResumeBookmarkCommand = new RelayCommand(() => ResumeBookmark(), () => HasBookmark);
    }

    /// <summary>非表示のカードも確実に見えるようにして、前後の筋とともに選択する。</summary>
    public bool ResumeBookmark()
    {
        if (!HasBookmark) return false;
        var node = _byId[_project.Bookmark!.NodeId];
        if (_focusedBlockId is not null) ToggleFocus();
        _collapsed.Clear();
        _focusId = null;
        HideCompleted = false;
        SearchText = string.Empty;
        SelectedTag = null;
        SelectedNode = node;
        RefreshVisibility();
        NotifyVisualsChanged();
        CenterOnRequested?.Invoke(this, node);
        StatusMessage = $"「{node.DisplayTitle}」から再開できます。右側のしおりに再開メモを表示しています。";
        return true;
    }

    internal void RefreshBookmark()
    {
        OnPropertyChanged(nameof(HasBookmark), nameof(BookmarkTitle), nameof(BookmarkNote));
        CommandManager.InvalidateRequerySuggested();
    }
}
