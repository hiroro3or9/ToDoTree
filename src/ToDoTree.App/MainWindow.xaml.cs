using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App;

public partial class MainWindow : Window
{
    private WorkspaceViewModel? _workspace;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _workspace = DataContext as WorkspaceViewModel;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _workspace = e.NewValue as WorkspaceViewModel;

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = _workspace?.ActiveDocument;
        if (viewModel is null || e.Key != Key.Enter)
        {
            if (e.Key == Key.Escape)
            {
                Graph.FocusCanvas();
                e.Handled = true;
            }

            return;
        }

        // Enter を押すだけでステップを繋げて増やしていける。
        viewModel.AddNode(viewModel.SelectedNode, sibling: Keyboard.Modifiers == ModifierKeys.Shift);
        e.Handled = true;
    }

    private void OnSidebarDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_workspace?.ActiveDocument is { SelectedNode: { } node } viewModel)
        {
            viewModel.FocusNodeCommand.Execute(node);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_workspace is not null && !_workspace.ConfirmCloseAll())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
