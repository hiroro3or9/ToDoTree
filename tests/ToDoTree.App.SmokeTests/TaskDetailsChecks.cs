using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToDoTree.App;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyTaskDetails()
    {
        var project = new TodoProject { Name = "新機能の確認", Nodes = [new() { Title = "資料を送る", X = 50, Y = 80 }] };
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null, AppContext.BaseDirectory);
        vm.InboxTitle = "アイデアを整理する"; vm.AddInboxCommand.Execute(null);
        var itemId = vm.InboxItems.Single().Id;
        Check(vm.Progress.Total == 1, "Inbox must not contribute to progress.");
        var before = State(vm);
        Check(vm.PlaceInboxItem(itemId, 360, 80), "Inbox placement works.");
        Check(vm.InboxItems.Count == 0 && vm.Nodes.Count == 2, "Placement moves the inbox item.");
        vm.Undo(); Check(State(vm) == before, "One undo restores inbox and removes the card.");
        vm.Redo(); Check(vm.InboxItems.Count == 0 && vm.Nodes.Count == 2, "Redo places once.");
        var id = vm.Nodes[0].Id;
        NodeViewModel N() => vm.Nodes.Single(n => n.Id == id);
        vm.SelectedNode = N();
        N().NewChecklistTitle = "添付を確認する"; N().AddChecklistCommand.Execute(null);
        N().ChecklistItems.Single().IsChecked = true;
        Check(!N().Model.IsSettled, "Checklist completion does not settle the card.");
        vm.Undo(); Check(!N().ChecklistItems.Single().IsChecked, "Checklist check is independently undoable.");
        vm.Redo(); Check(N().ChecklistItems.Single().IsChecked, "Checklist redo restores the check.");
        N().ChecklistItems.Single().DeleteCommand.Execute(null);
        vm.Undo(); Check(N().ChecklistItems.Count == 1, "Checklist deletion is undoable.");
        N().Status = NodeStatus.InProgress;
        N().ToggleBlockCommand.Execute(null);
        N().BlockReason = "先方からの回答待ち";
        Check(N().Readiness == Readiness.Blocked && vm.NextActions.All(a => a.Node.Id != id), "Blocked work is excluded from next actions.");
        N().ToggleBlockCommand.Execute(null);
        Check(N().Readiness == Readiness.InProgress, "Unblocking preserves the previous status.");
        vm.Undo(); Check(N().IsManuallyBlocked, "Undo restores block and reason.");
        var goal = vm.Nodes.Single(n => n.Id != id);
        goal.Kind = NodeKind.Goal; vm.TryConnect(id, goal.Id);
        vm.SelectedNode = goal;
        Check(goal.BlockingCauses.Single().Node.Id == id, "Goal links to the blocking card.");
        vm.FocusBlockingCauseCommand.Execute(N());
        Check(vm.SelectedNode == N() && N().IsVisible, "Cause navigation selects and reveals the card.");

        vm.InboxTitle = "次回の振り返り"; vm.AddInboxCommand.Execute(null);
        vm.AutoSave();
        try
        {
            var recovered = new JsonProjectStore().Load(vm.RecoveryFilePath);
            Check(recovered.Inbox.Single().Title == "次回の振り返り"
                && recovered.Nodes.Single(n => n.Id == id).Checklist.Single().IsChecked
                && recovered.Nodes.Single(n => n.Id == id).IsManuallyBlocked,
                "Unsaved recovery includes inbox, checklist, and blocking.");
        }
        finally { vm.DeleteRecoveryFile(); }
        // MainWindowへテスト専用の公開形状を渡し、実セッション・自動保存を起動しない。
        var window = new MainWindow { DataContext = new TaskDetailsWorkspace(vm), Width = 1500, Height = 1100,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        window.Show(); window.UpdateLayout();
        RenderTaskDetails(window, "task-details-light");
        Check(Descendants(window).OfType<TextBox>().Any(t => t.DataContext is ChecklistItemViewModel), "Checklist editor is rendered.");
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll(); window.UpdateLayout();
        RenderTaskDetails(window, "task-details-dark");
        vm.SelectedNode = goal; window.UpdateLayout();
        RenderTaskDetails(window, "task-goal-dark");
        vm.SelectedNode = N(); window.UpdateLayout();
        var detailScroller = Descendants(window).OfType<ScrollViewer>().Single(s => s.Content is StackPanel
            && Descendants(s).OfType<TextBox>().Any(t => t.DataContext is ChecklistItemViewModel));
        detailScroller.ScrollToVerticalOffset(520); window.UpdateLayout();
        RenderTaskDetails(window, "task-checklist-dark");
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();
        var graphView = (GraphView)window.FindName("Graph");
        var data = new DataObject(MainViewModel.InboxDragFormat, new InboxDragData(vm.DocumentId, vm.InboxItems[0].Id));
        var drag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, graphView, new Point(300, 150)], null)!;
        drag.RoutedEvent = DragDrop.DropEvent;
        typeof(GraphView).GetMethod("OnTemplateDrop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graphView, [graphView, drag]);
        Check(drag.Effects == DragDropEffects.Move && vm.InboxItems.Count == 0, "Graph drop consumes the inbox entry.");
        window.Close();
    }
    private static void RenderTaskDetails(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    public sealed class TaskDetailsWorkspace(MainViewModel vm)
    {
        public MainViewModel ActiveDocument { get; set; } = vm;
        public MainViewModel[] Documents { get; } = [vm];
        public string WindowTitle => "新機能の確認";
    }
}
