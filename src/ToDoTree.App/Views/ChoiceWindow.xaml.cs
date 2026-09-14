using System.Windows;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App.Views;

public partial class ChoiceWindow : Window
{
    private readonly MainViewModel _owner;
    private readonly Guid _sourceId;
    public string SourceTitle { get; }
    public List<ChoiceOption> Options { get; }
    public ChoiceWindow(MainViewModel owner, NodeViewModel source)
    {
        _owner = owner; _sourceId = source.Id; SourceTitle = source.DisplayTitle;
        Options = owner.Graph.Project.Edges.Where(e => e.FromId == source.Id).Select(e => new ChoiceOption
        {
            Id = e.Id, Title = owner.Nodes.FirstOrDefault(n => n.Id == e.ToId)?.DisplayTitle
                ?? owner.Graph.Project.Blocks.FirstOrDefault(b => b.Id == e.ToId)?.Title ?? "道",
            Reason = e.DecisionReason,
        }).ToList();
        InitializeComponent(); DataContext = this;
        OptionsList.SelectedValue = source.Model.SelectedChoiceEdgeId;
    }
    private void Clear_Click(object sender, RoutedEventArgs e) => OptionsList.SelectedIndex = -1;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _owner.ApplyChoice(_sourceId, EnabledChoice.IsChecked == true, OptionsList.SelectedValue as Guid?,
                Options.ToDictionary(o => o.Id, o => o.Reason));
            DialogResult = true;
        }
        catch (InvalidOperationException ex) { ErrorText.Text = ex.Message; }
    }
}

public sealed class ChoiceOption
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Reason { get; set; } = "";
}
