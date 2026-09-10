using System.Windows;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App.Views;

public partial class CompletedTasksWindow : Window
{
    public CompletedTasksWindow(CompletedTasksViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.Refresh();
        Activated += (_, _) => viewModel.Refresh();
    }
}
