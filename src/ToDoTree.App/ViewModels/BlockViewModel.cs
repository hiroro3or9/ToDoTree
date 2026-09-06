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
public sealed class BlockViewModel(TodoBlock model, MainViewModel owner) : ObservableObject
{
    private BlockBounds _bounds;
    private bool _isSelected;
    private bool _isEditing;
    private bool _isVisible;
    private bool _canMove = true;
    private int _visibleCount;
    private int _totalCount;

    public TodoBlock Model { get; } = model;

    public Guid Id => Model.Id;

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

    /// <summary>見出しに添える件数。隠れているものがあるときは、その旨も出す。</summary>
    public string CountText => VisibleCount == TotalCount
        ? $"{TotalCount} 件"
        : $"表示 {VisibleCount} / 全 {TotalCount} 件";

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
    public int ZIndex => IsEditing ? 30 : IsSelected ? 20 : 10;

    public Brush BodyFill => ThemeManager.BrushOf(IsSelected ? "Block.Selected.Fill" : "Block.Fill");

    public Brush BorderBrush => ThemeManager.BrushOf(IsSelected ? "Block.Selected.Stroke" : "Block.Stroke");

    public Thickness BorderThickness => new(IsSelected ? 2 : 1.2);

    public Brush HeaderFill => ThemeManager.BrushOf(IsSelected ? "Block.Header.Selected.Fill" : "Block.Header.Fill");

    public Brush HeaderText => ThemeManager.BrushOf("Block.Header.Text");

    public Brush CountBrush => ThemeManager.BrushOf("Block.Header.Count");

    /// <summary>
    /// 右クリックの「ブロックに追加」から呼ばれる、この囲みへ入れるコマンド。
    /// 一覧の各行がこの囲みを DataContext に持つので、親をたどらずに繋げられる。
    /// </summary>
    public ICommand AddSelectionCommand => _addSelectionCommand ??= new RelayCommand(
        () => owner.AddSelectionToBlock(this),
        () => owner.CanAddSelectionToBlock);

    private ICommand? _addSelectionCommand;

    /// <summary>境界と件数を計算し直す。ドラッグ中もここだけを更新する。</summary>
    public void Update(BlockBounds? bounds, int visibleCount, int totalCount)
    {
        TotalCount = totalCount;
        VisibleCount = visibleCount;
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

    /// <summary>配色が変わったあとに塗り直す。</summary>
    public void RefreshBrushes() => OnPropertyChanged(
        nameof(BodyFill),
        nameof(BorderBrush),
        nameof(BorderThickness),
        nameof(HeaderFill),
        nameof(HeaderText),
        nameof(CountBrush));

    public void NotifyTitleChanged() => OnPropertyChanged(nameof(Title), nameof(Tooltip));

    public override string ToString() => $"{Title}（{TotalCount} 件）";
}
