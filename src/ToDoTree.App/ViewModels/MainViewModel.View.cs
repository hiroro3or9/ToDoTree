using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Graph;

namespace ToDoTree.App.ViewModels;

/// <summary>数が増えても迷わないための表示制御：折りたたみ、絞り込み、タグ。</summary>
public sealed partial class MainViewModel
{
    private readonly HashSet<Guid> _collapsed = [];
    private Guid? _focusId;
    private string? _selectedTag;
    private int _hiddenCount;

    public ICommand ToggleFocusCommand { get; private set; } = null!;

    public ICommand ExpandAllCommand { get; private set; } = null!;

    public ICommand CollapseSelectedCommand { get; private set; } = null!;

    public ICommand ClearTagCommand { get; private set; } = null!;

    /// <summary>プロジェクトで使われているタグ。</summary>
    public ObservableCollection<string> AvailableTags { get; } = [];

    public string? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (!SetProperty(ref _selectedTag, value))
            {
                return;
            }

            UpdateHighlights();
            RefreshSidebar();
            NotifyVisualsChanged();
            StatusMessage = string.IsNullOrEmpty(value) ? "タグの絞り込みを解除しました。" : $"#{value} で絞り込みました。";
        }
    }

    public bool IsFocusMode => _focusId is not null;

    public string FocusLabel => IsFocusMode ? "絞り込み解除" : "この筋だけ";

    /// <summary>いくつ隠れているか。</summary>
    public string HiddenSummary => _hiddenCount > 0 ? $"{_hiddenCount} 件を隠しています" : string.Empty;

    public bool HasHidden => _hiddenCount > 0;

    private void InitializeView()
    {
        ClearTagCommand = new RelayCommand(() => SelectedTag = null, () => !string.IsNullOrEmpty(SelectedTag));
        ToggleFocusCommand = new RelayCommand(ToggleFocus, () => IsFocusMode || SelectedNode is not null);
        ExpandAllCommand = new RelayCommand(ExpandAll, () => _collapsed.Count > 0 || IsFocusMode);
        CollapseSelectedCommand = new RelayCommand(
            () =>
            {
                if (SelectedNode is { } node)
                {
                    ToggleCollapse(node);
                }
            },
            () => SelectedNode is { CanCollapse: true });
    }

    /// <summary>その先を畳む / 開く。</summary>
    public void ToggleCollapse(NodeViewModel node)
    {
        if (!_collapsed.Remove(node.Id))
        {
            _collapsed.Add(node.Id);
        }

        RefreshVisibility();
        NotifyVisualsChanged();

        StatusMessage = _collapsed.Contains(node.Id)
            ? $"「{node.Title}」の先を畳みました（{node.HiddenCount} 件）。"
            : $"「{node.Title}」の先を開きました。";
    }

    /// <summary>選択中のステップに関係する筋だけを残す / 解除する。</summary>
    public void ToggleFocus()
    {
        if (IsFocusMode)
        {
            _focusId = null;
            StatusMessage = "絞り込みを解除しました。";
        }
        else if (SelectedNode is { } node)
        {
            _focusId = node.Id;
            StatusMessage = $"「{node.Title}」に関わる筋だけを表示しています。";
        }

        RefreshVisibility();
        NotifyVisualsChanged();
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ExpandAll()
    {
        _collapsed.Clear();
        _focusId = null;
        RefreshVisibility();
        NotifyVisualsChanged();
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
        StatusMessage = "すべて表示しました。";
    }

    /// <summary>いま何を見せるかを計算し直す。</summary>
    internal void RefreshVisibility()
    {
        if (_focusId is { } focus && !_graph.Contains(focus))
        {
            _focusId = null;
        }

        _collapsed.RemoveWhere(id => !_graph.Contains(id));

        var result = VisibilityService.Compute(_graph, new VisibilityOptions
        {
            Collapsed = _collapsed,
            FocusId = _focusId,
            HideCompleted = HideCompleted,
        });

        _hiddenCount = 0;
        foreach (var node in Nodes)
        {
            node.IsVisible = result.IsVisible(node.Id);
            node.IsCollapsed = _collapsed.Contains(node.Id);
            node.HiddenCount = result.HiddenBehind(node.Id);

            if (!node.IsVisible)
            {
                _hiddenCount++;
            }
        }

        OnPropertyChanged(nameof(IsFocusMode));
        OnPropertyChanged(nameof(FocusLabel));
        OnPropertyChanged(nameof(HiddenSummary));
        OnPropertyChanged(nameof(HasHidden));
    }

    /// <summary>タグ一覧を作り直す（選択中のタグは、まだあるなら保つ）。</summary>
    private void RefreshTags()
    {
        var tags = Nodes
            .SelectMany(n => n.Model.Tags)
            .Distinct()
            .OrderBy(t => t, StringComparer.CurrentCulture)
            .ToList();

        for (var i = AvailableTags.Count - 1; i >= 0; i--)
        {
            if (!tags.Contains(AvailableTags[i]))
            {
                AvailableTags.RemoveAt(i);
            }
        }

        for (var i = 0; i < tags.Count; i++)
        {
            if (!AvailableTags.Contains(tags[i]))
            {
                AvailableTags.Insert(Math.Min(i, AvailableTags.Count), tags[i]);
            }
        }
    }
}
