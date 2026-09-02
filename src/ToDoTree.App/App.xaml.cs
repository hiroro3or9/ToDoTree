using System.Windows;
using System.Windows.Threading;
using ToDoTree.App.Services;

namespace ToDoTree.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;

        // 前回選んでいた配色で始める。
        ThemeManager.Apply(AppSettings.Load().Theme);

        base.OnStartup(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました。\n\n{e.Exception.Message}",
            "ToDoTree",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
