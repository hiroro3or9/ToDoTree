using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App;

public partial class MainWindow : Window
{
    private WorkspaceViewModel? _workspace;

    public MainWindow() : this(new WorkspaceViewModel()) { }

    /// <summary>Allows isolated UI hosts to supply a workspace without restoring the user's session.</summary>
    public MainWindow(object workspace)
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataContext = workspace;
        _workspace = DataContext as WorkspaceViewModel;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _workspace = e.NewValue as WorkspaceViewModel;

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_workspace?.ActiveDocument is { IsExecutionView: true, Procedure: { } procedure } &&
            (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key is Key.Z or Key.Y && Keyboard.FocusedElement is not TextBox)
            {
                var command = e.Key == Key.Z ? procedure.UndoCommand : procedure.RedoCommand;
                if (command.CanExecute(null)) command.Execute(null);
                e.Handled = true;
            }
            else if (e.Key is not (Key.S or Key.N or Key.O or Key.C or Key.V or Key.X or Key.A or Key.Z or Key.Y)
                && !(e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
                e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        // IME の変換確定 (Key.ImeProcessed) は検索結果への移動に使わない。
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape)
        {
            _workspace?.ActiveDocument?.ClearSearchCommand.Execute(null);
            Graph.FocusCanvas();
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Enter)
        {
            if (SidebarList.Items.Count > 0)
            {
                if (SidebarList.SelectedIndex < 0) SidebarList.SelectedIndex = 0;
                if (e.Key == Key.Enter)
                {
                    FocusSidebarSelection();
                }
                else
                {
                    SidebarList.ScrollIntoView(SidebarList.SelectedItem);
                    SidebarList.UpdateLayout();
                    if (SidebarList.ItemContainerGenerator.ContainerFromItem(SidebarList.SelectedItem) is ListBoxItem item)
                        item.Focus();
                }
            }
            e.Handled = true;
        }
    }

    private void OnSidebarKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Enter)
        {
            FocusSidebarSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Graph.FocusCanvas();
            e.Handled = true;
        }
    }

    private void FocusSidebarSelection()
    {
        if (SidebarList.SelectedItem is NodeViewModel node && _workspace?.ActiveDocument is { } viewModel)
        {
            viewModel.FocusNodeCommand.Execute(node);
            Graph.FocusCanvas();
        }
    }

    private void OnToolbarMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }
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
        FocusSidebarSelection();
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
