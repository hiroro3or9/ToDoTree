using System.Windows.Input;
using ToDoTree.App.Services;

namespace ToDoTree.App.ViewModels;

/// <summary>明るい配色と暗い配色の切り替え。</summary>
public sealed partial class MainViewModel
{
    private ICommand? _toggleThemeCommand;

    public ICommand ToggleThemeCommand => _toggleThemeCommand ??= new RelayCommand(ToggleTheme);

    public bool IsDarkTheme => ThemeManager.IsDark;

    public string ThemeTooltip => ThemeManager.IsDark
        ? "明るい配色に切り替える"
        : "暗い配色に切り替える";

    public void ToggleTheme()
    {
        var theme = ThemeManager.Toggle();

        _settings.Theme = theme;
        _settings.Save();

        // カードや線の色はバインディング経由なので、値を通知し直して塗り替える。
        RefreshAll();

        OnPropertyChanged(nameof(IsDarkTheme), nameof(ThemeTooltip), nameof(ForecastBrush));
        StatusMessage = theme == AppTheme.Dark ? "暗い配色にしました。" : "明るい配色にしました。";
    }
}
