using System.Windows.Input;
using ToDoTree.App.Services;
using ToDoTree.Core.Layout;

namespace ToDoTree.App.ViewModels;

/// <summary>カード表示と、丸ひとつのミニマル表示の切り替え。</summary>
public sealed partial class MainViewModel
{
    private ICommand? _toggleNodeStyleCommand;

    public ICommand ToggleNodeStyleCommand => _toggleNodeStyleCommand ??= new RelayCommand(ToggleNodeStyle);

    public bool IsMinimalView => NodeMetrics.IsMinimal;

    /// <summary>ボタンには「押したら何になるか」を出す。</summary>
    public string NodeStyleLabel => NodeMetrics.IsMinimal ? "カード表示" : "ミニマル";

    public string NodeStyleTooltip => NodeMetrics.IsMinimal
        ? "カード表示に戻す (Ctrl+Shift+M)"
        : "丸と線だけの表示にする (Ctrl+Shift+M)";

    public void ToggleNodeStyle()
    {
        var style = NodeMetrics.Toggle();

        _settings.NodeStyle = style;
        _settings.Save();

        // 座標は保存されるデータなので、表示を変えただけでは動かさない。
        // 詰め直したいときは Ctrl+L（自動整列）で、この表示の間隔に並び直す。
        RefreshAll();
        NotifyVisualsChanged();

        OnPropertyChanged(nameof(IsMinimalView), nameof(NodeStyleLabel), nameof(NodeStyleTooltip));

        // 箱が小さくなったぶんだけ余白が増えるので、全体表示にして見え方を揃える。
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);

        StatusMessage = style == NodeStyle.Minimal
            ? "ミニマル表示にしました。Ctrl+L で、この表示に合わせて詰め直せます。"
            : "カード表示に戻しました。";
    }
}
