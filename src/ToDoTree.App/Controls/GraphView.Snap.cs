using System.Windows;
using System.Windows.Input;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Layout;

namespace ToDoTree.App.Controls;

/// <summary>
/// ドラッグ中の位置合わせ。ブロックの見出しでもカードでも、
/// 「開始時の矩形」と「押した位置からの差分」だけを見て補正する。
///
/// 違うのは補正済みの差分をどこへ渡すかだけなので、状態と後始末はここに 1 組だけ置く。
/// 表示モード（カード／ミニマル）の分岐は持たない。カードの寸法は
/// <see cref="NodeViewModel.CardWidth"/> が表示モードごとの値を返すため、
/// 矩形の大きさとして自然に入る。
/// </summary>
public partial class GraphView
{
    /// <summary>いま何を動かしているか。</summary>
    private enum SnapSubject
    {
        None,
        Block,
        Cards,
    }

    private SnapSubject _snapSubject;

    /// <summary>ドラッグ開始時の外接矩形。途中で読み直さない。</summary>
    private BlockBounds _snapStart;

    private SnapTarget[] _snapTargets = [];
    private SnapState _snapState = new();

    /// <summary>最後に見たポインター（ワールド座標）。Alt だけが変わったときに使い直す。</summary>
    private Point _snapPointer;

    /// <summary>開始時に控える、対象が入れ替わっていないかの目印。</summary>
    private (double Width, double Height) _snapNodeSize;

    private Guid[] _snapMembers = [];
    private (int Nodes, int Edges, int Blocks) _snapCounts;
    private LayoutDirection _snapDirection;

    private const string SnapHint = "Altで吸着を解除";

    /// <summary>カードを動かしている間の案内。吸着先が変わるたびに書き換えない。</summary>
    internal const string CardDragHint = "Shiftを押すと、ドロップ先へ所属を変更できます。Altで吸着を解除。";

    private string _beforeSnapStatus = string.Empty;

    /// <summary>カードへ差分を当てている最中。座標の更新から呼び返される後始末を止める。</summary>
    private bool _applyingCardDrag;

    /// <summary>
    /// 移動中はズームと画面移動を止める。座標変換が途中で変わると計算の原点が跳び、
    /// ガイドも古い倍率のまま残る。
    /// </summary>
    private bool IsViewportLocked => _blockPress is not null || _snapSubject == SnapSubject.Cards;

    /// <summary>いま見えている範囲（ワールド座標）。吸着先を画面内に絞るのに使う。</summary>
    private BlockBounds ViewportBounds()
    {
        var zoom = Math.Max(0.01, ZoomTransform.ScaleX);
        return new BlockBounds(-PanTransform.X / zoom, -PanTransform.Y / zoom,
            Viewport.ActualWidth / zoom, Viewport.ActualHeight / zoom);
    }

    private void ClearSnap()
    {
        // 案内を出したままなら、掴む前の文言へ戻す。結果のメッセージは上書きしない。
        if (_viewModel is not null && _viewModel.StatusMessage is SnapHint or CardDragHint)
        {
            _viewModel.StatusMessage = _beforeSnapStatus;
        }

        _snapSubject = SnapSubject.None;
        _snapTargets = [];
        _snapMembers = [];
        _snapState = new();
        AlignmentGuides.Update([], 1, 0, 0);
    }

    /// <summary>ブロックの見出しを掴んだ。囲み同士で揃える。</summary>
    private void BeginBlockSnap(BlockViewModel block)
    {
        BeginSnapSession(SnapSubject.Block, block.Bounds, [.. block.Model.NodeIds]);

        var viewport = ViewportBounds();
        _snapTargets = [.. _viewModel!.Blocks
            .Where(b => b.Id != block.Id && b.IsVisible && b.VisibleCount == b.TotalCount
                && b.Bounds.IntersectsWith(viewport))
            .Select(b => new SnapTarget(b.Id, b.Bounds))];

        _viewModel.StatusMessage = SnapHint;
    }

    /// <summary>
    /// カードを掴んだ。複数選択でも群の外接矩形をひとつ作り、
    /// そこへ差分をまとめて当てるのでカード同士の相対配置は崩れない。
    /// </summary>
    private void BeginCardSnap()
    {
        if (_viewModel is null || _dragGroup.Count == 0)
        {
            return;
        }

        var width = NodeViewModel.CardWidth;
        var height = NodeViewModel.CardHeight;
        var moving = _dragGroup
            .Select(t => new NodeRect(t.Node.Id, t.StartX, t.StartY, width, height))
            .ToList();

        if (CardSnapService.MovingBounds(moving) is not { } start)
        {
            return;
        }

        BeginSnapSession(SnapSubject.Cards, start, [.. _dragGroupIds]);

        var cards = _viewModel.Nodes
            .Where(n => n.IsVisible)
            .Select(n => new NodeRect(n.Id, n.X, n.Y, width, height))
            .ToList();
        var blocks = _viewModel.Blocks
            .Where(b => b.IsVisible)
            .Select(b => new BlockSnapCandidate(b.Id, b.Bounds, b.Model.NodeIds))
            .ToList();

        _snapTargets = [.. CardSnapService.Targets(cards, blocks, _dragGroupIds, ViewportBounds())];
    }

    private void BeginSnapSession(SnapSubject subject, BlockBounds start, Guid[] members)
    {
        _snapSubject = subject;
        _snapStart = start;
        _snapState = new();
        _snapMembers = members;
        _snapNodeSize = (NodeViewModel.CardWidth, NodeViewModel.CardHeight);
        _snapCounts = (_viewModel!.Nodes.Count, _viewModel.Edges.Count, _viewModel.Blocks.Count);
        _snapDirection = _viewModel.Direction;
        _beforeSnapStatus = _viewModel.StatusMessage;
    }

    /// <summary>開始時に見た前提が崩れていないか。崩れていたら古い候補を使わず移動ごとやめる。</summary>
    private bool SnapSessionIsValid()
    {
        if (_viewModel is not { } vm
            || _snapNodeSize != (NodeViewModel.CardWidth, NodeViewModel.CardHeight)
            || _snapDirection != vm.Direction
            || _snapCounts != (vm.Nodes.Count, vm.Edges.Count, vm.Blocks.Count))
        {
            return false;
        }

        return _snapSubject switch
        {
            SnapSubject.Block => _blockPress is { } source
                && source.CanMove && source.IsVisible && vm.Blocks.Contains(source)
                && _snapMembers.SequenceEqual(source.Model.NodeIds)
                && _snapTargets.All(t => vm.Blocks.Any(b => b.Id == t.Id && b.IsVisible
                    && b.VisibleCount == b.TotalCount && b.Bounds == t.Bounds)),

            // 動くのはドラッグ中のカードだけで、それを含む囲みは揃え先から外してある。
            // 揃え先の矩形は開始時のままなので、毎フレーム全件を突き合わせない。
            SnapSubject.Cards => _dragGroup.Count > 0 && _dragGroup.All(t => t.Node.IsVisible),

            _ => false,
        };
    }

    /// <summary>
    /// 補正済みの差分を返す。吸着していない軸と、Alt を押している間は未補正の差分がそのまま返る。
    /// 判定は必ずポインターから求めた未補正の位置で行う。
    /// 表示上の吸着位置で測ると距離が 0 のままになり、外れなくなる。
    /// </summary>
    private Vec2 SnappedDelta(Point pointer, Point origin)
    {
        _snapPointer = pointer;
        var raw = new Vec2(pointer.X - origin.X, pointer.Y - origin.Y);

        if (_snapSubject == SnapSubject.None || _viewModel is null)
        {
            return raw;
        }

        try
        {
            var result = BlockSnapService.Compute(_snapStart, raw, _snapTargets, _snapState,
                ZoomTransform.ScaleX, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            _snapState = result.State;
            AlignmentGuides.Update(result.Guides, ZoomTransform.ScaleX, PanTransform.X, PanTransform.Y);
            return result.Delta;
        }
        catch (ArgumentException)
        {
            EndInteraction();
            return raw;
        }
    }

    private void UpdateBlockSnap(Point pointer)
    {
        if (!_blockDragActive || _viewModel is null)
        {
            return;
        }

        if (!SnapSessionIsValid())
        {
            EndInteraction();
            return;
        }

        var delta = SnappedDelta(pointer, _blockPressWorld);
        if (_blockDragActive)
        {
            _viewModel.UpdateBlockDrag(delta.X, delta.Y);
        }
    }

    /// <summary>カードを「開始位置 ＋ 補正済みの差分」で置き直す。</summary>
    private void UpdateCardDrag(Point world)
    {
        if (_viewModel is null || _dragGroup.Count == 0)
        {
            return;
        }

        if (_snapSubject == SnapSubject.Cards && !SnapSessionIsValid())
        {
            EndInteraction();
            return;
        }

        var delta = SnappedDelta(world, _nodeDragOrigin);
        if (_dragGroup.Count == 0)
        {
            // 途中で取り消された。
            return;
        }

        _applyingCardDrag = true;
        try
        {
            foreach (var (node, startX, startY) in _dragGroup)
            {
                node.X = startX + delta.X;
                node.Y = startY + delta.Y;
            }

            _viewModel.UpdateNodeDragWaypoints(_dragGroupIds, delta.X, delta.Y);
        }
        finally
        {
            _applyingCardDrag = false;
        }

        // 所属の下見はポインターの位置で決める。吸着でカードがずれても、下見と結果が食い違わない。
        UpdateMembershipPreview(world);
    }

    /// <summary>
    /// ドラッグ中の修飾キー。Alt はマウスを動かさなくても即座に反映し、
    /// メニューのアクティブ化へは流さない。
    /// </summary>
    private void OnSnapModifierKey(object sender, KeyEventArgs e)
    {
        var dragging = _blockPress is not null || _snapSubject == SnapSubject.Cards;
        if (!dragging)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
        {
            // Alt は吸着の入切、Shift は所属変更の下見。どちらも同じ更新関数を通す。
            if (_blockDragActive)
            {
                UpdateBlockSnap(_snapPointer);
            }
            else if (_snapSubject == SnapSubject.Cards)
            {
                UpdateCardDrag(_snapPointer);
            }

            e.Handled = true;
        }
        else if (e.RoutedEvent == Keyboard.PreviewKeyDownEvent)
        {
            if (key == Key.Escape)
            {
                EndInteraction();
            }

            // 保存は既存の確定経路へ渡す。他の編集・表示操作はドラッグを終えてから。
            if (!(key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                e.Handled = true;
            }
        }
    }
}
