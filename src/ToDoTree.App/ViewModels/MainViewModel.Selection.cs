using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>選択まわり：複数選択、線の選択、キーボードでの接続、カード上での編集。</summary>
public sealed partial class MainViewModel
{
    private readonly HashSet<Guid> _selection = [];
    private Guid? _connectSourceId;

    public ICommand StartConnectCommand { get; private set; } = null!;

    public ICommand SelectBranchCommand { get; private set; } = null!;

    /// <summary>いま選ばれているステップ（複数可）。</summary>
    public IReadOnlyList<NodeViewModel> SelectedNodes =>
        [.. Nodes.Where(n => _selection.Contains(n.Id))];

    public int SelectionCount => _selection.Count;

    public bool HasMultipleSelected => _selection.Count > 1;

    public string SelectionSummary => _selection.Count > 1 ? $"{_selection.Count} 件を選択中" : string.Empty;

    /// <summary>クリックで選ばれている線。</summary>
    public EdgeViewModel? SelectedEdge { get; private set; }

    public bool HasSelectedEdge => SelectedEdge is not null;

    /// <summary>キーボードでの接続中（相手を選んでいる最中）。</summary>
    public bool IsConnecting => _connectSourceId is not null;

    public NodeViewModel? ConnectSource =>
        _connectSourceId is { } id && _byId.TryGetValue(id, out var vm) ? vm : null;

    private void InitializeSelection()
    {
        StartConnectCommand = new RelayCommand(StartKeyboardConnect, () => SelectedNode is not null);
        SelectBranchCommand = new RelayCommand(SelectBranch, () => SelectedNode is not null);
    }

    // ---- ステップの選択 ----

    /// <summary>1 つだけ選び直す。</summary>
    public void SelectOnly(NodeViewModel? node)
    {
        _selection.Clear();
        if (node is not null)
        {
            _selection.Add(node.Id);
        }

        ApplySelection(node);
    }

    /// <summary>Ctrl+クリック：選択に足す / 外す。</summary>
    public void ToggleSelection(NodeViewModel node)
    {
        if (_selection.Add(node.Id))
        {
            ApplySelection(node);
            return;
        }

        _selection.Remove(node.Id);
        ApplySelection(Nodes.FirstOrDefault(n => _selection.Contains(n.Id)));
    }

    /// <summary>まとめて選ぶ（矩形選択・枝の選択・全選択）。</summary>
    public void SelectNodes(IEnumerable<NodeViewModel> nodes, NodeViewModel? primary = null)
    {
        _selection.Clear();
        NodeViewModel? last = null;

        foreach (var node in nodes)
        {
            _selection.Add(node.Id);
            last = node;
        }

        ApplySelection(primary ?? last);
    }

    public void SelectAllNodes()
    {
        SelectNodes(Nodes, SelectedNode);
        StatusMessage = $"{_selection.Count} 件すべてを選びました。";
    }

    /// <summary>選択中のステップと、その下流をまとめて選ぶ。</summary>
    public void SelectBranch()
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        var ids = new HashSet<Guid>(_graph.Descendants(node.Id)) { node.Id };
        SelectNodes(Nodes.Where(n => ids.Contains(n.Id)), node);
        StatusMessage = $"この枝の {ids.Count} 件を選びました。";
    }

    public bool IsSelected(NodeViewModel node) => _selection.Contains(node.Id);

    private void ApplySelection(NodeViewModel? primary)
    {
        ClearEdgeSelection();

        foreach (var node in Nodes)
        {
            node.IsSelected = _selection.Contains(node.Id);

            if (node.IsEditing && !node.IsSelected)
            {
                node.IsEditing = false;
            }
        }

        _selectedNode = primary;

        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasMultipleSelected));
        OnPropertyChanged(nameof(SelectionSummary));

        UpdateHighlights();
        NotifyVisualsChanged();
    }

    // ---- 線の選択 ----

    public void SelectEdge(EdgeViewModel? edge)
    {
        ClearEdgeSelection();

        if (edge is not null)
        {
            edge.IsSelected = true;
            SelectedEdge = edge;

            _selection.Clear();
            foreach (var node in Nodes)
            {
                node.IsSelected = false;
                node.IsEditing = false;
            }

            _selectedNode = null;

            OnPropertyChanged(nameof(SelectedNode));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionCount));
            OnPropertyChanged(nameof(HasMultipleSelected));
            OnPropertyChanged(nameof(SelectionSummary));
            UpdateHighlights();

            StatusMessage = $"「{edge.From.Title}」→「{edge.To.Title}」を選びました。Delete で外せます。";
        }

        OnPropertyChanged(nameof(SelectedEdge));
        OnPropertyChanged(nameof(HasSelectedEdge));
        NotifyVisualsChanged();
    }

    private void ClearEdgeSelection()
    {
        if (SelectedEdge is null)
        {
            return;
        }

        SelectedEdge.IsSelected = false;
        SelectedEdge = null;
        OnPropertyChanged(nameof(SelectedEdge));
        OnPropertyChanged(nameof(HasSelectedEdge));
    }

    /// <summary>キャンバス上の座標に線があれば返す。</summary>
    public EdgeViewModel? FindEdgeAt(double x, double y, double tolerance)
    {
        var point = new Vec2(x, y);
        EdgeViewModel? best = null;
        var bestDistance = tolerance;

        foreach (var edge in Edges)
        {
            var (start, end) = CurveGeometry.Anchors(
                new Vec2(edge.From.X, edge.From.Y),
                new Vec2(edge.To.X, edge.To.Y),
                NodeViewModel.CardWidth,
                NodeViewModel.CardHeight);

            var (control1, control2) = CurveGeometry.ControlPoints(start, end);
            var distance = CurveGeometry.DistanceToCurve(point, start, control1, control2, end);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = edge;
            }
        }

        return best;
    }

    public void DeleteSelectedEdge()
    {
        if (SelectedEdge is not { } edge)
        {
            return;
        }

        PushUndo();
        _graph.Disconnect(edge.Model.Id);
        ClearEdgeSelection();
        RebuildEdges();
        MarkDirty();
        RefreshAll();
        StatusMessage = "繋がりを外しました。Ctrl+Z で戻せます。";
    }

    // ---- キーボードでの接続 ----

    public void StartKeyboardConnect()
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        _connectSourceId = node.Id;
        OnPropertyChanged(nameof(IsConnecting));
        StatusMessage = $"「{node.Title}」から繋ぎます。矢印キーで相手を選んで Enter（Esc で取り消し）。";
        NotifyVisualsChanged();
    }

    public void CompleteKeyboardConnect()
    {
        if (_connectSourceId is not { } from)
        {
            return;
        }

        _connectSourceId = null;
        OnPropertyChanged(nameof(IsConnecting));

        if (SelectedNode is { } target)
        {
            TryConnect(from, target.Id);
        }

        NotifyVisualsChanged();
    }

    public void CancelKeyboardConnect()
    {
        if (_connectSourceId is null)
        {
            return;
        }

        _connectSourceId = null;
        OnPropertyChanged(nameof(IsConnecting));
        StatusMessage = "接続をやめました。";
        NotifyVisualsChanged();
    }

    // ---- カード上での編集 ----

    /// <summary>そのステップの名前を、カードの上で書き換え始める。</summary>
    public void BeginEdit(NodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null)
        {
            return;
        }

        if (!_selection.Contains(node.Id))
        {
            SelectOnly(node);
        }

        foreach (var other in Nodes)
        {
            other.IsEditing = ReferenceEquals(other, node);
        }
    }

    public void EndEdit()
    {
        foreach (var node in Nodes)
        {
            node.IsEditing = false;
        }
    }

    /// <summary>選択中のステップの状態をまとめて変える。</summary>
    public void SetStatusOfSelection(NodeStatus status)
    {
        var targets = SelectedNodes;
        if (targets.Count == 0)
        {
            return;
        }

        PushUndo();

        foreach (var node in targets)
        {
            node.Model.Status = status;
            node.Model.CompletedAt = status == NodeStatus.Done ? DateTimeOffset.Now : null;
            node.Model.UpdatedAt = DateTimeOffset.Now;
        }

        MarkDirty();
        RefreshAll();

        if (status == NodeStatus.Done && targets.Count == 1)
        {
            AnnounceUnlocked(targets[0]);
        }
    }
}
