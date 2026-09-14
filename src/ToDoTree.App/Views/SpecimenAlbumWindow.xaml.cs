using System.Windows;
using System.Windows.Controls;
using ToDoTree.App.Controls;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Models;

namespace ToDoTree.App.Views;

public partial class SpecimenAlbumWindow : Window
{
    private readonly MainViewModel _owner;
    public SpecimenAlbumWindow(MainViewModel owner)
    {
        _owner = owner; InitializeComponent();
        ProjectTitle.Text = $"{owner.ProjectName} — 作ったもの、学んだこと、その道のり。";
        RefreshCards();
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsInitialized) RefreshCards(); }
    private void RefreshCards()
    {
        if (Search is null || Cards is null) return;
        var category = ((ComboBoxItem)CategoryFilter.SelectedItem).Content.ToString();
        var query = Search.Text.Trim();
        var entries = _owner.Graph.Project.Specimens.Where(s => (category == "すべて" || s.Category == category)
            && (s.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || s.Reflection.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(s => s.CompletedAt).ToList();
        Cards.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _owner.Graph.Project.Specimens.Count == 0
            ? "まだ標本がありません。完了したゴールを選び、編集メニューから「標本帳に残す」を選んでください。"
            : "この条件に合う標本はありません。";
    }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not AchievementSpecimen specimen) return;
        var window = new Window { Title = specimen.Title, Owner = this, Width = 940, Height = 720,
            MinWidth = 600, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty, "Brush.Window");
        window.SetResourceReference(ForegroundProperty, "Brush.Text");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = specimen.Title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"{specimen.Category} ・ {specimen.CompletedAt:yyyy/MM/dd} 達成", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new SpecimenGraph { Specimen = specimen, Height = 320 });
        panel.Children.Add(new TextBlock { Text = specimen.Reflection, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
        foreach (var node in specimen.Nodes.OrderBy(n => n.X).ThenBy(n => n.Y))
        {
            var skipped = specimen.SkippedNodeIds.Contains(node.Id);
            panel.Children.Add(new TextBlock { Text = $"{(skipped ? "見送り" : node.Status == NodeStatus.Done ? "完了" : "取り消し")}  ·  {node.Title}",
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
            if (!string.IsNullOrWhiteSpace(node.Notes)) panel.Children.Add(new TextBlock { Text = node.Notes, TextWrapping = TextWrapping.Wrap });
            foreach (var edge in specimen.Edges.Where(edge => edge.FromId == node.Id))
            {
                var target = specimen.Nodes.First(n => n.Id == edge.ToId);
                var state = node.IsChoice ? node.SelectedChoiceEdgeId == edge.Id ? "採用" : "見送り" : "次へ";
                panel.Children.Add(new TextBlock { Text = $"→ {target.Title}（{state}）{(string.IsNullOrWhiteSpace(edge.DecisionReason) ? "" : "：" + edge.DecisionReason)}", TextWrapping = TextWrapping.Wrap });
            }
        }
        var remove = new Button { Content = "この標本を外す（元のタスクは保持）", Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        remove.SetResourceReference(StyleProperty, "Btn.Outline");
        remove.Click += (_, _) => { _owner.RemoveSpecimen(specimen.Id); window.Close(); RefreshCards(); };
        panel.Children.Add(remove);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
    }
}
