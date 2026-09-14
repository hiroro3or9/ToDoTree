using System.Windows;
using System.Windows.Controls;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App.Views;

public partial class CaptureSpecimenWindow : Window
{
    private readonly MainViewModel _owner;
    private readonly Guid _goalId;
    public CaptureSpecimenWindow(MainViewModel owner, NodeViewModel goal)
    {
        _owner = owner; _goalId = goal.Id;
        InitializeComponent(); GoalTitle.Text = goal.DisplayTitle;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _owner.CaptureSpecimen(_goalId, ((ComboBoxItem)Category.SelectedItem).Content.ToString()!, Reflection.Text);
            DialogResult = true;
        }
        catch (InvalidOperationException ex) { ErrorText.Text = ex.Message; }
    }
}
