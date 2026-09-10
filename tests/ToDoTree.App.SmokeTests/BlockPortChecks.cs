using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyBlockPorts()
    {
        var nodes = new[] { new TodoNode { Title = "設計", X = 120, Y = 180 },
            new TodoNode { Title = "レビュー", X = 120, Y = 360 },
            new TodoNode { Title = "実装", X = 680, Y = 180 },
            new TodoNode { Title = "動作確認", X = 680, Y = 360 } };
        var project = new TodoProject { Nodes = [.. nodes] };
        var aId = BlockService.Create(project, [nodes[0].Id, nodes[1].Id], "設計ブロック").Block!.Id;
        var bId = BlockService.Create(project, [nodes[2].Id, nodes[3].Id], "実装ブロック").Block!.Id;
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null, AppContext.BaseDirectory);
        var view = new GraphView { DataContext = vm, Width = 1280, Height = 720 };
        var host = new Window { Content = view, Width = 1280, Height = 720, Opacity = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        host.Show();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        BlockViewModel A() => vm.Blocks.Single(b => b.Id == aId);
        BlockViewModel B() => vm.Blocks.Single(b => b.Id == bId);
        EdgeViewModel E() => vm.Edges.Single(e => e.Model.FromId == aId && e.Model.ToId == bId);

        // 枠の当たり判定と追加メニュー。後の操作は、Undoで作り直されたVMを毎回取得する。
        var point = new Vec2(A().Bounds.Right, A().Y + A().Height * 0.25);
        Check(vm.FindBlockBorderAt(point, 6)?.Id == aId, "The expanded body border can be targeted for adding a port.");
        vm.SelectBlock(A()); vm.SetMenuAnchor(point.X, point.Y);
        var menu = (ContextMenu)view.FindResource("BlockMenu"); menu.DataContext = vm;
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var add = menu.Items.OfType<MenuItem>().Single(m => m.Header is "ここに接続点を追加");
        Check(add.Command?.CanExecute(null) == true, "The block context menu binds its add-port command.");
        add.Command!.Execute(null);
        var aPort = A().Model.Ports.Single().Id;
        Check(vm.FindBlockPortAt(point, 6)?.Port.Id == aPort, "A custom port is hit at its rendered position.");
        var addUndo = History(vm, "_undo");
        vm.AddBlockPort(A(), point);
        Check(History(vm, "_undo") == addUndo && A().Model.Ports.Count == 1, "Adding at an existing port does not duplicate it or add history.");
        vm.Undo(); Check(A().Model.Ports.Count == 0, "Undo removes the added port.");
        vm.Redo(); Check(A().Model.Ports.Single().Id == aPort, "Redo restores the port with the same ID.");
        vm.AddBlockPort(B(), new(B().X, B().Y + B().Height * 0.75));
        var bPort = B().Model.Ports.Single().Id;
        Check(vm.TryConnect(aId, bId, fromPortId: aPort, toPortId: bPort), "Custom ports connect two blocks.");
        Check(E().GetRoute(vm.Nodes)[0] == A().Model.Ports[0].Resolve(A().Bounds)
            && E().GetRoute(vm.Nodes)[^1] == B().Model.Ports[0].Resolve(B().Bounds), "The displayed route lands exactly on both custom ports.");
        var startPortPosition = A().Model.Ports[0].Resolve(A().Bounds);
        Check((bool)typeof(GraphView).GetMethod("TryPressBlockPort", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [new Point(startPortPosition.X, startPortPosition.Y)])!, "Pressing the rendered port starts the WPF connection gesture.");
        Check((Guid?)typeof(GraphView).GetField("_connectPortId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view) == aPort,
            "The gesture remembers the custom source port.");
        var targetPosition = B().Model.Ports[0].Resolve(B().Bounds);
        var target = (NodeViewModel?)typeof(GraphView).GetMethod("ConnectionTarget", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, [null, new Point(targetPosition.X, targetPosition.Y)]);
        Check(target?.Id == bId, "A target port is hit even outside the block's visual bounds.");
        typeof(GraphView).GetMethod("EndInteraction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);
        Check(typeof(GraphView).GetField("_connectPortId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view) is null,
            "Cancelling connection clears the custom source port.");
        var undo = History(vm, "_undo");
        Check(!vm.TryConnect(bId, aId, fromPortId: bPort, toPortId: aPort) && History(vm, "_undo") == undo,
            "Port connections preserve cycle rejection without changing history.");

        vm.SelectBlockPort(A(), aPort);
        var before = State(vm);
        Check(vm.BeginBlockPortDrag() && vm.IsBlockEditing, "Port drag defers auto-save through the edit transaction.");
        vm.MoveBlockPort(new(A().Bounds.Right, A().Y + A().Height * 0.65));
        vm.EndBlockPortDrag(true);
        Check(History(vm, "_undo") == undo + 1, "An entire port drag is one undo step.");
        Check(E().GetRoute(vm.Nodes)[0] == A().Model.Ports[0].Resolve(A().Bounds), "The line follows a moved port immediately.");
        var moved = State(vm);
        vm.Undo(); Check(State(vm) == before, "Undo restores port position and edge references.");
        vm.SelectBlockPort(A(), aPort);
        var redo = History(vm, "_redo"); var dirty = vm.IsDirty;
        vm.BeginBlockPortDrag(); vm.MoveBlockPort(new(A().X + A().Width * 0.3, A().Y)); vm.EndBlockPortDrag(false);
        Check(State(vm) == before && History(vm, "_redo") == redo && vm.IsDirty == dirty && !vm.IsBlockEditing,
            "Cancellation restores port data, dirty state and the redo stack.");
        vm.BeginBlockPortDrag(); vm.EndBlockPortDrag(true);
        Check(History(vm, "_redo") == redo, "A click without movement preserves redo.");
        vm.Redo(); Check(State(vm) == moved, "Redo restores a committed port drag after a cancelled gesture.");

        void BeginCancelledGesture()
        {
            vm.SelectBlockPort(A(), aPort); vm.BeginBlockPortDrag();
            vm.MoveBlockPort(new(A().X + A().Width * 0.8, A().Y));
            typeof(GraphView).GetField("_draggingBlockPort", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, true);
        }
        BeginCancelledGesture();
        var viewport = (FrameworkElement)view.FindName("Viewport");
        viewport.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view), Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.KeyDownEvent });
        Check(State(vm) == moved && !vm.IsBlockEditing, "Escape cancels the WPF port gesture.");
        BeginCancelledGesture();
        viewport.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });
        Check(State(vm) == moved && !vm.IsBlockEditing, "Capture loss cancels the WPF port gesture.");
        BeginCancelledGesture();
        view.DataContext = null;
        Check(State(vm) == moved && !vm.IsBlockEditing, "Switching away from the document cancels port movement.");
        view.DataContext = vm;

        vm.SelectEdge(E());
        Check(vm.FromPortChoices.Count == 2 && vm.ToPortChoices.Count == 2, "Existing edges offer the endpoint block's ports.");
        vm.FromPortChoices[0].Command.Execute(null);
        Check(E().Model.FromPortId is null && E().Model.ToPortId == bPort, "Rebinding the source leaves the destination intact.");
        vm.FromPortChoices[1].Command.Execute(null);
        Check(E().Model.FromPortId == aPort, "An existing line can be attached to a custom port.");
        vm.SelectBlockPort(A(), aPort);
        var beforeDelete = State(vm);
        vm.RemoveBlockPort();
        Check(A().Model.Ports.Count == 0 && E().Model.FromPortId is null && vm.Edges.Count == 1,
            "Deleting a port preserves the dependency and falls back to the standard side.");
        vm.Undo(); Check(State(vm) == beforeDelete, "Undo restores a deleted port and all its connections.");

        var beforeBlockMove = State(vm);
        var oldPortPosition = A().Model.Ports[0].Resolve(A().Bounds);
        Check(vm.BeginBlockDrag(A()), "A block with custom ports can be moved.");
        vm.UpdateBlockDrag(50, 35); vm.CommitBlockDrag();
        Check(A().Model.Ports[0].Resolve(A().Bounds) == oldPortPosition + new Vec2(50, 35)
            && E().GetRoute(vm.Nodes)[0] == A().Model.Ports[0].Resolve(A().Bounds), "Moving a block carries its ports and connected lines.");
        vm.Undo(); Check(State(vm) == beforeBlockMove, "Undo restores the entire block move without altering relative port positions.");

        vm.ToggleBlockCollapse(A()); vm.ToggleBlockCollapse(B());
        Check(E().GetRoute(vm.Nodes)[0] == A().Model.Ports[0].Resolve(A().Bounds)
            && E().GetRoute(vm.Nodes)[^1] == B().Model.Ports[0].Resolve(B().Bounds), "Collapsed blocks use the same proportional port positions.");
        Render(view, "ports-collapsed-light");
        vm.ToggleBlockCollapse(A()); vm.ToggleBlockCollapse(B());
        vm.SelectEdge(E()); vm.AddWaypoint(new(530, 280));
        Check(E().GetRoute(vm.Nodes).Contains(new Vec2(530, 280)), "Custom endpoints and manual waypoints work together.");
        vm.SelectBlockPort(A(), aPort);
        Render(view, "ports-expanded-light");
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll(); Render(view, "ports-expanded-dark");
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();
        Check(BlockConnections.Validate(vm.Graph.Project) is null, "All UI edits leave valid stored data.");
        host.Close();
    }
}
