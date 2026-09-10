using System.IO;
using System.Windows;
using System.Windows.Controls;
using ToDoTree.App;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyCompletedTasks()
    {
        var now = DateTimeOffset.Now;
        var project = new TodoProject { Name = "今日の作業を振り返る", Nodes =
        [
            new() { Title = "{製品名} の動作確認", Status = NodeStatus.Done, CompletedAt = now, X = 50, Y = 80 },
            new() { Title = "昨日の作業", Status = NodeStatus.Done, CompletedAt = now.AddDays(-1) },
            new() { Title = "取り消した作業", Status = NodeStatus.Cancelled, CompletedAt = now },
            new() { Title = "これから完了する作業", X = 370, Y = 80 },
            new() { Title = "繰り返しの確認", Repeat = new RepeatProgress { TargetCount = 2 } },
        ], Variables = [new ProjectVariable { Name = "製品名", Value = "みかん箱" }] };
        var block = BlockService.Create(project, [project.Nodes[0].Id, project.Nodes[1].Id], "閉じたブロック").Block!;
        block.IsCollapsed = true;
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null, AppContext.BaseDirectory);
        vm.HideCompleted = true;
        vm.SearchText = "一致しない検索語";
        vm.SelectedTag = "一致しないタグ";
        Check(vm.CompletedTodayNodes.Count == 1 && vm.CompletedTodayNodes[0].DisplayTitle == "みかん箱 の動作確認",
            "Today's completions ignore hidden, collapsed, search and tag filters and expand variables.");
        Check(!vm.IsDirty && History(vm, "_undo") == 0, "Reading the list never changes the project or history.");

        vm.Nodes[3].Status = NodeStatus.Done;
        Check(vm.CompletedTodayNodes.Count == 2 && vm.CompletedTodayNodes[0].Id == project.Nodes[3].Id,
            "Completion immediately appears newest first.");
        vm.Undo();
        Check(vm.CompletedTodayNodes.Count == 1, "Undo removes the completion.");
        vm.Redo();
        Check(vm.CompletedTodayNodes.Count == 2, "Redo restores the completion.");
        vm.Nodes[3].Status = NodeStatus.NotStarted;
        Check(vm.CompletedTodayNodes.Count == 1, "Reopening a task removes it from the list.");

        var repeat = vm.Nodes[4];
        RepeatService.Advance(repeat.Model, now);
        vm.RefreshAll();
        Check(!vm.CompletedTodayNodes.Contains(repeat), "Intermediate repetitions are not completed tasks.");
        RepeatService.Advance(repeat.Model, now);
        vm.RefreshAll();
        Check(vm.CompletedTodayNodes.Contains(repeat), "The final repetition appears in today's list.");
        vm.RefreshCompletedTasks(now.AddDays(1));
        Check(vm.HasNoCompletedToday, "Changing the local date clears the previous day's list.");
        vm.RefreshCompletedTasks(now);

        var path = Path.Combine(AppContext.BaseDirectory, "artifacts", $"completed-{Guid.NewGuid():N}.json");
        var store = new JsonProjectStore();
        try
        {
            store.Save(path, vm.Graph.Project);
            var reopened = new MainViewModel(store, new AppSettings(), store.Load(path), path, AppContext.BaseDirectory);
            Check(reopened.CompletedTodayNodes.Count == 2, "Reopening the saved project reconstructs the completion list.");
            var other = new MainViewModel(store, new AppSettings(), new TodoProject(), null, AppContext.BaseDirectory);
            Check(other.HasNoCompletedToday, "Another project's list is independent.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        vm.IsCompletedTasksExpanded = true;
        var window = new MainWindow(new TaskDetailsWorkspace(vm))
        {
            Width = 1500, Height = 1100, ShowInTaskbar = false, ShowActivated = false, Opacity = 0,
        };
        window.Show();
        window.UpdateLayout();
        var list = (ItemsControl)window.FindName("CompletedTodayList");
        Check(list.Items.Count == 2, "The actual WPF list binds today's completions.");
        var button = Descendants(list).OfType<Button>().Single(b => b.DataContext is NodeViewModel n && n.Id == project.Nodes[0].Id);
        Check(button.Command is not null && button.CommandParameter is NodeViewModel, "The row binds the navigation command and node.");
        NodeViewModel? centered = null;
        vm.CenterOnRequested += (_, node) => centered = node;
        button.Command!.Execute(button.CommandParameter);
        Check(vm.SelectedNode?.Id == project.Nodes[0].Id && vm.SelectedNode.IsVisible && centered?.Id == project.Nodes[0].Id,
            "Clicking a completion reveals the collapsed block, selects the card, and centers it.");
        Check(!vm.HideCompleted && vm.SearchText.Length == 0 && vm.SelectedTag is null,
            "Navigating clears filters that would hide the completed card.");
        RenderTaskDetails(window, "completed-today-light");
        ThemeManager.Apply(AppTheme.Dark); vm.RefreshAll(); window.UpdateLayout();
        RenderTaskDetails(window, "completed-today-dark");
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();
        window.Close();
    }
}
