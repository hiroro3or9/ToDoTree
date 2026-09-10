using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ToDoTree.App.Services;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// キャンバス上の 1 つの囲み。
///
/// 位置と大きさは所属ノードから毎回計算する派生値で、保存されない。
/// カードの状態色を主役にしたいので、枠と背景はどれも控えめにしてある。
/// </summary>
public sealed class BlockViewModel(TodoBlock model, MainViewModel owner, int depth = 0) : ObservableObject
{
    /// <summary>選択中に背景を濃くする倍率。既定色の Block.Fill と Block.Selected.Fill の比に合わせてある。</summary>
    private const double SelectedFillFactor = 1.75;

    private BlockBounds _bounds;
    private bool _isSelected;
    private bool _isEditing;
    private bool _isVisible;
    private bool _canMove = true;
    private int _visibleCount;
    private int _totalCount;
    private int _childCount;

    public TodoBlock Model { get; } = model;

    public Guid Id => Model.Id;
    public Guid? ParentBlockId => Model.ParentBlockId;
    public int Depth { get; } = depth;
    public string HierarchyPath => owner.BlockPath(Id);
    private NodeViewModel? _connectionNode;
    public NodeViewModel ConnectionNode
    {
        get
        {
            _connectionNode ??= new NodeViewModel(new ToDoTree.Core.Models.TodoNode { Id = Id }, owner);
            _connectionNode.Model.Title = Title;
            _connectionNode.Model.X = X; _connectionNode.Model.Y = Y;
            _connectionNode.Model.Status = owner.DescendantNodeIds(Id).All(id => owner.Graph.Find(id)?.IsSettled == true)
                ? ToDoTree.Core.Models.NodeStatus.Done : ToDoTree.Core.Models.NodeStatus.NotStarted;
            return _connectionNode;
        }
    }
    public bool IsCollapsed => Model.IsCollapsed && !owner.IsExpandedForFocus(Id);
    public string CollapseActionText => IsCollapsed ? "ブロックを開く" : "ブロックを畳む";
    public string ProgressText
    {
        get
        {
            var members = owner.DescendantNodeIds(Id);
            return $"完了 {members.Count(id => owner.Graph.Find(id)?.IsSettled == true)}/{members.Count}";
        }
    }
    private bool _isDropTarget;
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set { if (SetProperty(ref _isDropTarget, value)) RefreshBrushes(); }
    }
    public void RefreshSummary() => OnPropertyChanged(nameof(IsCollapsed), nameof(CollapseActionText), nameof(ProgressText), nameof(CountText));

    /// <summary>見出しの名前。書き換えは <see cref="MainViewModel"/> の操作単位に乗せる。</summary>
    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == (value ?? string.Empty))
            {
                return;
            }

            // 空にした瞬間に既定名へ戻すと打ち直せないので、確定時に正規化する。
            Model.Title = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Tooltip));
            owner.NotifyBlockRenamed(this);
        }
    }

    // ---- 境界（派生値） ----

    public double X => _bounds.X;

    public double Y => _bounds.Y;

    public double Width => Math.Max(1, _bounds.Width);

    public double Height => Math.Max(1, _bounds.Height);

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public double HeaderHeight => BlockGeometry.HeaderHeight;

    public BlockBounds Bounds => _bounds;

    /// <summary>可視の所属ノードが 1 件でもある。0 件なら囲みごと隠す。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(CountText), nameof(Tooltip));
            }
        }
    }

    public int VisibleCount
    {
        get => _visibleCount;
        private set
        {
            if (SetProperty(ref _visibleCount, value))
            {
                OnPropertyChanged(nameof(CountText), nameof(Tooltip));
            }
        }
    }

    public int ChildCount
    {
        get => _childCount;
        private set => SetProperty(ref _childCount, value, nameof(ChildCount));
    }

    /// <summary>見出しに添える件数。隠れているものがあるときは、その旨も出す。</summary>
    public string CountText
    {
        get
        {
            var tasks = IsCollapsed ? ProgressText : VisibleCount == TotalCount
                ? $"{TotalCount} 件" : $"表示 {VisibleCount} / 全 {TotalCount} 件";
            return ChildCount > 0 ? $"{tasks} / 子 {ChildCount}" : tasks;
        }
    }

    /// <summary>隠れている所属ノードがあるあいだは、見えないものを動かさないよう移動を止める。</summary>
    public bool CanMove
    {
        get => _canMove;
        private set
        {
            if (SetProperty(ref _canMove, value))
            {
                OnPropertyChanged(nameof(Tooltip), nameof(HeaderCursor));
            }
        }
    }

    public string Tooltip => CanMove
        ? $"{Title}（{CountText}）\nドラッグで位置合わせ・Altで自由移動 ・ ダブルクリックで名前を変更"
        : $"{Title}（{CountText}）\n隠れているステップがあるので、いまは動かせません";

    /// <summary>掴めるときだけ「動かせる」カーソルにする。</summary>
    public Cursor HeaderCursor => CanMove ? Cursors.SizeAll : Cursors.Arrow;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BorderBrush), nameof(BorderThickness), nameof(HeaderFill), nameof(ZIndex), nameof(BodyFill));
            }
        }
    }

    /// <summary>見出しの上で名前を書き換えている最中。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(ZIndex));
            }
        }
    }

    /// <summary>選んでいる囲みだけ、ブロック層の中で手前に出す。</summary>
    public int ZIndex => Depth * 100 + (IsEditing ? 30 : IsSelected ? 20 : 10);

    /// <summary>個別色。null は既定色。<see cref="MainViewModel.ApplyBlockColor"/> から書き換える。</summary>
    public string? ColorId => Model.ColorId;

    /// <summary>描くときに使う色。知らない色は既定色として扱う（モデルの値はそのまま残す）。</summary>
    private string? PaintColorId => ColorPalette.Effective(Model.ColorId);

    /// <summary>選んでいる、または移動の受け皿になっている。どちらも同じ強調にする。</summary>
    private bool IsAccented => IsSelected || IsDropTarget;

    // 選択中は色を保ったまま背景だけ濃くする。既定色のときは、これまでどおり専用のキーを使う。
    public Brush BodyFill => IsAccented
        ? (PaintColorId is null
            ? ThemeManager.BrushOf("Block.Selected.Fill")
            : ColorPalette.Emphasize("Block.Fill", PaintColorId, SelectedFillFactor))
        : ColorPalette.BrushOf("Block.Fill", PaintColorId);

    // 枠は色に関わらずアクセント色へ変える。どの色のブロックでも「いま選んでいる」を同じ見え方にするため。
    public Brush BorderBrush => IsAccented
        ? ThemeManager.BrushOf("Block.Selected.Stroke")
        : ColorPalette.BrushOf("Block.Stroke", PaintColorId);

    public Thickness BorderThickness => new(IsAccented ? 2 : 1.2);

    // 見出しの下地は選択中も色を保つ（選択は枠と背景で分かる）。
    public Brush HeaderFill => IsSelected && PaintColorId is null
        ? ThemeManager.BrushOf("Block.Header.Selected.Fill")
        : ColorPalette.BrushOf("Block.Header.Fill", PaintColorId);

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public Brush HeaderText => ThemeManager.BrushOf("Block.Header.Text");

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public Brush CountBrush => ThemeManager.BrushOf("Block.Header.Count");

    /// <summary>
    /// 右クリックの「ブロックに追加」から呼ばれる、この囲みへ入れるコマンド。
    /// 一覧の各行がこの囲みを DataContext に持つので、親をたどらずに繋げられる。
    /// </summary>
    public ICommand AddSelectionCommand => _addSelectionCommand ??= new RelayCommand(
        () => owner.AddSelectionToBlock(this),
        () => owner.CanAddSelectionToBlock);

    private ICommand? _addSelectionCommand;

    public ICommand MoveSelectedBlockHereCommand => _moveSelectedBlockHereCommand ??= new RelayCommand(
        () => owner.MoveSelectedBlockTo(this), () => owner.CanMoveSelectedBlockTo(this));

    private ICommand? _moveSelectedBlockHereCommand;

    /// <summary>境界と件数を計算し直す。ドラッグ中もここだけを更新する。</summary>
    public void Update(BlockBounds? bounds, int visibleCount, int totalCount, int childCount = 0)
    {
        RefreshSummary();
        TotalCount = totalCount;
        VisibleCount = visibleCount;
        ChildCount = childCount;
        OnPropertyChanged(nameof(CountText));
        CanMove = visibleCount == totalCount && totalCount > 0;

        if (bounds is not { } box)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;

        if (Math.Abs(_bounds.X - box.X) > 0.01
            || Math.Abs(_bounds.Y - box.Y) > 0.01
            || Math.Abs(_bounds.Width - box.Width) > 0.01
            || Math.Abs(_bounds.Height - box.Height) > 0.01)
        {
            _bounds = box;
            OnPropertyChanged(nameof(X), nameof(Y), nameof(Width), nameof(Height), nameof(Bounds));
        }
    }

    /// <summary>配色や個別色が変わったあとに塗り直す。</summary>
    public void RefreshBrushes() => OnPropertyChanged(
        nameof(ColorId),
        nameof(BodyFill),
        nameof(BorderBrush),
        nameof(BorderThickness),
        nameof(HeaderFill),
        nameof(HeaderText),
        nameof(CountBrush));

    public void NotifyTitleChanged() => OnPropertyChanged(nameof(Title), nameof(Tooltip));

    public override string ToString() => $"{Title}（{TotalCount} 件）";
}
