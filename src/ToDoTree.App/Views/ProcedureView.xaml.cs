using System.Windows;
using System.Windows.Controls;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App.Views;

public partial class ProcedureView : UserControl
{
    public ProcedureView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ProcedureViewModel old) old.StartRequested -= OnStart;
            if (e.NewValue is ProcedureViewModel current) current.StartRequested += OnStart;
        };
    }
    private void OnStart()
    {
        if (DataContext is not ProcedureViewModel vm) return;
        var dialog = new ProcedureStartWindow(vm.Data.Definition.Variables) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) vm.StartRun(dialog.RunName, dialog.Variables, dialog.Due);
    }
    private void OnPreviewExpanded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => { if (DataContext is ProcedureViewModel vm) vm.Preview?.ZoomFitCommand.Execute(null); });
}
