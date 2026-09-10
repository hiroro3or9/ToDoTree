using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    private Guid? _selectedBlockPortId;
    private (BlockViewModel Block, BlockPort Original)? _portDrag;
    public BlockPort? SelectedBlockPort => SelectedBlock?.Model.Ports.FirstOrDefault(p => p.Id == _selectedBlockPortId);

    private ICommand? _addBlockPortCommand, _removeBlockPortCommand;
    public ICommand AddBlockPortCommand => _addBlockPortCommand ??= new RelayCommand(
        () => AddBlockPort(SelectedBlock!, new Vec2(_menuX, _menuY)), () => SelectedBlock is not null && !IsBlockEditing);
    public ICommand RemoveBlockPortCommand => _removeBlockPortCommand ??= new RelayCommand(
        RemoveBlockPort, () => SelectedBlockPort is not null && !IsBlockEditing);

    public (BlockViewModel Block, BlockPort Port)? FindBlockPortAt(Vec2 point, double tolerance)
    {
        return Blocks.Where(b => b.IsVisible).SelectMany(b => b.Model.Ports.Select(p =>
                (Block: b, Port: p, Distance: (p.Resolve(b.Bounds) - point).Length)))
            .Where(p => p.Distance <= tolerance).OrderBy(p => p.Distance).ThenByDescending(p => p.Block.Depth)
            .ThenByDescending(p => p.Block.IsSelected)
            .Select(p => ((BlockViewModel Block, BlockPort Port)?)(p.Block, p.Port)).FirstOrDefault();
    }

    public BlockViewModel? FindBlockBorderAt(Vec2 point, double tolerance) => Blocks
        .Where(b => b.IsVisible && (BlockPort.At(b.Bounds, point).Resolve(b.Bounds) - point).Length <= tolerance)
        .OrderByDescending(b => b.Depth).ThenByDescending(b => b.IsSelected)
        .ThenBy(b => b.Width * b.Height).FirstOrDefault();

    public void SelectBlockPort(BlockViewModel block, Guid portId)
    {
        SelectBlock(block);
        _selectedBlockPortId = portId;
        OnPropertyChanged(nameof(SelectedBlockPort));
        StatusMessage = "接続点からドラッグで接続、Alt＋ドラッグで枠上を移動、Deleteで点を削除できます。";
        NotifyVisualsChanged();
    }

    public void AddBlockPort(BlockViewModel block, Vec2 point)
    {
        if (IsBlockEditing || !Blocks.Contains(block) || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        var port = BlockPort.At(block.Bounds, point);
        var existing = block.Model.Ports.FirstOrDefault(p => (p.Resolve(block.Bounds) - port.Resolve(block.Bounds)).Length < 8);
        if (existing is not null) { SelectBlockPort(block, existing.Id); return; }
        PushUndo();
        block.Model.Ports.Add(port);
        SelectBlockPort(block, port.Id);
        MarkDirty();
    }

    public void RemoveBlockPort()
    {
        if (IsBlockEditing || SelectedBlock is not { } block || SelectedBlockPort is not { } port) return;
        PushUndo();
        foreach (var edge in _project.Edges)
        {
            if (edge.FromId == block.Id && edge.FromPortId == port.Id) { edge.FromPortId = null; edge.FromSide = port.Side; }
            if (edge.ToId == block.Id && edge.ToPortId == port.Id) { edge.ToPortId = null; edge.ToSide = port.Side; }
        }
        block.Model.Ports.Remove(port);
        _selectedBlockPortId = null;
        MarkDirty();
        NotifyVisualsChanged();
        StatusMessage = "接続点を削除しました。線は同じ辺の標準位置に繋ぎ直しました。Ctrl+Zで戻せます。";
    }

    public bool BeginBlockPortDrag()
    {
        if (IsBlockEditing || SelectedBlock is not { } block || SelectedBlockPort is not { } port) return false;
        BeginTransaction();
        _portDrag = (block, port);
        return true;
    }

    public void MoveBlockPort(Vec2 point)
    {
        if (_portDrag is not { } drag || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        var index = drag.Block.Model.Ports.FindIndex(p => p.Id == drag.Original.Id);
        if (index < 0) return;
        drag.Block.Model.Ports[index] = BlockPort.At(drag.Block.Bounds, point, drag.Original.Id);
        NotifyVisualsChanged();
    }

    public void EndBlockPortDrag(bool commit)
    {
        if (_portDrag is not { } drag) return;
        _portDrag = null;
        if (commit) CommitTransaction();
        else
        {
            var index = drag.Block.Model.Ports.FindIndex(p => p.Id == drag.Original.Id);
            if (index >= 0) drag.Block.Model.Ports[index] = drag.Original;
            CancelTransaction();
        }
        NotifyVisualsChanged();
    }

    public sealed record PortChoice(string Title, ICommand Command);
    public IReadOnlyList<PortChoice> FromPortChoices => PortChoices(true);
    public IReadOnlyList<PortChoice> ToPortChoices => PortChoices(false);

    private IReadOnlyList<PortChoice> PortChoices(bool source)
    {
        if (SelectedEdge is not { IsAggregated: false } edge) return [];
        var endpoint = source ? edge.Model.FromId : edge.Model.ToId;
        if (_project.Blocks.FirstOrDefault(b => b.Id == endpoint) is not { } block) return [];
        List<PortChoice> choices = [new("標準位置", new RelayCommand(() => SetEdgePort(edge, source, null)))];
        choices.AddRange(block.Ports.Select((p, i) => new PortChoice(
            $"接続点 {i + 1}（{SideName(p.Side)}・{p.Position:P0}）", new RelayCommand(() => SetEdgePort(edge, source, p.Id)))));
        return choices;
    }

    private static string SideName(ConnectionSide side) => side switch
    {
        ConnectionSide.Top => "上", ConnectionSide.Bottom => "下", ConnectionSide.Left => "左", _ => "右",
    };

    public void SetEdgePort(EdgeViewModel edge, bool source, Guid? portId)
    {
        if (IsBlockEditing || edge.IsAggregated || !Edges.Contains(edge)) return;
        var endpoint = source ? edge.Model.FromId : edge.Model.ToId;
        var port = BlockConnections.FindPort(_project, endpoint, portId);
        if (portId is not null && port is null || (source ? edge.Model.FromPortId : edge.Model.ToPortId) == portId) return;
        PushUndo();
        if (source) { edge.Model.FromPortId = portId; edge.Model.FromSide = port?.Side ?? edge.Model.FromSide; }
        else { edge.Model.ToPortId = portId; edge.Model.ToSide = port?.Side ?? edge.Model.ToSide; }
        MarkDirty();
        NotifyVisualsChanged();
    }
}
