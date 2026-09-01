using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _viewModel = DataContext as MainViewModel;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _viewModel = e.NewValue as MainViewModel;

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.Key != Key.Enter)
        {
            if (e.Key == Key.Escape)
            {
                Graph.FocusCanvas();
                e.Handled = true;
            }

            return;
        }

        // Enter を押すだけでステップを繋げて増やしていける。
        _viewModel.AddNode(_viewModel.SelectedNode, sibling: Keyboard.Modifiers == ModifierKeys.Shift);
        e.Handled = true;
    }

    private void OnSidebarDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.SelectedNode is { } node)
        {
            _viewModel.FocusNodeCommand.Execute(node);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel is not null && !_viewModel.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
