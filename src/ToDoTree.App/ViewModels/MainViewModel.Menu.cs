using System.Windows.Input;

namespace ToDoTree.App.ViewModels;

/// <summary>キャンバスを右クリックしたときのメニューから呼ぶ操作。</summary>
public sealed partial class MainViewModel
{
    private double _menuX;
    private double _menuY;

    private ICommand? _insertOnEdgeCommand;
    private ICommand? _deleteEdgeCommand;
    private ICommand? _addNodeHereCommand;

    /// <summary>選んでいる線の途中に、新しいステップを挟む。</summary>
    public ICommand InsertOnEdgeCommand => _insertOnEdgeCommand ??=
        new RelayCommand(() => InsertOnEdge(SelectedEdge), () => HasSelectedEdge);

    /// <summary>選んでいる線を外す。</summary>
    public ICommand DeleteEdgeCommand => _deleteEdgeCommand ??=
        new RelayCommand(DeleteSelectedEdge, () => HasSelectedEdge);

    /// <summary>右クリックした場所にステップを足す。</summary>
    public ICommand AddNodeHereCommand => _addNodeHereCommand ??=
        new RelayCommand(() => AddNodeAt(_menuX, _menuY));

    /// <summary>
    /// メニューを出す前に、キャンバスのどこを右クリックしたかを控える。
    /// 「ここにステップを追加」は、この座標に置く。
    /// </summary>
    public void SetMenuAnchor(double x, double y)
    {
        _menuX = x;
        _menuY = y;
    }

    /// <summary>
    /// 線のメニューの見出し。線は細く、当たり判定にも幅があるので、
    /// どれを掴んだのかを名前で確かめられるようにしておく。
    /// </summary>
    public string EdgeMenuHeader => SelectedEdge is { } edge
        ? $"{Shorten(edge.From.Title)}  →  {Shorten(edge.To.Title)}"
        : string.Empty;

    private static string Shorten(string text) =>
        text.Length > 14 ? string.Concat(text.AsSpan(0, 14), "…") : text;
}
