using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToDoTree.App;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyTaskHierarchy()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "task-hierarchy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "project.json");
        var store = new JsonProjectStore();
        var project = new TodoProject { Name = "みかん箱のリリース", Nodes =
        [
            new() { Title = "仕様の承認", X = 40, Y = 140 },
            new() { Title = "画面を実装する", X = 360, Y = 140 },
            new() { Title = "公開する", Kind = NodeKind.Goal, X = 680, Y = 140 },
        ] };
        var parentId = project.Nodes[1].Id;
        var graph = new TodoGraph(project);
        graph.Connect(project.Nodes[0].Id, parentId);
        graph.Connect(parentId, project.Nodes[2].Id);
        store.Save(path, project);
        var vm = new MainViewModel(store, AppSettings.Load(Path.Combine(folder, "settings.json")), project, path, folder);
        NodeViewModel N(Guid id) => vm.Nodes.Single(n => n.Id == id);
        vm.SelectOnly(N(parentId));
        var before = State(vm); var undoBefore = History(vm, "_undo");
        vm.ActivateTask(N(parentId));
        Check(vm.CurrentTaskId == parentId && vm.Nodes.All(n => !n.IsVisible),
            "Double-click activation enters the empty interior without showing the outer graph.");
        Check(State(vm) == before && History(vm, "_undo") == undoBefore && !vm.IsDirty,
            "Entering an empty task is navigation, not a data edit.");
        Check(vm.TaskBreadcrumbs.Count == 2 && vm.TaskBreadcrumbs.Last().Title == "画面を実装する",
            "Breadcrumbs identify the current task.");
        var first = vm.AddNode(null, false);
        first.Title = "部品を作る"; first.IsEditing = false;
        var firstId = first.Id;
        var second = vm.AddNode(first, false);
        second.Title = "動作を確かめる"; second.IsEditing = false;
        var secondId = second.Id;
        Check(first.Model.ParentTaskId == parentId && second.Model.ParentTaskId == parentId,
            "New steps and connected successors belong to the current interior.");
        Check(vm.Graph.Project.Edges.Count == 3 && vm.SidebarNodes.Count == 2
            && vm.Nodes.Count(n => n.IsVisible) == 2, "Internal edits retain outer edges and scope the sidebar and canvas.");
        first.Status = NodeStatus.Done;
        Check(first.Status == NodeStatus.NotStarted && vm.StatusMessage.Contains("親作業"),
            "An unresolved external prerequisite prevents completion inside the parent.");
        vm.NavigateTask(null);
        N(project.Nodes[0].Id).Status = NodeStatus.Done;
        var parent = N(parentId);
        Check(parent.HasInternalSteps && parent.InternalProgress.Contains("0／2"), "Parent cards show internal progress.");
        var historyBeforeParentStatus = History(vm, "_undo");
        parent.Status = NodeStatus.Done;
        Check(parent.Status != NodeStatus.Done && History(vm, "_undo") == historyBeforeParentStatus,
            "A parent cannot bypass unfinished internal work with a manual status change.");
        vm.ActivateTask(parent);
        N(firstId).Status = NodeStatus.Done;
        Check(N(parentId).Status == NodeStatus.InProgress, "A child's completion updates parent progress.");
        vm.ActivateTask(N(secondId));
        Check(vm.TaskBreadcrumbs.Count == 3 && vm.Nodes.All(n => !n.IsVisible), "Steps can contain another level.");
        var deep = vm.AddNode(null, false);
        deep.Title = "日本語入力と保存を確認"; deep.IsEditing = false;
        var deepId = deep.Id;
        Check(deep.Model.ParentTaskId == secondId, "A deeper step has the correct direct parent.");
        deep.Status = NodeStatus.Done;
        Check(N(secondId).Status == NodeStatus.Done && N(parentId).Status == NodeStatus.Done,
            "Completion propagates across all enclosing levels.");
        vm.Undo();
        Check(vm.CurrentTaskId == secondId && N(deepId).Status == NodeStatus.NotStarted
            && N(parentId).Status == NodeStatus.InProgress, "Undo restores both child and parent state in the original scope.");
        vm.Redo();
        Check(N(parentId).Status == NodeStatus.Done, "Redo restores nested completion.");
        vm.TaskBreadcrumbs[0].OpenCommand.Execute(null);
        Check(vm.CurrentTaskId is null && vm.Nodes.Count(n => n.IsVisible) == 3, "A breadcrumb returns directly to the project.");
        vm.RevealTaskScope(deepId);
        Check(vm.CurrentTaskId == secondId, "Navigation to an internal task reveals its containing scope.");
        vm.LeaveTaskCommand.Execute(null);
        Check(vm.CurrentTaskId == parentId && vm.SelectedNode?.Id == secondId, "Back selects the enclosing card.");
        var outerPosition = (N(parentId).X, N(parentId).Y);
        vm.AutoLayout();
        Check((N(parentId).X, N(parentId).Y) == outerPosition, "Interior layout never moves the parent in the outer graph.");
        vm.NavigateTask(null);
        vm.SelectOnly(N(parentId));
        var deleteState = State(vm);
        vm.DeleteSelected();
        Check(vm.Nodes.Count == 2 && vm.Graph.Project.Edges.Count == 1, "Deleting a parent removes its entire interior and bridges outer edges.");
        vm.Undo();
        Check(State(vm) == deleteState && vm.Nodes.Any(n => n.Id == deepId), "Undo restores the complete nested structure.");
        vm.NavigateTask(secondId);
        Check(vm.Save(), "Saving from the interior saves the entire document.");
        var reloaded = new MainViewModel(store, AppSettings.Load(Path.Combine(folder, "reload-settings.json")),
            store.Load(path), path, folder);
        Check(reloaded.CurrentTaskId is null && reloaded.Nodes.Count(n => n.IsVisible) == 3
            && reloaded.Graph.Find(deepId)?.ParentTaskId == secondId, "Reload restores nested data and starts at the project scope.");
        reloaded.ActivateTask(reloaded.Nodes.Single(n => n.Id == parentId));
        Check(reloaded.Nodes.Count(n => n.IsVisible) == 2, "The saved interior remains navigable.");
        var template = BranchTemplate.Capture(vm.Graph.Project, [parentId], "画面の部品");
        vm.NavigateTask(parentId);
        vm.InsertTemplate(template, 650, 350);
        var insertedParent = vm.SelectedNodes.First(n => n.HasInternalSteps && n.Id != parentId);
        Check(insertedParent.Model.ParentTaskId == parentId
            && TaskHierarchy.Validate(vm.Graph.Project, TodoProject.CurrentSchemaVersion) is null,
            "Inserting a nested template attaches its roots to the current task and preserves deeper parents.");
        vm.Undo();
        vm.NavigateTask(null);

        N(deepId).Status = NodeStatus.NotStarted;

        // パンくずは実際のMainWindowのボタンから操作し、両テーマを描画する。
        var settings = AppSettings.Load(Path.Combine(folder, "window-settings.json"));
        var workspace = new WorkspaceViewModel(store, settings, folder, restoreSession: false);
        StopMoveWorkspace(workspace);
        workspace.Documents.Add(vm); workspace.ActiveDocument = vm;
        var window = new MainWindow(workspace) { Width = 1500, Height = 920,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Apply(theme); vm.RefreshAll();
            vm.NavigateTask(null);
            var suffix = theme == AppTheme.Light ? "light" : "dark";
            RenderFeatureWindow(window, $"task-hierarchy-overview-{suffix}");
            // WPFが配送するダブルクリックと同じルーティングイベントでカードを開く。
            var view = (GraphView)window.FindName("Graph");
            var card = Descendants(view).OfType<FrameworkElement>()
                .First(e => e.DataContext is NodeViewModel n && n.Id == parentId && e is Border && e.IsVisible);
            var mouse = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent };
            typeof(System.Windows.Input.MouseButtonEventArgs).GetProperty("ClickCount")!.GetSetMethod(true)!.Invoke(mouse, [2]);
            card.RaiseEvent(mouse);
            Check(vm.CurrentTaskId == parentId && mouse.Handled, "A routed double-click enters the task through GraphView.");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            Check(Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "‹ 戻る")).IsEnabled,
                "The visible back button is enabled inside a task.");
            Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "内部ステップを追加")
                && b.Visibility == Visibility.Visible), "The interior exposes an add-step button.");
            RenderFeatureWindow(window, $"task-hierarchy-inside-{suffix}");
            vm.EnterTask(N(secondId));
            window.UpdateLayout();
            Check(vm.TaskBreadcrumbs.Count == 3, "Deep navigation remains visible in the actual window.");
            RenderFeatureWindow(window, $"task-hierarchy-deep-{suffix}");
            var crumb = Descendants(window).OfType<Button>().First(b => b.DataContext is TaskBreadcrumb c && c.Title == project.Name);
            crumb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // RaiseEventだけではButtonBaseのCommandが動かないので、実際のバインド先を実行する。
            crumb.Command.Execute(crumb.CommandParameter);
            Check(vm.CurrentTaskId is null, "The rendered breadcrumb is bound to project navigation.");
        }
        ThemeManager.Apply(AppTheme.Light); vm.RefreshAll();
        Check(vm.Save(), "The UI verification document can be saved without dialogs.");
        window.Close();
    }
}