using System.Windows;
using System.Windows.Input;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Controls;

public partial class GraphView
{
    private (Guid Id, BlockBounds Bounds)[] _membershipTargets = [];
    private Point _nodeDragOrigin;

    private void OnBlockFocusChanged(object? sender, bool entering)
    {
        if (sender is not MainViewModel vm) return;
        if (entering) vm.BlockFocusViewport = (PanTransform.X, PanTransform.Y, ZoomTransform.ScaleX);
        else if (vm.BlockFocusViewport is { } saved)
        {
            PanTransform.X = saved.X; PanTransform.Y = saved.Y;
            ZoomTransform.ScaleX = ZoomTransform.ScaleY = saved.Zoom;
            vm.BlockFocusViewport = null;
            SyncMiniMap();
        }
    }

    private Guid? MembershipTarget(Point world) => _membershipTargets
        .Where(t => t.Bounds.Contains(world.X, world.Y))
        .OrderBy(t => t.Bounds.Width * t.Bounds.Height)
        .Select(t => (Guid?)t.Id).FirstOrDefault();

    private void UpdateMembershipPreview(Point world)
    {
        if (_viewModel is null) return;
        var transfer = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var target = transfer ? MembershipTarget(world) : null;
        foreach (var block in _viewModel.Blocks) block.IsDropTarget = target == block.Id;
        if (transfer) _viewModel.StatusMessage = target is { } id
            ? $"離すと「{_viewModel.Blocks.First(b => b.Id == id).Title}」へ所属を変更します（Shiftを離すと位置だけ移動）。"
            : "離すとブロックから外します（Shiftを離すと位置だけ移動）。";
        else _viewModel.StatusMessage = CardDragHint;
    }

    private void ClearMembershipPreview()
    {
        if (_viewModel is not null) foreach (var block in _viewModel.Blocks) block.IsDropTarget = false;
        _membershipTargets = [];
    }

    private NodeViewModel? ConnectionTarget(DependencyObject? hit, Point world) =>
        _viewModel?.FindBlockPortAt(new Vec2(world.X, world.Y), PortHitTolerance)?.Block.ConnectionNode
        ?? FindNodeElement(hit)?.DataContext as NodeViewModel
        ?? (FindBlockElement(hit)?.DataContext as BlockViewModel)?.ConnectionNode
        ?? _viewModel?.Blocks.Where(b => b.IsVisible && b.Bounds.Contains(world.X, world.Y))
            .OrderBy(b => b.Width * b.Height).FirstOrDefault()?.ConnectionNode;

    private void StartBlockConnection(BlockViewModel block, ConnectionSide side, Point world)
    {
        _connectSource = block.ConnectionNode;
        _connectSide = side;
        IsConnectionDragging = true;
        UpdatePreviewLine(world);
        Viewport.CaptureMouse();
    }
}
