using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Controls;

/// <summary>ズーム・パン・ドラッグ・複数選択ができるグラフのキャンバス。</summary>
public partial class GraphView : UserControl
{
    public static readonly DependencyProperty IsConnectionDraggingProperty = DependencyProperty.Register(
        nameof(IsConnectionDragging), typeof(bool), typeof(GraphView), new PropertyMetadata(false));

    public bool IsConnectionDragging
    {
        get => (bool)GetValue(IsConnectionDraggingProperty);
        private set => SetValue(IsConnectionDraggingProperty, value);
    }

    private static ConnectionSide SideOf(DependencyObject? hit) => (hit as FrameworkElement)?.Tag switch
    {
        "connector-top" => ConnectionSide.Top,
        "connector-bottom" => ConnectionSide.Bottom,
        "connector-left" => ConnectionSide.Left,
        "connector" => ConnectionSide.Right,
        _ => ConnectionSide.Auto,
    };

    private const double MinZoom = 0.25;
    private const double MaxZoom = 2.5;

    /// <summary>線をクリックしたとみなす距離（キャンバス上の単位）。</summary>
    private const double EdgeHitTolerance = 8;

    private MainViewModel? _viewModel;

    private readonly List<(NodeViewModel Node, Vector Offset)> _dragGroup = [];
    private bool _dragUndoPushed;
    private bool _draggingWaypoint;
    private Vector _waypointOffset;

    private ConnectionSide _connectSide;
    private NodeViewModel? _connectSource;

    private bool _panning;
    private Point _panStartScreen;
    private double _panStartX;
    private double _panStartY;

    private Point? _marqueeStart;

    private Point _pressScreen;
    private bool _movedSincePress;

    /// <summary>見出しを押さえたブロック。しきい値を超えたところで移動が始まる。</summary>
    private BlockViewModel? _blockPress;
    private Point _blockPressScreen;
    private Point _blockPressWorld;
    private bool _blockDragActive;
    private BlockBounds _snapStart;
    private SnapTarget[] _snapTargets = [];
    private SnapState _snapState = new();
    private Point _snapPointer;
    private Window? _ownerWindow;
    private (double Width, double Height) _snapNodeSize;
    private Guid[] _snapMembers = [];
    private (int Nodes, int Edges, int Blocks) _snapCounts;
    private LayoutDirection _snapDirection;
    private const string SnapHint = "Altで吸着を解除";
    private string _beforeSnapStatus = string.Empty;

    /// <summary>右クリックでメニューを用意した直後だけ true。キーボードからの要求と区別する。</summary>
    private bool _menuRequested;

    public GraphView()
    {
        InitializeComponent();
        Viewport.AllowDrop = true;
        Viewport.PreviewDragOver += OnTemplateDragOver;
        Viewport.PreviewDrop += OnTemplateDrop;
        Viewport.MouseLeave += (_, _) => SetCompletionHover(null);
        PreviewMouseDown += (_, _) => SetCompletionHover(null);
        DataContextChanged += OnDataContextChanged;
        MiniMapView.Navigate += OnMiniMapNavigate;
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _templateLibrary?.Close();
            SetCompletionHover(null);
            EdgeRenderer.ClearCompletionEffects();
            EndInteraction();
            if (_ownerWindow is not null) _ownerWindow.Deactivated -= OnWindowDeactivated;
            _ownerWindow = null;
        };
        PreviewKeyDown += OnSnapModifierKey;
        PreviewKeyUp += OnSnapModifierKey;
        Viewport.LostMouseCapture += (_, _) =>
        {
            _draggingWaypoint = false;

            // 予期しないキャプチャ喪失は「移動をやめた」扱いにする。
            // 確定に伴う通常の解放では、この時点ですでに印を落としてある。
            CancelBlockDragIfActive();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ownerWindow is not null) _ownerWindow.Deactivated -= OnWindowDeactivated;
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null) _ownerWindow.Deactivated += OnWindowDeactivated;
        Viewport.Focus();
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_viewModel?.HasViewportState == true)
                {
                    RestoreViewport(_viewModel);
                }
                else
                {
                    ZoomToFit();
                }
            }),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _templateLibrary?.Close();
        SetCompletionHover(null);
        EdgeRenderer.ClearCompletionEffects();
        if (_viewModel is not null)
        {
            // 移動は取り消し、名前の書き換えは確定してからタブを離れる。
            CancelBlockDragIfActive();
            _viewModel.EndBlockRename(commit: true);

            CaptureViewport(_viewModel);
            _viewModel.VisualsChanged -= OnVisualsChanged;
            _viewModel.TemplateLibraryRequested -= OpenTemplateLibrary;
            _viewModel.CompletionRequested -= OnCompletionRequested;
            _viewModel.ZoomToFitRequested -= OnZoomToFitRequested;
            _viewModel.ZoomStepRequested -= OnZoomStepRequested;
            _viewModel.CenterOnRequested -= OnCenterOnRequested;
            _viewModel.EnsureVisibleRequested -= OnEnsureVisibleRequested;
            _viewModel.FocusCanvasRequested -= OnFocusCanvasRequested;
        }

        EndInteraction();
        _viewModel = e.NewValue as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.VisualsChanged += OnVisualsChanged;
            _viewModel.TemplateLibraryRequested += OpenTemplateLibrary;
            _viewModel.CompletionRequested += OnCompletionRequested;
            _viewModel.ZoomToFitRequested += OnZoomToFitRequested;
            _viewModel.ZoomStepRequested += OnZoomStepRequested;
            _viewModel.CenterOnRequested += OnCenterOnRequested;
            _viewModel.EnsureVisibleRequested += OnEnsureVisibleRequested;
            _viewModel.FocusCanvasRequested += OnFocusCanvasRequested;

            if (IsLoaded)
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (_viewModel is null)
                        {
                            return;
                        }

                        if (_viewModel.HasViewportState)
                        {
                            RestoreViewport(_viewModel);
                        }
                        else
                        {
                            ZoomToFit();
                        }
                    }),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        EdgeRenderer.Redraw();
    }

    private void OnVisualsChanged(object? sender, EventArgs e)
    {
        EdgeRenderer.ClearCompletionEffects();
        SetCompletionHover(_completionHover);
        if (_blockDragActive && _viewModel?.IsBlockDragging != true)
        {
            _blockDragActive = false;
            _blockPress = null;
            ClearSnap();
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
        }
        else if (_blockDragActive && !SnapSessionIsValid())
        {
            EndInteraction();
        }
        UpdateKeyboardConnectPreview();
        EdgeRenderer.Redraw();
        SyncMiniMap();
    }

    /// <summary>ミニマップに、いまの表示範囲と見えているカードを渡す。</summary>
    private void SyncMiniMap()
    {
        if (_viewModel is null)
        {
            return;
        }

        var scale = Math.Max(0.01, ZoomTransform.ScaleX);
        CaptureViewport(_viewModel);
        var world = new Rect(
            -PanTransform.X / scale,
            -PanTransform.Y / scale,
            Math.Max(1, Viewport.ActualWidth / scale),
            Math.Max(1, Viewport.ActualHeight / scale));

        MiniMapView.Update(
            [.. _viewModel.Nodes.Where(n => n.IsVisible)],
            [.. _viewModel.Blocks.Where(b => b.IsVisible)],
            world);
    }

    private void CaptureViewport(MainViewModel viewModel)
    {
        viewModel.ViewZoom = Math.Max(MinZoom, ZoomTransform.ScaleX);
        viewModel.ViewPanX = PanTransform.X;
        viewModel.ViewPanY = PanTransform.Y;
        viewModel.HasViewportState = true;
    }

    private void RestoreViewport(MainViewModel viewModel)
    {
        var zoom = Math.Clamp(viewModel.ViewZoom, MinZoom, MaxZoom);
        ZoomTransform.ScaleX = zoom;
        ZoomTransform.ScaleY = zoom;
        PanTransform.X = viewModel.ViewPanX;
        PanTransform.Y = viewModel.ViewPanY;
        EdgeRenderer.Redraw();
        SyncMiniMap();
    }

    private void OnMiniMapNavigate(object? sender, Point world)
    {
        if (_blockPress is not null) return;
        var scale = ZoomTransform.ScaleX;
        PanTransform.X = (Viewport.ActualWidth / 2) - (world.X * scale);
        PanTransform.Y = (Viewport.ActualHeight / 2) - (world.Y * scale);
        SyncMiniMap();
    }

    private void OnZoomToFitRequested(object? sender, EventArgs e) => ZoomToFit();

    private void OnZoomStepRequested(object? sender, double factor) =>
        ZoomAt(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), factor);

    private void OnCenterOnRequested(object? sender, NodeViewModel node) => CenterOn(node);

    private void OnEnsureVisibleRequested(object? sender, NodeViewModel node) => EnsureVisible(node);

    private void OnFocusCanvasRequested(object? sender, EventArgs e) => FocusCanvas();

    /// <summary>キャンバスにフォーカスを戻す（キーボード操作を効かせるため）。</summary>
    public void FocusCanvas() => Viewport.Focus();

    // ---- マウス ----

    /// <summary>
    /// 右クリック。押した場所を見て、線・ステップ・背景のどのメニューを出すかを決める。
    /// メニュー自体は GraphView.xaml のリソースにあり、ここでは Viewport に差し替えるだけ。
    /// 実際に開くのは、このあとの右ボタンを離したときの標準の動き。
    /// </summary>
    private void OnViewportPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_blockPress is not null) { e.Handled = true; return; }
        Viewport.ContextMenu = null;

        if (_viewModel is null)
        {
            return;
        }

        // 接続中・パン中・矩形選択中・ドラッグ中は、その操作を邪魔しない。
        if (_connectSource is not null || _panning || _marqueeStart is not null
            || _dragGroup.Count > 0 || _draggingWaypoint || _blockDragActive)
        {
            return;
        }

        // 名前を書き換えている入力欄では、切り取り・貼り付けの標準メニューに任せる。
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        // ミニマップの上では出さない（全体図をクリックして飛ぶための場所なので）。
        if (FindAncestor<MiniMap>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var world = e.GetPosition(Surface);
        _viewModel.SetMenuAnchor(world.X, world.Y);
        Viewport.Focus();

        if (FindNodeElement(e.OriginalSource as DependencyObject)?.DataContext is NodeViewModel node)
        {
            // まとめて選んでいるときは、その選択を保ったままメニューを出す。
            if (!(_viewModel.SelectionCount > 1 && _viewModel.IsSelected(node)))
            {
                _viewModel.SelectOnly(node);
            }

            ShowMenu("NodeMenu");
            return;
        }

        var tolerance = EdgeHitTolerance / Math.Max(0.2, ZoomTransform.ScaleX);
        if (_viewModel.FindWaypointAt(new Vec2(world.X, world.Y), tolerance) is { } waypoint)
        {
            _viewModel.SelectWaypoint(waypoint.Edge, waypoint.Index);
            ShowMenu("WaypointMenu");
            return;
        }
        if (_viewModel.FindEdgeAt(world.X, world.Y, tolerance) is { } edge)
        {
            _viewModel.SelectEdge(edge);
            ShowMenu("EdgeMenu");
            return;
        }

        // 見出しは線より奥に描いてあるので、線・通過点を先に見てからここへ来る。
        if (FindBlockElement(e.OriginalSource as DependencyObject)?.DataContext is BlockViewModel block)
        {
            _viewModel.SelectBlock(block);
            ShowMenu("BlockMenu");
            return;
        }

        ShowMenu("CanvasMenu");
    }

    private void ShowMenu(string key)
    {
        Viewport.ContextMenu = (ContextMenu)FindResource(key);
        _menuRequested = true;
    }

    /// <summary>
    /// キーボードのメニューキーなど、右クリック以外から開こうとしたときは出さない。
    /// 直前に割り当てたメニューがそのまま残っているので、
    /// 中身も「どこを指していたか」も古いまま開いてしまうため。
    /// </summary>
    private void OnViewportContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_menuRequested)
        {
            Viewport.ContextMenu = null;
            e.Handled = true;
        }

        _menuRequested = false;
    }

    private void OnViewportPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // カードの上の編集欄をクリックしたときは、そのまま入力させる。
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _pressScreen = e.GetPosition(Viewport);
        _movedSincePress = false;
        var world = e.GetPosition(Surface);

        var element = FindNodeElement(e.OriginalSource as DependencyObject);
        if (element?.DataContext is NodeViewModel node)
        {
            Viewport.Focus();

            // カードの隅の「▾」は、その先を畳む / 開くボタン。
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is { Tag: "collapse" })
            {
                _viewModel.ToggleCollapse(node);
                e.Handled = true;
                return;
            }

            if (e.ClickCount >= 2)
            {
                _viewModel.BeginEdit(node);
                e.Handled = true;
                return;
            }

            // Ctrl+クリックは選択に足す / 外す。
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _viewModel.ToggleSelection(node);
                e.Handled = true;
                return;
            }

            if ((e.OriginalSource as FrameworkElement)?.Tag is string connectorTag && connectorTag is "connector" or "connector-top" or "connector-bottom" or "connector-left")
            {
                _viewModel.SelectOnly(node);
                _connectSource = node;
                IsConnectionDragging = true;
                _connectSide = connectorTag switch
                {
                    "connector-top" => ConnectionSide.Top,
                    "connector-bottom" => ConnectionSide.Bottom,
                    "connector-left" => ConnectionSide.Left,
                    _ => ConnectionSide.Right,
                };
                UpdatePreviewLine(world);
                Viewport.CaptureMouse();
                e.Handled = true;
                return;
            }

            // すでに複数選ばれていて、その一員を掴んだときは選択を保ったまま動かす。
            if (!(_viewModel.SelectionCount > 1 && _viewModel.IsSelected(node)))
            {
                _viewModel.SelectOnly(node);
            }

            BeginDrag(node, world);
            Viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        Viewport.Focus();

        // ブロックの見出し。囲みの本体は当たり判定を持たないので、ここへ来るのは見出しだけ。
        if (FindBlockElement(e.OriginalSource as DependencyObject)?.DataContext is BlockViewModel block)
        {
            _viewModel.SelectBlock(block);

            if (e.ClickCount >= 2)
            {
                _viewModel.BeginBlockRename(block);
                e.Handled = true;
                return;
            }

            // 押した瞬間の座標を控える。差分はここから毎回計算し、足し込まない。
            _blockPress = block;
            _blockPressScreen = _pressScreen;
            _blockPressWorld = world;
            _blockDragActive = false;
            Viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Alt+ドラッグは矩形選択。
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _marqueeStart = world;
            Viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_viewModel.FindWaypointAt(new Vec2(world.X, world.Y), EdgeHitTolerance / Math.Max(0.2, ZoomTransform.ScaleX)) is { } waypoint)
        {
            _viewModel.SelectWaypoint(waypoint.Edge, waypoint.Index);
            var position = waypoint.Edge.Model.Waypoints[waypoint.Index];
            _waypointOffset = new Vector(position.X - world.X, position.Y - world.Y);
            _draggingWaypoint = true;
            _dragUndoPushed = false;
            Viewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        // 背景でも、線の近くをクリックしたらその線を選ぶ。
        if (_viewModel.FindEdgeAt(world.X, world.Y, EdgeHitTolerance / Math.Max(0.2, ZoomTransform.ScaleX)) is { } edge)
        {
            _viewModel.SelectEdge(edge);
            if (e.ClickCount >= 2) _viewModel.AddWaypoint(new Vec2(world.X, world.Y));
            e.Handled = true;
            return;
        }

        _panning = true;
        _panStartScreen = _pressScreen;
        _panStartX = PanTransform.X;
        _panStartY = PanTransform.Y;
        Viewport.CaptureMouse();
    }

    private void BeginDrag(NodeViewModel node, Point world)
    {
        IsConnectionDragging = false;
        _dragGroup.Clear();
        _dragUndoPushed = false;

        var targets = _viewModel is { } vm && vm.SelectionCount > 1 && vm.IsSelected(node)
            ? vm.SelectedNodes
            : [node];

        foreach (var target in targets)
        {
            _dragGroup.Add((target, new Vector(target.X - world.X, target.Y - world.Y)));
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        SetCompletionHover(null);
        EdgeRenderer.ClearCompletionEffects();
        EndInteraction();
    }

    private void ClearSnap()
    {
        if (_viewModel?.StatusMessage == SnapHint) _viewModel.StatusMessage = _beforeSnapStatus;
        _snapTargets = [];
        _snapMembers = [];
        _snapState = new();
        AlignmentGuides.Update([], 1, 0, 0);
    }

    private void BeginSnap(BlockViewModel block)
    {
        _snapStart = block.Bounds;
        _snapNodeSize = (NodeViewModel.CardWidth, NodeViewModel.CardHeight);
        _snapMembers = block.Model.NodeIds.ToArray();
        _snapCounts = (_viewModel!.Nodes.Count, _viewModel.Edges.Count, _viewModel.Blocks.Count);
        _snapDirection = _viewModel.Direction;
        _snapState = new();
        var zoom = ZoomTransform.ScaleX;
        var viewport = new BlockBounds(-PanTransform.X / zoom, -PanTransform.Y / zoom,
            Viewport.ActualWidth / zoom, Viewport.ActualHeight / zoom);
        _snapTargets = _viewModel!.Blocks
            .Where(b => b.Id != block.Id && b.IsVisible && b.VisibleCount == b.TotalCount
                && b.Bounds.IntersectsWith(viewport))
            .Select(b => new SnapTarget(b.Id, b.Bounds)).ToArray();
        _beforeSnapStatus = _viewModel.StatusMessage;
        _viewModel.StatusMessage = SnapHint;
    }

    private bool SnapSessionIsValid() => _blockPress is { } source && _viewModel is not null
        && source.CanMove && source.IsVisible && _viewModel.Blocks.Contains(source)
        && _snapNodeSize == (NodeViewModel.CardWidth, NodeViewModel.CardHeight)
        && _snapDirection == _viewModel.Direction
        && _snapCounts == (_viewModel.Nodes.Count, _viewModel.Edges.Count, _viewModel.Blocks.Count)
        && _snapMembers.SequenceEqual(source.Model.NodeIds)
        && _snapTargets.All(t => _viewModel.Blocks.Any(b => b.Id == t.Id && b.IsVisible
            && b.VisibleCount == b.TotalCount && b.Bounds == t.Bounds));

    private void UpdateSnap(Point pointer)
    {
        if (!_blockDragActive || _viewModel is null) return;
        if (!SnapSessionIsValid())
        {
            EndInteraction();
            return;
        }
        _snapPointer = pointer;
        try
        {
            var result = BlockSnapService.Compute(_snapStart,
                new(pointer.X - _blockPressWorld.X, pointer.Y - _blockPressWorld.Y),
                _snapTargets, _snapState, ZoomTransform.ScaleX,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            _snapState = result.State;
            _viewModel.UpdateBlockDrag(result.Delta.X, result.Delta.Y);
            if (_blockDragActive)
                AlignmentGuides.Update(result.Guides, ZoomTransform.ScaleX, PanTransform.X, PanTransform.Y);
        }
        catch (ArgumentException)
        {
            EndInteraction();
        }
    }

    private void OnSnapModifierKey(object sender, KeyEventArgs e)
    {
        if (_blockPress is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            if (_blockDragActive) UpdateSnap(_snapPointer);
            e.Handled = true;
        }
        else if (e.RoutedEvent == Keyboard.PreviewKeyDownEvent)
        {
            if (key == Key.Escape) EndInteraction();
            // 保存は既存の確定経路へ渡す。他の編集・表示操作はドラッグを終えてから。
            if (!(key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) e.Handled = true;
        }
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        var hovered = e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released
            && e.MiddleButton == MouseButtonState.Released && _viewModel?.IsConnecting != true
            ? FindNodeElement(e.OriginalSource as DependencyObject)?.DataContext as NodeViewModel : null;
        if (!ReferenceEquals(hovered, _completionHover)) SetCompletionHover(hovered);
        var screen = e.GetPosition(Viewport);
        if (!_movedSincePress && (screen - _pressScreen).Length > 3)
        {
            _movedSincePress = true;
        }

        if (_blockPress is { } pending && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_blockDragActive)
            {
                // 開始判定は画面座標。ズーム倍率で掴みやすさが変わらないようにする。
                var moved = screen - _blockPressScreen;
                if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                if (_viewModel?.BeginBlockDrag(pending) != true)
                {
                    _blockPress = null;
                    return;
                }

                _blockDragActive = true;
                BeginSnap(pending);
            }

            UpdateSnap(e.GetPosition(Surface));
            e.Handled = true;
            return;
        }

        if (_draggingWaypoint && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_movedSincePress) return;
            if (!_dragUndoPushed)
            {
                _viewModel?.PushUndo();
                _dragUndoPushed = true;
            }
            var position = e.GetPosition(Surface);
            _viewModel?.MoveWaypoint(new Vec2(position.X + _waypointOffset.X, position.Y + _waypointOffset.Y));
            e.Handled = true;
            return;
        }

        if (_dragGroup.Count > 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragUndoPushed && _movedSincePress)
            {
                _viewModel?.BeginNodeDrag();
                _viewModel?.MarkDirty();
                _dragUndoPushed = true;
            }

            var world = e.GetPosition(Surface);
            foreach (var (node, offset) in _dragGroup)
            {
                node.X = world.X + offset.X;
                node.Y = world.Y + offset.Y;
            }

            return;
        }

        if (_connectSource is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdatePreviewLine(e.GetPosition(Surface));
            return;
        }

        if (_marqueeStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            EdgeRenderer.SetMarquee(MakeRect(start, e.GetPosition(Surface)));
            return;
        }

        if (_panning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
        {
            PanTransform.X = _panStartX + (screen.X - _panStartScreen.X);
            PanTransform.Y = _panStartY + (screen.Y - _panStartScreen.Y);
            SyncMiniMap();
        }
    }

    private void OnViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null)
        {
            EndInteraction();
            return;
        }

        if (_blockPress is not null)
        {
            if (_blockDragActive) UpdateSnap(e.GetPosition(Surface));
            var wasDragging = _blockDragActive;

            // 先に印を落としてから確定する。キャプチャの解放で「取り消し」に化けないように。
            _blockPress = null;
            _blockDragActive = false;

            if (wasDragging)
            {
                _viewModel.CommitBlockDrag();
            }

            EndInteraction();
            return;
        }

        if (_connectSource is not null)
        {
            var hit = HitTestAt(e.GetPosition(Viewport));
            var target = FindNodeElement(hit)?.DataContext as NodeViewModel;

            if (target is not null && target.Id != _connectSource.Id)
            {
                _viewModel.TryConnect(_connectSource.Id, target.Id, _connectSide, SideOf(hit));
            }
            else if (target is null)
            {
                _viewModel.StatusMessage = "繋ぎたいステップの上で離してください。";
            }

            _connectSource = null;
            IsConnectionDragging = false;
            EdgeRenderer.SetPreview(null, null);
        }
        else if (_marqueeStart is { } start)
        {
            var rect = MakeRect(start, e.GetPosition(Surface));
            EdgeRenderer.SetMarquee(null);
            _marqueeStart = null;

            var caught = _viewModel.Nodes
                .Where(n => rect.IntersectsWith(new Rect(n.X, n.Y, NodeViewModel.CardWidth, NodeViewModel.CardHeight)))
                .ToList();

            if (caught.Count > 0)
            {
                _viewModel.SelectNodes(caught);
                _viewModel.StatusMessage = $"{caught.Count} 件を選びました。";
            }
            else
            {
                _viewModel.SelectOnly(null);
            }
        }
        else if (_panning && !_movedSincePress)
        {
            _viewModel.SelectOnly(null);
        }

        EndInteraction();
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_blockPress is not null) { e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panning = true;
        _panStartScreen = e.GetPosition(Viewport);
        _panStartX = PanTransform.X;
        _panStartY = PanTransform.Y;
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_blockPress is not null && e.ChangedButton == MouseButton.Middle) { e.Handled = true; return; }
        if (e.ChangedButton == MouseButton.Middle)
        {
            EndInteraction();
        }
    }

    private void EndInteraction()
    {
        CancelBlockDragIfActive();
        _draggingWaypoint = false;
        IsConnectionDragging = false;
        _dragGroup.Clear();
        _panning = false;
        _marqueeStart = null;
        EdgeRenderer.SetMarquee(null);

        if (Viewport.IsMouseCaptured)
        {
            Viewport.ReleaseMouseCapture();
        }
    }

    /// <summary>移動の途中なら取り消して、押した瞬間の位置へ戻す。</summary>
    private void CancelBlockDragIfActive()
    {
        _blockPress = null;
        ClearSnap();

        if (!_blockDragActive)
        {
            return;
        }

        _blockDragActive = false;
        _viewModel?.CancelBlockDrag();
    }

    private void UpdatePreviewLine(Point world)
    {
        if (_connectSource is null)
        {
            return;
        }

        var from = new Point(
            _connectSource.X + (_connectSide switch { ConnectionSide.Left => 0, ConnectionSide.Right => NodeViewModel.CardWidth, _ => NodeViewModel.CardWidth / 2 }),
            _connectSource.Y + (_connectSide switch { ConnectionSide.Top => 0, ConnectionSide.Bottom => NodeViewModel.CardHeight, _ => NodeViewModel.CardHeight / 2 }));

        var hit = HitTestAt(Surface.TranslatePoint(world, Viewport));
        var target = FindNodeElement(hit)?.DataContext as NodeViewModel;
        if (target is not null && target.Id != _connectSource.Id)
        {
            EdgeRenderer.SetPreviewRoute(EdgeRouting.Route(
                new Vec2(_connectSource.X, _connectSource.Y), new Vec2(target.X, target.Y),
                NodeViewModel.CardWidth, NodeViewModel.CardHeight,
                _viewModel!.Nodes.Where(n => n.IsVisible && n.Id != _connectSource.Id && n.Id != target.Id)
                    .Select(n => new Vec2(n.X, n.Y)).ToArray(), _connectSide, SideOf(hit)));
        }
        else
        {
            EdgeRenderer.SetPreview(from, world, _connectSide);
        }
    }

    /// <summary>キーボードで接続中は、相手までのガイド線を出す。</summary>
    private void UpdateKeyboardConnectPreview()
    {
        if (_connectSource is not null)
        {
            return;
        }

        if (_viewModel is { IsConnecting: true, ConnectSource: { } source } &&
            _viewModel.SelectedNode is { } target &&
            !ReferenceEquals(source, target))
        {
            EdgeRenderer.SetPreview(
                new Point(source.X + NodeViewModel.CardWidth, source.Y + (NodeViewModel.CardHeight / 2)),
                target.Center);
            return;
        }

        EdgeRenderer.SetPreview(null, null);
    }

    private static Rect MakeRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // ---- カード上の編集欄 ----

    private void OnCardEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Shift:
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.Shift:
                _viewModel.AddNode(_viewModel.SelectedNode, sibling: true);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.AddNode(_viewModel.SelectedNode, sibling: false);
                e.Handled = true;
                break;

            case Key.Escape:
                _viewModel.EndEdit();
                Viewport.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnCardEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 編集をやめるのは、そのカードだけ（次のカードの編集を消さないように）。
        if (sender is FrameworkElement element && element.DataContext is NodeViewModel node)
        {
            node.IsEditing = false;
        }
    }

    // ---- ブロックの見出しの入力欄 ----

    private void OnBlockEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // 日本語変換の途中に押した Enter は、WPF が Key.ImeProcessed として届ける。
        // ここで拾うと変換の確定と名前の確定が同時に起きるので、そのまま入力に任せる。
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.EndBlockRename(commit: true);
                Viewport.Focus();
                e.Handled = true;
                break;

            case Key.Escape:
                _viewModel.EndBlockRename(commit: false);
                Viewport.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnBlockEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 外側をクリックしたら、そこで確定する。
        _viewModel?.EndBlockRename(commit: true);
    }

    // ---- キーボード ----

    private void OnViewportKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // 入力欄に文字を打っている最中は、キャンバスの操作を拾わない。
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (control && e.Key == Key.A)
        {
            // 矩形選択と Ctrl+A は、これまでどおりステップだけを対象にする。
            _viewModel.SelectAllNodes();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.Down)
        {
            _viewModel.SelectBranch();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.L)
        {
            // ドラッグや接続の途中に整列させない。手を離してからやり直してもらう。
            if (_dragGroup.Count > 0 || _draggingWaypoint || _blockDragActive
                || _blockPress is not null || _connectSource is not null)
            {
                e.Handled = true;
                return;
            }

            _viewModel.Align();
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.G)
        {
            _viewModel.UngroupSelectedBlock();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.G)
        {
            _viewModel.GroupSelectedNodes();
            e.Handled = true;
            return;
        }

        // ブロックを選んでいるあいだは、ステップの追加・完了・削除を割り当てない。
        // 囲みの解除は Ctrl+Shift+G か右クリックのメニューだけで行う。
        if (_viewModel.HasSelectedBlock && e.Key is Key.Enter or Key.Space or Key.Delete)
        {
            _viewModel.StatusMessage = e.Key switch
            {
                Key.Delete => "ブロックを選んでいます。囲みを外すには Ctrl+Shift+G を押してください。",
                _ => "ブロックを選んでいます。中のステップを操作するには、カードを選び直してください。",
            };

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when _viewModel.IsConnecting:
                _viewModel.CompleteKeyboardConnect();
                e.Handled = true;
                break;

            case Key.Enter when shift:
                _viewModel.AddNode(_viewModel.SelectedNode, sibling: true);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.AddNode(_viewModel.SelectedNode, sibling: false);
                e.Handled = true;
                break;

            case Key.Delete:
                if (_viewModel.SelectedWaypointIndex >= 0) _viewModel.RemoveWaypoint();
                else _viewModel.DeleteSelected();
                e.Handled = true;
                break;

            case Key.Space:
                _viewModel.ToggleDone();
                e.Handled = true;
                break;

            case Key.F2 when _viewModel.HasSelectedBlock:
                _viewModel.BeginBlockRename(_viewModel.SelectedBlock);
                e.Handled = true;
                break;

            case Key.F2:
                _viewModel.BeginEdit(null);
                e.Handled = true;
                break;

            case Key.Left:
                _viewModel.MoveSelection(MoveDirection.Left);
                e.Handled = true;
                break;

            case Key.Right:
                _viewModel.MoveSelection(MoveDirection.Right);
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MoveSelection(MoveDirection.Up);
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MoveSelection(MoveDirection.Down);
                e.Handled = true;
                break;

            case Key.Escape:
                if (_blockDragActive)
                {
                    CancelBlockDragIfActive();
                }
                else if (_viewModel.IsConnecting)
                {
                    _viewModel.CancelKeyboardConnect();
                }
                else if (_viewModel.HasSelectedBlock)
                {
                    _viewModel.SelectBlock(null);
                }
                else
                {
                    _connectSource = null;
                    IsConnectionDragging = false;
                    EdgeRenderer.SetPreview(null, null);
                    _viewModel.SelectOnly(null);
                }

                e.Handled = true;
                break;
        }
    }

    // ---- 表示位置 ----

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(Viewport), e.Delta > 0 ? 1.12 : 1 / 1.12);
        e.Handled = true;
    }

    private void ZoomAt(Point screen, double factor)
    {
        if (_blockPress is not null) return;
        var current = ZoomTransform.ScaleX;
        var next = Math.Clamp(current * factor, MinZoom, MaxZoom);
        if (Math.Abs(next - current) < 0.0001)
        {
            return;
        }

        var worldX = (screen.X - PanTransform.X) / current;
        var worldY = (screen.Y - PanTransform.Y) / current;

        ZoomTransform.ScaleX = next;
        ZoomTransform.ScaleY = next;
        PanTransform.X = screen.X - (worldX * next);
        PanTransform.Y = screen.Y - (worldY * next);
        SyncMiniMap();
    }

    public void ZoomToFit()
    {
        if (_blockPress is not null) return;
        if (_viewModel is null || _viewModel.Nodes.Count == 0)
        {
            return;
        }

        var viewWidth = Viewport.ActualWidth;
        var viewHeight = Viewport.ActualHeight;
        if (viewWidth < 20 || viewHeight < 20)
        {
            return;
        }

        // 見えているカードだけに合わせる（畳んだ先や絞り込みで隠したものは無視する）。
        var shown = _viewModel.Nodes.Where(n => n.IsVisible).ToList();
        if (shown.Count == 0)
        {
            return;
        }

        var minX = shown.Min(n => n.X);
        var minY = shown.Min(n => n.Y);
        var maxX = shown.Max(n => n.X) + NodeViewModel.CardWidth;
        var maxY = shown.Max(n => n.Y) + NodeViewModel.CardHeight;

        foreach (var point in _viewModel.Edges.Where(e => e.From.IsVisible && e.To.IsVisible).SelectMany(e => e.Model.Waypoints))
        {
            minX = Math.Min(minX, point.X - 8);
            minY = Math.Min(minY, point.Y - 8);
            maxX = Math.Max(maxX, point.X + 8);
            maxY = Math.Max(maxY, point.Y + 8);
        }

        // 囲みは見出しのぶんカードより上へ出る。切れないよう、その範囲も含める。
        foreach (var block in _viewModel.Blocks.Where(b => b.IsVisible))
        {
            minX = Math.Min(minX, block.X);
            minY = Math.Min(minY, block.Y);
            maxX = Math.Max(maxX, block.X + block.Width);
            maxY = Math.Max(maxY, block.Y + block.Height);
        }

        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        const double padding = 70;

        var scale = Math.Min((viewWidth - (padding * 2)) / width, (viewHeight - (padding * 2)) / height);
        scale = Math.Clamp(scale, MinZoom, 1.2);

        ZoomTransform.ScaleX = scale;
        ZoomTransform.ScaleY = scale;
        PanTransform.X = ((viewWidth - (width * scale)) / 2) - (minX * scale);
        PanTransform.Y = ((viewHeight - (height * scale)) / 2) - (minY * scale);
        SyncMiniMap();
    }

    private void CenterOn(NodeViewModel node)
    {
        if (_blockPress is not null) return;
        var scale = ZoomTransform.ScaleX;
        PanTransform.X = (Viewport.ActualWidth / 2) - (node.Center.X * scale);
        PanTransform.Y = (Viewport.ActualHeight / 2) - (node.Center.Y * scale);
        SyncMiniMap();
    }

    private void EnsureVisible(NodeViewModel node)
    {
        if (_blockPress is not null) return;
        var scale = ZoomTransform.ScaleX;
        var left = (node.X * scale) + PanTransform.X;
        var top = (node.Y * scale) + PanTransform.Y;
        var right = left + (NodeViewModel.CardWidth * scale);
        var bottom = top + (NodeViewModel.CardHeight * scale);
        const double margin = 48;

        if (left < margin)
        {
            PanTransform.X += margin - left;
        }
        else if (right > Viewport.ActualWidth - margin)
        {
            PanTransform.X -= right - (Viewport.ActualWidth - margin);
        }

        if (top < margin)
        {
            PanTransform.Y += margin - top;
        }
        else if (bottom > Viewport.ActualHeight - margin)
        {
            PanTransform.Y -= bottom - (Viewport.ActualHeight - margin);
        }

        SyncMiniMap();
    }

    // ---- ヒットテスト ----

    private DependencyObject? HitTestAt(Point point)
    {
        var result = VisualTreeHelper.HitTest(Viewport, point);
        return result?.VisualHit;
    }

    private static FrameworkElement? FindNodeElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is NodeViewModel)
            {
                return element;
            }

            source = ParentOf(source);
        }

        return null;
    }

    /// <summary>ブロックの見出し（囲みの中で当たり判定を持つ唯一の部品）を探す。</summary>
    private static FrameworkElement? FindBlockElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is BlockViewModel)
            {
                return element;
            }

            source = ParentOf(source);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = ParentOf(source);
        }

        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject source) =>
        source is Visual or Visual3D
            ? VisualTreeHelper.GetParent(source)
            : (source as FrameworkContentElement)?.Parent;
}
