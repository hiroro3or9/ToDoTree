using System.Windows;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Views;

public partial class ProcedureStartWindow : Window
{
    public ProcedureStartWindow(IEnumerable<ProjectVariable> variables)
    {
        Variables = [.. variables.Select(v => v.Clone())];
        InitializeComponent(); DataContext = Variables;
    }
    public string RunName => NameBox.Text;
    public List<ProjectVariable> Variables { get; }
    public DateTimeOffset? Due => DuePicker.SelectedDate is { } date ? new DateTimeOffset(date) : null;
    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;
}
