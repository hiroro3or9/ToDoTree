using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private sealed class BranchMoveTestStore : IProjectStore
    {
        private readonly JsonProjectStore _inner = new();
        public string? FailPath { get; set; }
        public string FileFilter => _inner.FileFilter;
        public string DefaultExtension => _inner.DefaultExtension;
        public TodoProject Load(string path) => _inner.Load(path);
        public void Save(string path, TodoProject project)
        {
            if (path == FailPath) throw new IOException("保存失敗のテスト");
            _inner.Save(path, project);
        }
    }

    private static TodoProject MoveSample()
    {
        var project = new TodoProject { Name = "引っ越し元", Nodes =
        [
            new() { Title = "全体計画", X = 60, Y = 120 },
            new() { Title = "{製品}の開発", X = 360, Y = 120 },
            new() { Title = "完成", Kind = NodeKind.Goal, X = 650, Y = 120,
                Status = NodeStatus.Done, CompletedAt = DateTimeOffset.Now },
            new() { Title = "外部レビュー", X = 60, Y = 330 },
        ], Variables = [new() { Name = "製品", Value = "みかん箱" }] };
        var graph = new TodoGraph(project);
        graph.Connect(project.Nodes[0].Id, project.Nodes[1].Id);
        graph.Connect(project.Nodes[1].Id, project.Nodes[2].Id);
        graph.Connect(project.Nodes[3].Id, project.Nodes[2].Id);
        return project;
    }

    private static void StopMoveWorkspace(WorkspaceViewModel workspace) =>
        ((DispatcherTimer)typeof(WorkspaceViewModel).GetField("_autoSaveTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(workspace)!).Stop();

    private static void VerifyBranchMove()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "branch-move", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var sourcePath = Path.Combine(folder, "source.todotree.json");
        var destinationPath = Path.Combine(folder, "destination.todotree.json");
        var settingsPath = Path.Combine(folder, "settings.json");
        var store = new JsonProjectStore();
        var project = MoveSample();
        var rootId = project.Nodes[1].Id;
        store.Save(sourcePath, project);
        var settings = AppSettings.Load(settingsPath);
        settings.OpenProjects = [new() { FilePath = sourcePath, DocumentId = Guid.NewGuid() }];
        var workspace = new WorkspaceViewModel(store, settings, folder);
        StopMoveWorkspace(workspace);
        var source = workspace.ActiveDocument!;
        source.SelectOnly(source.Nodes.Single(n => n.Id == rootId));
        var before = State(source);
        Check(source.MoveBranchCommand.CanExecute(null), "The branch move menu is enabled for a normal step.");
        Check(source.MoveBranchTo(destinationPath), "Moving a branch succeeds.");
        Check(workspace.Documents.Count == 2, "Moving opens the saved destination as a second tab.");
        var destination = workspace.ActiveDocument!;
        Check(destination != source && destination.Nodes.Count == 2 && source.Nodes.Count == 4,
            "The source has two entrances and the destination contains the complete branch.");
        Check(destination.SelectedNode?.Id == rootId, "The moved root is selected in the destination.");
        Check(store.Load(sourcePath).Nodes.Single(n => n.Id == rootId).ProjectLink is not null,
            "The source's entrance is persisted before reporting success.");
        var entrance = source.Nodes.Single(n => n.Id == rootId);
        Check(entrance.KindLabel.Contains("入口") && entrance.MetaText.Contains("ダブルクリック")
            && entrance.CardTooltip.Contains(destinationPath), "Entrance cards explain how to open their destination.");
        Check(History(source, "_undo") == 1, "Moving creates exactly one source undo entry.");
        source.Undo();
        Check(State(source) == before && File.Exists(destinationPath), "Undo restores the original branch and retains the independent file.");
        source.Redo();
        entrance = source.Nodes.Single(n => n.Id == rootId);
        Check(entrance.IsProjectEntrance && source.Graph.Project.Edges.Count == 2, "Redo restores entrances and incoming connections.");
        source.SelectOnly(entrance);
        Check(!source.MoveBranchCommand.CanExecute(null) && source.OpenProjectLinkCommand.CanExecute(null),
            "An entrance offers navigation rather than another move.");

        destination.Nodes.Single(n => n.Id == rootId).Title = "移動後の編集";
        destination.HideCompleted = true;
        destination.SearchText = "表示を絞り込み中";
        workspace.ActiveDocument = source;
        Check(source.OpenProjectEntrance(entrance), "Entrance activation consumes the double-click action.");
        Check(workspace.ActiveDocument == destination && workspace.Documents.Count == 2
            && destination.SelectedNode?.Title == "移動後の編集" && destination.SearchText == "",
            "Activation reuses the open tab and preserves edits while revealing the target.");
        Check(!source.OpenProjectEntrance(source.Nodes.First(n => !n.IsProjectEntrance)),
            "Ordinary cards retain their normal double-click behavior.");

        var link = entrance.Model.ProjectLink!.Clone();
        var wrong = link.Clone(); wrong.ProjectId = Guid.NewGuid();
        Check(!workspace.TryOpenLinkedProject(source, wrong) && workspace.Documents.Count == 2,
            "A replaced project is rejected without opening another tab.");
        wrong = link.Clone(); wrong.NodeId = Guid.NewGuid();
        Check(!workspace.TryOpenLinkedProject(source, wrong), "A removed target reports a navigation error.");
        wrong = link.Clone(); wrong.FilePath = Path.Combine(folder, "missing.json");
        Check(!workspace.TryOpenLinkedProject(source, wrong), "A missing destination is handled without throwing.");
        Check(source.Save() && destination.Save(), "Both projects can be saved after independent editing.");

        var reopenedSettings = AppSettings.Load(Path.Combine(folder, "reopened-settings.json"));
        var reopened = new WorkspaceViewModel(store, reopenedSettings, Path.Combine(folder, "recovery"), restoreSession: false);
        StopMoveWorkspace(reopened);
        Check(reopened.TryOpenLinkedProject(source, link) && reopened.Documents.Count == 1
            && reopened.ActiveDocument!.SelectedNode?.Title == "移動後の編集",
            "A closed destination can be opened from its persisted path and project ID.");
        Check(reopened.TryOpenLinkedProject(source, link) && reopened.Documents.Count == 1, "Repeated opening does not duplicate a tab.");

        var view = new GraphView { DataContext = source, Width = 1100, Height = 620 };
        var host = new Window { Content = view, Width = 1100, Height = 620, Opacity = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        host.Show();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        source.ZoomFitCommand.Execute(null);
        Render(view, "branch-move-light");
        ThemeManager.Apply(AppTheme.Dark); source.RefreshAll();
        Render(view, "branch-move-dark");
        ThemeManager.Apply(AppTheme.Light); source.RefreshAll();
        var menu = (ContextMenu)view.FindResource("NodeMenu");
        menu.DataContext = source;
        menu.PlacementTarget = view; menu.Opacity = 0; menu.IsOpen = true;
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Check(menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "枝の引っ越し…") && i.Command == source.MoveBranchCommand),
            "The node context menu binds the actual move command.");
        menu.IsOpen = false;
        host.Close();

        // 保存の各段階で失敗させ、元文書・保存内容・Undo/Redoが残ることを確認する。
        foreach (var failAtSource in new[] { false, true })
        {
            var failStore = new BranchMoveTestStore();
            var failSource = Path.Combine(folder, $"fail-source-{failAtSource}.json");
            var failDestination = Path.Combine(folder, $"fail-destination-{failAtSource}.json");
            var original = MoveSample();
            store.Save(failSource, original);
            var vm = new MainViewModel(failStore, AppSettings.Load(Path.Combine(folder, $"fail-{failAtSource}-settings.json")),
                original, failSource, folder);
            vm.SelectOnly(vm.Nodes[1]);
            vm.Nodes[1].Title = "取り消す編集"; vm.Undo();
            vm.SelectOnly(vm.Nodes[1]);
            var snapshot = State(vm);
            var disk = File.ReadAllText(failSource);
            var undo = History(vm, "_undo"); var redo = History(vm, "_redo");
            failStore.FailPath = failAtSource ? failSource : failDestination;
            Check(!vm.MoveBranchTo(failDestination), "Injected save failure rejects the move.");
            Check(State(vm) == snapshot && File.ReadAllText(failSource) == disk
                && History(vm, "_undo") == undo && History(vm, "_redo") == redo,
                "Save failure preserves source data and both history stacks.");
            Check(File.Exists(failDestination) == failAtSource, "Only a successful destination save leaves a recoverable copy.");
        }

        var unsaved = new MainViewModel(store, AppSettings.Load(Path.Combine(folder, "unsaved-settings.json")),
            MoveSample(), null, folder);
        unsaved.SelectOnly(unsaved.Nodes[1]);
        Check(unsaved.MoveBranchTo(Path.Combine(folder, "unsaved-branch.json")) && unsaved.IsDirty
            && store.Load(unsaved.RecoveryFilePath).Nodes.Any(n => n.ProjectLink is not null),
            "An unsaved source is recoverable with its entrances after a move.");
        var existing = new MainViewModel(store, AppSettings.Load(Path.Combine(folder, "existing-settings.json")),
            MoveSample(), null, folder);
        existing.SelectOnly(existing.Nodes[1]);
        var existingSnapshot = State(existing);
        var destinationDisk = File.ReadAllText(destinationPath);
        Check(!existing.MoveBranchTo(destinationPath) && State(existing) == existingSnapshot
            && File.ReadAllText(destinationPath) == destinationDisk, "An existing destination is never overwritten.");
    }
}
