using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.App.Views;

public partial class TemplateLibraryWindow : Window
{
    public const string DragFormat = "ToDoTree.BranchTemplate";
    private readonly BranchTemplateStore _store;
    private readonly TodoProject? _draft;
    private Point? _dragStart;
    private TemplateItem? _dragItem;
    public event Action<TodoProject>? InsertRequested;

    public TemplateLibraryWindow(TodoProject? draft, BranchTemplateStore? store = null)
    {
        InitializeComponent();
        _draft = draft;
        _store = store ?? new BranchTemplateStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToDoTree", "Templates"));
        SavePanel.Visibility = draft is null ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = draft?.Name ?? "新しい部品";
        DraftSummary.Text = $"選択した {draft?.Nodes.Count ?? 0} 件のステップを保存";
        RefreshTemplates();
        if (draft is not null) Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void RefreshTemplates(Guid? selectedId = null)
    {
        try
        {
            var (templates, errors) = _store.LoadAll();
            var rows = templates.Select(t => new TemplateItem(t)).ToList();
            TemplateList.ItemsSource = rows;
            TemplateList.SelectedItem = rows.FirstOrDefault(t => t.Project.Id == selectedId) ?? rows.FirstOrDefault();
            StatusText.Text = errors.Count > 0 ? $"{errors.Count} 件の部品を読み込めませんでした。"
                : rows.Count == 0 ? "部品はまだありません。カードやブロックを右クリックして「部品として保存」を選んでください。"
                : "つながり・メモ・見積もり・タグを引き継ぎ、未着手・期限なしで配置します。";
            StatusText.ToolTip = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"部品一覧を読み込めませんでした：{ex.Message}";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { StatusText.Text = "部品の名前を入力してください。"; NameBox.Focus(); return; }
        try
        {
            var saved = _store.Save(_draft, NameBox.Text);
            RefreshTemplates(saved.Id);
            StatusText.Text = $"「{saved.Name}」を保存しました。別のプロジェクトでも使えます。";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = $"保存できませんでした：{ex.Message}";
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Details is null || InsertButton is null) return;
        var item = TemplateList.SelectedItem as TemplateItem;
        InsertButton.IsEnabled = item is not null;
        Details.Text = item?.Details ?? "保存した部品を選ぶと、ステップとつながりを確認できます。";
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is TemplateItem item) InsertRequested?.Invoke(item.Project);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(TemplateList);
        _dragItem = (ItemsControl.ContainerFromElement(TemplateList, e.OriginalSource as DependencyObject)
            as ListBoxItem)?.DataContext as TemplateItem;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragStart = null; return; }
        if (_dragStart is not { } start || _dragItem is not { } item) return;
        var delta = e.GetPosition(TemplateList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        DragDrop.DoDragDrop(TemplateList, new DataObject(DragFormat, item.Project), DragDropEffects.Copy);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed record TemplateItem(TodoProject Project)
    {
        public string Name => Project.Name;
        public string Summary => $"{Project.Nodes.Count} ステップ ・ {Project.Edges.Count} 接続";
        public string Details => string.Join("\n", Project.Nodes.Select(n => $"• {n.Title}"))
            + "\n\nつながり\n" + (Project.Edges.Count == 0 ? "なし" : string.Join("\n", Project.Edges.Select(e =>
                $"{Project.Nodes.First(n => n.Id == e.FromId).Title} → {Project.Nodes.First(n => n.Id == e.ToId).Title}")));
    }
}

