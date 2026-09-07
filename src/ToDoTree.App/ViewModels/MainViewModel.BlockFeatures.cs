using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    private Guid? _focusedBlockId;
    private IReadOnlySet<Guid> _baseVisible = new HashSet<Guid>();
    private bool _nodeDragActive;
    public (double X, double Y, double Zoom)? BlockFocusViewport { get; set; }
    public Guid? FocusedBlockId => _focusedBlockId;
    public bool IsNodeDragging => _nodeDragActive;
    public event EventHandler<bool>? BlockFocusChanged;
    private ICommand? _collapseBlockCommand, _focusBlockCommand;
    public ICommand CollapseBlockCommand => _collapseBlockCommand ??= new RelayCommand(
        () => { if (SelectedBlock is { } block) ToggleBlockCollapse(block); }, () => HasSelectedBlock);
    public ICommand FocusBlockCommand => _focusBlockCommand ??= new RelayCommand(
        () => { if (SelectedBlock is { } block) FocusBlock(block); }, () => HasSelectedBlock);

    public void ToggleBlockCollapse(BlockViewModel block)
    {
        CommitPendingBlockEdit();
        PushUndo();
        block.Model.IsCollapsed = !block.Model.IsCollapsed;
        MarkDirty();
        RefreshAll();
        StatusMessage = block.IsCollapsed ? $"「{block.Title}」を畳みました。破線は内部ステップへの個別接続です。"
            : $"「{block.Title}」を開きました。";
    }

    public void FocusBlock(BlockViewModel block)
    {
        if (_focusedBlockId == block.Id) { ToggleFocus(); return; }
        CommitPendingBlockEdit();
        var entering = _focusedBlockId is null;
        _focusedBlockId = block.Id;
        if (entering) BlockFocusChanged?.Invoke(this, true);
        RefreshAll();
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
        StatusMessage = $"「{block.Title}」の中と、直接つながる外部ステップを表示しています。絞り込み解除で元の表示へ戻れます。";
    }

    public NodeViewModel? EndpointNode(Guid id) => _byId.GetValueOrDefault(id)
        ?? _blockById.GetValueOrDefault(id)?.ConnectionNode;

    public (Guid Id, BlockBounds Bounds, bool Visible, bool IsBlock) DisplayEndpoint(Guid id)
    {
        var block = _blockById.GetValueOrDefault(id);
        if (block is not null) return (id, block.Bounds, block.IsVisible, true);
        if (!_byId.TryGetValue(id, out var node)) return (id, default, false, false);
        block = BlockOf(id);
        if (block is { IsCollapsed: true }) return (block.Id, block.Bounds, block.IsVisible && _baseVisible.Contains(id), true);
        return (id, new BlockBounds(node.X, node.Y, NodeViewModel.CardWidth, NodeViewModel.CardHeight), node.IsVisible, false);
    }

    public void BeginNodeDrag()
    {
        BeginTransaction();
        _nodeDragActive = true;
    }

    public void UpdateNodeDragWaypoints(IReadOnlySet<Guid> moving, double dx, double dy)
    {
        if (_pendingSnapshot is null) return;
        foreach (var original in BlockGeometry.InternalEdges(_pendingSnapshot, moving))
        {
            var edge = _project.Edges.FirstOrDefault(e => e.Id == original.Id);
            if (edge is not null) edge.Waypoints = [.. original.Waypoints.Select(p => p with { X = p.X + dx, Y = p.Y + dy })];
        }
        NotifyVisualsChanged();
    }

    public void FinishNodeDrag(IReadOnlyList<Guid> ids, bool transfer, Guid? target)
    {
        if (!_nodeDragActive) return;
        _nodeDragActive = false;
        if (transfer && BlockService.Transfer(_project, ids, target) is { } error)
        {
            RollbackTransaction();
            StatusMessage = error + " 移動と所属変更を取り消しました。";
            return;
        }
        RebuildBlocks();
        RebuildEdges();
        CommitTransaction();
        RefreshAll();
        if (transfer) StatusMessage = target is { } id && _blockById.TryGetValue(id, out var block)
            ? $"「{block.Title}」へ移しました。Ctrl+Zで位置と所属を戻せます。"
            : "ブロックから外しました。Ctrl+Zで位置と所属を戻せます。";
    }

    public void CancelNodeDrag()
    {
        if (!_nodeDragActive) return;
        _nodeDragActive = false;
        RollbackTransaction();
        StatusMessage = "移動を取り消しました。";
    }
}
