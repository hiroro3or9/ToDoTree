using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static int _checks;
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var resource in new[] { "Palette.Light", "Controls", "Calendar", "Styles" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/{typeof(GraphView).Assembly.GetName().Name};component/Themes/{resource}.xaml", UriKind.Relative) });
            Verify();
            VerifyRepeat();
            VerifyLoopLayout();
            VerifyBlockPorts();
            VerifyBlockNesting();
            VerifyTaskDetails();
            VerifyProjectVariables();
            VerifyProcedures();
            Console.WriteLine($"WPF smoke checks: {_checks} passed. Renders: {Path.Combine(AppContext.BaseDirectory, "artifacts")}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static int History(MainViewModel vm, string field) =>
        ((ICollection)typeof(MainViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!).Count;
    private static string State(MainViewModel vm) => JsonSerializer.Serialize(vm.Graph.Project);

    private static void Verify()
    {
        TodoNode[] nodes = [
            new() { Title = "要件の承認", X = 35, Y = 195 },
            new() { Title = "画面設計", X = 360, Y = 150 },
            new() { Title = "設計レビュー", X = 360, Y = 320 },
            new() { Title = "画面の実装", X = 820, Y = 150 },
            new() { Title = "動作確認", X = 820, Y = 320 },
            new() { Title = "次回の改善案", X = 35, Y = 510 },
        ];
        var project = new TodoProject { Nodes = [.. nodes], Name = "ブロック操作の検証" };
        var aId = BlockService.Create(project, [nodes[1].Id, nodes[2].Id], "設計").Block!.Id;
        var bId = BlockService.Create(project, [nodes[3].Id, nodes[4].Id], "実装").Block!.Id;
        var graph = new TodoGraph(project);
        graph.Connect(nodes[0].Id, aId);
        var fullId = graph.Connect(aId, bId)!.Id;
        var individualId = graph.Connect(nodes[2].Id, nodes[3].Id)!.Id;
        graph.Connect(nodes[1].Id, nodes[2].Id);
        graph.Connect(nodes[3].Id, nodes[4].Id);
        var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts");
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null, artifacts);
        var view = new GraphView { DataContext = vm, Width = 1280, Height = 720 };
        view.Measure(new Size(1280, 720)); view.Arrange(new Rect(0, 0, 1280, 720)); view.UpdateLayout();
        var host = new Window { Content = view, Width = 1280, Height = 720, Opacity = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        host.Show();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var zoom = (ScaleTransform)view.FindName("ZoomTransform");
        var pan = (TranslateTransform)view.FindName("PanTransform");
        zoom.ScaleX = zoom.ScaleY = 1; pan.X = pan.Y = 24;
        BlockViewModel A() => vm.Blocks.Single(b => b.Id == aId);
        BlockViewModel B() => vm.Blocks.Single(b => b.Id == bId);
        NodeViewModel N(int i) => vm.Nodes.Single(n => n.Id == nodes[i].Id);
        Check(vm.Edges.Count == 5, "All stored block and node edges must have a view model.");
        Check(N(3).Readiness == Readiness.Blocked, "Block predecessor gates readiness in the UI.");
        Render(view, "expanded-light");
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll();
        Render(view, "expanded-dark");
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();

        Button CollapseButton() => Descendants(view).OfType<Button>()
            .Single(b => b.Tag is "block-collapse" && b.DataContext is BlockViewModel block && block.Id == aId);
        var beforeButton = State(vm);
        var buttonUndoCount = History(vm, "_undo");
        CollapseButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(A().IsCollapsed && vm.SelectedBlock?.Id == aId && History(vm, "_undo") == buttonUndoCount + 1,
            "The collapse button selects its block and folds exactly once through the standard Click event.");
        CollapseButton().RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view),
            Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(!A().IsCollapsed, "Enter on the button opens the block through WPF keyboard handling.");
        vm.Undo(); vm.Undo();
        Check(State(vm) == beforeButton, "Button actions preserve the existing undo behavior.");

        var positions = vm.Nodes.Select(n => (n.Id, n.X, n.Y)).ToArray();
        vm.SelectBlock(A()); vm.ToggleBlockCollapse(A());
        Check(A().IsVisible && A().IsCollapsed && A().Height == 48, "A collapsed block remains visible.");
        Check(!N(1).IsVisible && !N(2).IsVisible, "Collapsed members must be hidden.");
        Check(vm.Nodes.Select(n => (n.Id, n.X, n.Y)).SequenceEqual(positions), "Folding must preserve coordinates.");
        var full = vm.Edges.Single(e => e.Model.Id == fullId);
        var individual = vm.Edges.Single(e => e.Model.Id == individualId);
        Check(full.IsVisible && !full.IsAggregated && full.IsBlockConnection, "Full-block edges remain solid.");
        Check(individual.IsVisible && individual.IsAggregated, "Individual edges are projected when collapsed.");
        Check(vm.Edges.Count(e => e.IsVisible) == 4, "Internal edges disappear while collapsed.");
        Check(individual.GetRoute(vm.Nodes).Count >= 2, "A projected edge has a hit-testable route.");
        var beforeReveal = State(vm);
        vm.SelectEdge(individual); vm.DeleteSelectedEdge();
        Check(vm.Edges.Count == 5, "Projected lines cannot silently delete one of several overlapping edges.");
        vm.RevealEdgeCommand.Execute(null);
        Check(N(2).IsVisible && vm.SelectedEdge?.IsAggregated == false, "A projected line can reveal and select its original endpoints.");
        vm.Undo(); Check(State(vm) == beforeReveal, "Revealing projected endpoints is undoable.");
        vm.ToggleBlockCollapse(B());
        Check(full.GetRoute(vm.Nodes)[0] != individual.GetRoute(vm.Nodes)[0], "Full and projected edges use separate ports to stay distinguishable.");
        Render(view, "collapsed-light");
        var originalTitle = A().Title;
        A().Title = "とても長いブロック名でも進捗を隠さずに表示できることを確認する";
        Render(view, "collapsed-long-title-light");
        vm.BeginBlockRename(A());
        Render(view, "collapsed-editing-light");
        vm.EndBlockRename(commit: true);
        A().Title = originalTitle;
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll();
        Render(view, "collapsed-dark");
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();

        vm.SelectBlock(A());
        var beforeDrag = State(vm);
        Check(vm.BeginBlockDrag(A()), "Collapsed blocks can move with all their members.");
        vm.UpdateBlockDrag(32, 48); vm.CommitBlockDrag();
        Check(N(1).X == positions.Single(p => p.Id == N(1).Id).X + 32 && N(1).Y == positions.Single(p => p.Id == N(1).Id).Y + 48, "Collapsed members move by the requested delta.");
        vm.Undo(); Check(State(vm) == beforeDrag, "Undo restores collapsed block positions.");
        vm.Redo(); Check(State(vm) != beforeDrag, "Redo restores the block movement.");
        vm.Undo();

        zoom.ScaleX = zoom.ScaleY = 0.8; pan.X = 47; pan.Y = 63;
        var beforeFocus = State(vm);
        vm.FocusBlock(A());
        Check(!A().IsCollapsed && A().Model.IsCollapsed, "Focus temporarily opens the block without changing the file.");
        Check(N(1).IsVisible && N(2).IsVisible && N(0).IsVisible, "Focus includes members and direct predecessors.");
        Check(!N(5).IsVisible && B().IsVisible, "Focus hides unrelated nodes, retaining connected blocks.");
        Check(State(vm) == beforeFocus, "Focus does not mutate project data.");
        Render(view, "focused-light");
        vm.ToggleFocus();
        Check(A().IsCollapsed && N(5).IsVisible, "Leaving focus restores folding and visibility.");
        Check(zoom.ScaleX == 0.8 && pan.X == 47 && pan.Y == 63, "Leaving focus restores the viewport.");

        vm.SelectOnly(N(5));
        var beforeTransfer = State(vm);
        var undoCount = History(vm, "_undo");
        vm.BeginNodeDrag(); N(5).X += 50; N(5).Y += 70;
        Check(vm.IsBlockEditing, "Autosave pauses during a node drag transaction.");
        vm.FinishNodeDrag([N(5).Id], true, aId);
        Check(A().Model.NodeIds.Contains(N(5).Id), "Shift drop adds the member.");
        Check(History(vm, "_undo") == undoCount + 1, "Position and membership share a single undo.");
        vm.Undo(); Check(State(vm) == beforeTransfer, "Undo restores both membership and position.");
        var redoCount = History(vm, "_redo");
        vm.BeginNodeDrag(); N(5).X += 20; vm.CancelNodeDrag();
        Check(State(vm) == beforeTransfer && History(vm, "_redo") == redoCount, "Cancelled drags preserve state and redo.");
        vm.Redo(); Check(A().Model.NodeIds.Contains(N(5).Id), "Redo still applies the original transfer.");
        vm.Undo();

        var beforeFailure = State(vm); undoCount = History(vm, "_undo");
        vm.SelectOnly(N(0)); vm.BeginNodeDrag(); N(0).X += 99;
        vm.FinishNodeDrag([N(0).Id], true, bId);
        Check(State(vm) == beforeFailure, "A cycle-causing transfer restores coordinates and membership.");
        Check(History(vm, "_undo") == undoCount && vm.StatusMessage.Contains("循環"), "Rejected transfer explains why without adding history.");
        vm.BeginNodeDrag(); vm.FinishNodeDrag([N(0).Id], false, null);
        Check(History(vm, "_undo") == undoCount, "A zero-movement drag adds no history.");

        vm.SelectBlock(A()); vm.UngroupSelectedBlock();
        Check(vm.Graph.Project.Blocks.All(b => b.Id != aId), "Dissolve removes only the selected block.");
        Check(vm.Edges.Count == vm.Graph.Project.Edges.Count, "Dissolved edges are rebuilt for display.");
        Check(vm.Graph.ReadinessOf(N(3).Model) == Readiness.Blocked, "Dissolve preserves dependency gates.");
        vm.Undo(); Check(A().IsCollapsed, "Undo restores the block and collapse state.");

        vm.SelectBlock(A()); vm.StartKeyboardConnect(); vm.SelectBlock(B());
        Check(vm.IsConnecting && vm.ConnectSource?.Id == aId, "Selecting a block destination preserves the keyboard source.");
        vm.CancelKeyboardConnect();
        Check(!vm.IsConnecting, "Keyboard connection cancellation clears the source.");

        vm.ToggleBlockCollapse(A()); vm.ToggleBlockCollapse(B());
        N(0).Model.Status = N(1).Model.Status = N(2).Model.Status = NodeStatus.Done;
        N(3).Model.Status = NodeStatus.InProgress;
        vm.RefreshAll();
        vm.BeginNodeDrag(); vm.FinishNodeDrag([N(5).Id], true, aId);
        Check(N(3).Status == NodeStatus.InProgress && N(3).StatusLabel.Contains("先行に未完了あり"), "Late prerequisites warn without resetting started work.");
        vm.FocusBlock(A());
        vm.ToggleBlockCollapse(A());
        Check(!vm.IsFocusMode && A().IsCollapsed, "Collapse during focus exits focus and folds the block.");
        vm.SelectedNode = N(1);
        Check(N(1).IsVisible && !A().IsCollapsed, "Selecting a hidden member reveals its block.");
        vm.Graph.Project.Bookmark = new WorkBookmark { NodeId = N(1).Id };
        vm.ToggleBlockCollapse(A());
        Check(vm.ResumeBookmark() && N(1).IsVisible, "A bookmark inside a folded block is revealed.");
        vm.ToggleBlockCollapse(A());
        if (!B().IsCollapsed) vm.ToggleBlockCollapse(B());
        N(0).Model.Status = NodeStatus.Done;
        vm.HideCompleted = true;
        // Keep the unfinished member so that A remains represented despite completion filtering.
        Check(vm.Nodes.All(n => !n.IsVisible),
            "The test scene contains only collapsed blocks. visible=" + string.Join(",", vm.Nodes.Where(n => n.IsVisible).Select(n => n.Title)));
        pan.X = 9999; pan.Y = 9999; view.ZoomToFit();
        Check(double.IsFinite(pan.X) && pan.X != 9999, "Zoom-to-fit works when only blocks are visible.");
        var map = new MiniMap(); map.Update([], [A(), B()], new Rect(0, 0, 20, 20));
        var mapBounds = (Rect)typeof(MiniMap).GetMethod("ComputeBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map, null)!;
        Check(mapBounds.Contains(new Point(B().Bounds.Right, B().Bounds.Bottom)), "The minimap includes collapsed blocks outside the current viewport.");
        host.Close();
    }

    /// <summary>
    /// 回数で完了する項目。カードの加算ボタン、Space の振り分け、混在選択、履歴、
    /// 自己接続からの設定要求を、実際のビューを組み立てて確かめる。
    /// </summary>
    private static void VerifyRepeat()
    {
        TodoNode[] nodes = [
            new() { Title = "素振り", X = 40, Y = 70 },
            new() { Title = "フォーム確認", X = 420, Y = 70 },
            new() { Title = "資料をまとめる", X = 40, Y = 300 },
            new() { Title = "取り消した練習", X = 420, Y = 300 },
            new() { Title = "読書", X = 40, Y = 530 },
        ];
        var project = new TodoProject { Nodes = [.. nodes], Name = "回数で完了する項目の検証" };
        var graph = new TodoGraph(project);
        graph.Connect(nodes[0].Id, nodes[1].Id);

        var now = DateTimeOffset.Now;
        RepeatService.Configure(nodes[0], 3, 0, now);
        RepeatService.Configure(nodes[4], 2, 0, now);
        RepeatService.Configure(nodes[3], 4, 1, now);
        RepeatService.Cancel(nodes[3], now);

        // ブロックの中でもバッジと加算ボタンが重ならないことを、この場面で一緒に見る。
        var blockId = BlockService.Create(project, [nodes[2].Id, nodes[3].Id], "まとめ").Block!.Id;

        var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts");
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null, artifacts);
        var view = new GraphView { DataContext = vm, Width = 1280, Height = 720 };
        view.Measure(new Size(1280, 720)); view.Arrange(new Rect(0, 0, 1280, 720)); view.UpdateLayout();
        var host = new Window { Content = view, Width = 1280, Height = 720, Opacity = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        host.Show();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

        NodeViewModel N(int i) => vm.Nodes.Single(n => n.Id == nodes[i].Id);
        Button AdvanceButton(NodeViewModel node)
        {
            view.UpdateLayout();
            return Descendants(view).OfType<Button>()
                .Single(b => b.Tag is "repeat-advance" && ReferenceEquals(b.DataContext, node));
        }

        Check(N(0).IsRepeating && N(0).RepeatText == "0 / 3 回" && N(0).RepeatCompactText == "0/3",
            "The card and the minimal view both show the counts.");
        Check(!N(2).IsRepeating && N(2).RepeatText.Length == 0, "Plain steps show no repeat badge.");
        Check(AdvanceButton(N(0)).IsEnabled && !AdvanceButton(N(3)).IsEnabled,
            "The add button is disabled while the item is cancelled.");

        var undoCount = History(vm, "_undo");
        AdvanceButton(N(0)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(N(0).Model.Repeat!.CompletedCount == 1 && N(0).Status == NodeStatus.InProgress,
            "The card button adds exactly one through the standard Click event.");
        Check(History(vm, "_undo") == undoCount + 1, "One repetition is one undo step.");
        Check(N(1).Readiness == Readiness.Blocked, "Intermediate repetitions keep the successor waiting.");

        vm.SelectOnly(N(0));
        Check(vm.ToggleDoneLabel == "1回達成", "Space is relabelled for a repeating item.");
        vm.ToggleDone();
        vm.ToggleDone();
        Check(N(0).Status == NodeStatus.Done && N(0).Model.CompletedAt is not null,
            "The final repetition completes the item and stamps the time.");
        Check(N(1).Readiness == Readiness.Ready, "The successor is released only after the final repetition.");

        undoCount = History(vm, "_undo");
        var atCap = State(vm);
        AdvanceButton(N(0)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.ToggleDone();
        Check(State(vm) == atCap && History(vm, "_undo") == undoCount,
            "Pressing again at the target changes neither the counts nor the history.");

        vm.SelectNodes([N(2), N(0), N(3), N(4)]);
        Check(vm.ToggleDoneLabel.Contains("通常項目を完了"), "A mixed selection says what will happen.");
        var beforeBatch = State(vm);
        undoCount = History(vm, "_undo");
        vm.ToggleDone();
        Check(N(2).Status == NodeStatus.Done, "Plain unfinished steps complete in a mixed selection.");
        Check(N(4).Model.Repeat!.CompletedCount == 1, "Repeating items advance by one in a mixed selection.");
        Check(N(0).Model.Repeat!.CompletedCount == 3 && N(3).Model.Repeat!.CompletedCount == 1,
            "Finished items and cancelled repeats are left alone.");
        Check(N(3).Status == NodeStatus.Cancelled, "A cancelled repeat stays cancelled.");
        Check(History(vm, "_undo") == undoCount + 1, "A mixed batch is a single undo step.");
        vm.Undo();
        Check(State(vm) == beforeBatch, "Undo restores counts, statuses and completion times together.");

        // 汎用の状態指定でも、回数を置き去りにした完了は作らせない。
        vm.SelectOnly(N(4));
        N(4).Status = NodeStatus.Done;
        Check(N(4).Model.Repeat!.CompletedCount == 1 && N(4).Status == NodeStatus.InProgress,
            "Choosing 完了 on a repeating item advances once instead of forcing the status.");
        Check(RepeatService.Validate(N(4).Model) is null, "The item stays consistent after the status route.");

        // 設定要求の検証中はダイアログの購読を外し、無人検証でモーダルを開かない。
        view.DataContext = null;
        var requests = 0;
        var lastIsNew = false;
        vm.RepeatSettingsRequested += (_, isNew) => { requests++; lastIsNew = isNew; };
        vm.RequestRepeatFromSelfConnection(N(2).Id);
        Check(requests == 1 && lastIsNew, "Connecting a step to itself asks for a new repeat setting.");
        vm.RequestRepeatFromSelfConnection(N(0).Id);
        Check(requests == 2 && !lastIsNew, "An item that already repeats opens the edit form.");
        vm.RequestRepeatFromSelfConnection(blockId);
        Check(requests == 2 && vm.StatusMessage.Contains("ブロック全体"),
            "A block connected to itself explains that whole-block repetition is unsupported.");
        Check(vm.Graph.Project.Edges.All(e => e.FromId != e.ToId), "Self connections never become dependency edges.");

        view.DataContext = vm;
        RepeatService.Configure(N(0).Model, 3, 2, now);
        RepeatService.Configure(N(2).Model, 4, 1, now);
        vm.SelectOnly(null); vm.RefreshAll();
        Check(N(0).VisualBounds.Top < N(0).Y, "Visual bounds include the external self loop.");
        var repeatBlock = vm.Blocks.Single(b => b.Id == blockId);
        Check(repeatBlock.Bounds.Header.Bottom < N(2).VisualBounds.Top,
            "Block headers leave room for the member's self loop.");
        var repeatHeader = repeatBlock.Bounds.Header;
        vm.ToggleBlockCollapse(repeatBlock);
        Check(repeatBlock.X == repeatHeader.X && repeatBlock.Y == repeatHeader.Y && !N(2).IsVisible,
            "Collapsing a repeating block keeps its header anchored and hides the member loops.");
        vm.Undo();
        Render(view, "repeat-light");
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll();
        Render(view, "repeat-dark");
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();

        // 4 桁の回数と長い名前でも、加算ボタンと折りたたみが重ならないことを目で確かめる。
        N(4).Title = "とても長い名前でも回数と操作が重ならないことを確認するためのステップ";
        RepeatService.Configure(N(4).Model, 9999, 1234, now);
        vm.RefreshAll();
        Check(N(4).RepeatText == "1234 / 9999 回", "Four digit counts render in full.");
        Render(view, "repeat-wide-light");
        NodeMetrics.Apply(ToDoTree.Core.Layout.NodeStyle.Minimal);
        ((ItemsControl)view.FindName("NodeHost")).ItemTemplate = (DataTemplate)view.FindResource("NodeMinimalTemplate");
        vm.RefreshAll();
        Render(view, "repeat-minimal-light");
        Check(N(4).VisualBounds.Width >= 112, "Minimal bounds include the full loop label.");
        var label = Descendants(view).OfType<Button>().Single(b => b.Tag is "repeat-badge" && ReferenceEquals(b.DataContext, N(4)));
        var rightOfLabel = label.TransformToAncestor(view).Transform(new Point(label.ActualWidth - 3, label.ActualHeight / 2));
        var hit = view.InputHitTest(rightOfLabel) as DependencyObject;
        while (hit is not null && !ReferenceEquals(hit, label)) hit = VisualTreeHelper.GetParent(hit);
        Check(hit == label, "The right side of the minimal loop label is visible and clickable beyond the node bounds.");
        vm.SelectOnly(N(0)); vm.StartKeyboardConnect();
        var edgeLayer = (EdgeLayer)view.FindName("EdgeRenderer");
        Check(typeof(EdgeLayer).GetField("_previewLoopGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(edgeLayer) is Geometry,
            "A keyboard self connection previews the same external loop.");
        vm.CancelKeyboardConnect();
        Check(typeof(EdgeLayer).GetField("_previewLoopGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(edgeLayer) is null,
            "Cancelling connection clears the loop preview.");
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll();
        Render(view, "repeat-minimal-dark");
        ThemeManager.Apply(AppTheme.Light);
        NodeMetrics.Apply(ToDoTree.Core.Layout.NodeStyle.Card); vm.RefreshAll();

        host.Close();
    }

    private static void VerifyLoopLayout()
    {
        foreach (var direction in new[] { ToDoTree.Core.Layout.LayoutDirection.LeftToRight, ToDoTree.Core.Layout.LayoutDirection.TopToBottom })
        {
            var first = new TodoNode { Title = "反復1", X = 100, Y = 120 };
            var second = new TodoNode { Title = "反復2", X = 150, Y = 340 };
            RepeatService.Configure(first, 3, 1, DateTimeOffset.Now);
            RepeatService.Configure(second, 3, 1, DateTimeOffset.Now);
            var project = new TodoProject { Nodes = [first, second] };
            new TodoGraph(project).Connect(first.Id, second.Id);
            var id = BlockService.Create(project, [first.Id, second.Id], "周囲に収まるループ").Block!.Id;
            var vm = new MainViewModel(new JsonProjectStore(), new AppSettings { Direction = direction }, project, null, AppContext.BaseDirectory);
            var block = vm.Blocks.Single(b => b.Id == id);
            vm.SelectBlock(block);
            var original = State(vm);
            var header = block.Bounds.Header;
            vm.LayoutSelectedBlock();
            Check(State(vm) != original, "Loop layout produces an arranged block in both directions.");
            Check(block.Bounds.Header.X == header.X && block.Bounds.Header.Y == header.Y,
                "Arranging repeating members preserves the block header position.");
            Check(!vm.Nodes[0].VisualBounds.IntersectsWith(vm.Nodes[1].VisualBounds),
                "Arranged cards leave enough room for their external loops.");
            vm.Undo();
            Check(State(vm) == original, "The loop-aware block layout is one undo step.");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Render(GraphView view, string name)
    {
        view.ZoomToFit();
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png")); png.Save(stream);
    }
}
