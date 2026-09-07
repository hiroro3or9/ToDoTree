using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using ToDoTree.App.Services;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// 右クリックに出す色見本の 1 行。
/// ブロック用と線用で、見本の引き方と適用先だけが違う。
/// </summary>
public sealed class ColorChoiceViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly bool _forEdge;
    private ICommand? _applyCommand;

    public ColorChoiceViewModel(ColorPreset? preset, bool forEdge, MainViewModel owner)
    {
        Preset = preset;
        _forEdge = forEdge;
        _owner = owner;

        // 選んでいる相手が変われば、印を付け直す。
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    /// <summary>null は「色を外す」行。10 番目の色ではない。</summary>
    public ColorPreset? Preset { get; }

    public string? ColorId => Preset?.Id;

    public string DisplayName => Preset?.DisplayName ?? "既定";

    /// <summary>
    /// 見本の塗り。囲みの見出しの下地はどれもほぼ白で 13px では見分けが付かないので、
    /// 見本には彩度のある枠の色を使う。線は普段の色をそのまま出す。
    /// </summary>
    public Brush Swatch => _forEdge
        ? ColorPalette.BrushOf("Edge.Normal", ColorId)
        : ColorPalette.BrushOf("Block.Stroke", ColorId);

    public Brush SwatchStroke => Swatch;

    /// <summary>
    /// いま選んでいる相手に付いている色か。
    /// 知らない色が付いているときは、どの行にも印が付かない（既定色で描いてはいるが、既定ではない）。
    /// </summary>
    public bool IsCurrent => string.Equals(
        _forEdge ? _owner.SelectedEdge?.Model.ColorId : _owner.SelectedBlock?.ColorId,
        ColorId,
        StringComparison.Ordinal);

    public ICommand ApplyCommand => _applyCommand ??= new RelayCommand(Apply);

    /// <summary>配色や選択が変わったあとに引き直す。</summary>
    public void Refresh() => OnPropertyChanged(nameof(Swatch), nameof(SwatchStroke), nameof(IsCurrent));

    private void Apply()
    {
        if (_forEdge)
        {
            _owner.ApplyEdgeColor(ColorId);
        }
        else
        {
            _owner.ApplyBlockColor(ColorId);
        }
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedBlock) or nameof(MainViewModel.SelectedEdge))
        {
            Refresh();
        }
    }

    public override string ToString() => DisplayName;
}
