using System.Windows;
using System.Windows.Input;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Controls;

public partial class GraphView
{
    private Guid? _connectPortId;
    private bool _draggingBlockPort;

    private double PortHitTolerance => 9 / Math.Max(0.2, ZoomTransform.ScaleX);

    private bool TryPressBlockPort(Point world)
    {
        if (_viewModel?.FindBlockPortAt(new Vec2(world.X, world.Y), PortHitTolerance) is not { } hit) return false;
        Viewport.Focus();
        _viewModel.SelectBlockPort(hit.Block, hit.Port.Id);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _draggingBlockPort = _viewModel.BeginBlockPortDrag();
            Viewport.CaptureMouse();
        }
        else
        {
            _connectPortId = hit.Port.Id;
            StartBlockConnection(hit.Block, hit.Port.Side, world);
        }
        return true;
    }

    private (Guid? Id, ConnectionSide Side) TargetPort(Point world, Guid targetId, ConnectionSide fallback)
    {
        var hit = _viewModel?.FindBlockPortAt(new Vec2(world.X, world.Y), PortHitTolerance);
        return hit is { } p && p.Block.Id == targetId ? (p.Port.Id, p.Port.Side) : (null, fallback);
    }

    private void CancelBlockPortDrag()
    {
        _draggingBlockPort = false;
        _viewModel?.EndBlockPortDrag(commit: false);
    }
}
