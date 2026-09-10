using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ToDoTree.App;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.App.Views;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyWorkspaceCompletedTasks()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "artifacts", "workspace-completed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        var settings = AppSettings.Load(settingsPath);
        var store = new JsonProjectStore();
        var now = DateTimeOffset.Now;
        var project = new TodoProject
        {
            Name = "閉じたプロジェクト / リリース準備",
            Variables = [new ProjectVariable { Name = "製品", Value = "みかん箱" }],
            Nodes = [new() { Title = "{製品} の動作確認", Notes = "画面と保存結果を確認", Status = NodeStatus.Done, CompletedAt = now }],
        };
        var path = Path.Combine(directory, "closed.todotree.json");
        store.Save(path, project);

        WorkspaceViewModel CreateWorkspace(AppSettings config) =>
            (WorkspaceViewModel)Activator.CreateInstance(typeof(WorkspaceViewModel), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [store, config, Path.Combine(directory, "recovery"), false], null)!;
        MainViewModel Add(WorkspaceViewModel workspace, TodoProject data, string? filePath) =>
            (MainViewModel)typeof(WorkspaceViewModel).GetMethod("AddDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(workspace, [data, filePath, null, false, null])!;

        var workspace = CreateWorkspace(settings);
        var document = Add(workspace, store.Load(path), path);
        workspace.ActiveDocument = document;
        workspace.AllCompletedTasks.Refresh();
        Check(workspace.AllCompletedTasks.Rows.Count == 1 && workspace.AllCompletedTasks.Rows[0].IsOpen,
            "The global list uses the open document once, even though its file is registered.");

        document.Nodes[0].Status = NodeStatus.NotStarted;
        workspace.AllCompletedTasks.Refresh();
        Check(workspace.AllCompletedTasks.IsEmpty, "An unsaved completion correction overrides the saved file.");
        document.Undo(); workspace.AllCompletedTasks.Refresh();
        Check(workspace.AllCompletedTasks.Rows.Count == 1, "Undo restores the global completion.");
        document.AutoSave();
        Check(!document.IsDirty, "The test project is saved before closing without a prompt.");
        workspace.CloseProjectCommand.Execute(document);
        workspace.AllCompletedTasks.Refresh();
        Check(!workspace.Documents.Contains(document) && workspace.AllCompletedTasks.Rows.Single().IsOpen == false,
            "Closing the actual project tab retains its saved completion without reopening it.");
        Check(workspace.AllCompletedTasks.Rows.Single().Title == "みかん箱 の動作確認",
            "Closed project titles expand that project's saved variables.");
        Check(AppSettings.Load(settingsPath).KnownProjectPaths.Contains(path), "The closed project's path is persisted in settings.");

        var reopened = CreateWorkspace(AppSettings.Load(settingsPath));
        try
        {
            reopened.AllCompletedTasks.Refresh();
            Check(reopened.Documents.Count == 0 && reopened.AllCompletedTasks.Rows.Count == 1,
                "A fresh workspace lists the closed project after settings reload without opening any tab.");

            var working = new TodoProject { Name = "開いているプロジェクト / 今日の整理", Nodes =
            [new() { Title = "作業メモを整理", Status = NodeStatus.Done, CompletedAt = now.AddSeconds(1) }] };
            var unsaved = Add(reopened, working, null);
            reopened.ActiveDocument = unsaved;
            reopened.AllCompletedTasks.Refresh();
            Check(reopened.AllCompletedTasks.Rows.Count == 2 && reopened.AllCompletedTasks.Rows[0].IsOpen
                && reopened.AllCompletedTasks.Rows[0].FilePath is null, "Open unsaved projects appear alongside closed projects, newest first.");

            var before = File.ReadAllText(path);
            var historyCount = History(unsaved, "_undo");
            reopened.AllCompletedTasks.Refresh();
            Check(File.ReadAllText(path) == before && History(unsaved, "_undo") == historyCount && !unsaved.IsDirty,
                "Reading the global list never saves a project or creates undo history.");

            // 同一パスの大小文字違いは1回だけ読み、他ファイルの障害は個別に表示する。
            var listSettings = AppSettings.Load(settingsPath);
            listSettings.KnownProjectPaths.Add(path.ToUpperInvariant());
            var missing = Path.Combine(directory, "missing.json");
            var corrupt = Path.Combine(directory, "corrupt.json");
            File.WriteAllText(corrupt, "invalid json");
            listSettings.KnownProjectPaths.AddRange([missing, corrupt]);
            var list = new CompletedTasksViewModel(store, listSettings, () => reopened.Documents);
            list.Refresh();
            Check(list.Rows.Count == 2 && list.HasErrors && list.Errors.Contains(missing) && list.Errors.Contains(corrupt),
                "Duplicate paths are deduplicated; missing and corrupt files do not hide valid results.");
            list.Refresh(now.AddDays(1));
            Check(list.IsEmpty, "The global list rolls over at the local date boundary.");
            list.Refresh(now);

            var host = new MainWindow(reopened) { Width = 1500, Height = 1100,
                ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            host.Show(); host.UpdateLayout();
            var button = (Button)host.FindName("AllCompletedTasksButton");
            Check(button.Command == reopened.ShowCompletedTasksCommand && button.IsEnabled,
                "The tab bar exposes the workspace-wide completion command.");
            RenderTaskDetails(host, "completed-global-entry");

            var window = new CompletedTasksWindow(list) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show(); window.UpdateLayout();
            Check(((ListBox)window.FindName("CompletedTasksList")).Items.Count == 2,
                "The global completion window renders both open and closed projects.");
            RenderTaskDetails(window, "completed-global-light");
            ThemeManager.Apply(AppTheme.Dark); window.UpdateLayout();
            RenderTaskDetails(window, "completed-global-dark");
            ThemeManager.Apply(AppTheme.Light);
            window.Close(); host.Close();

            // 閉じた後のファイル側の訂正も、次の更新で反映される。
            project.Nodes[0].Status = NodeStatus.NotStarted;
            project.Nodes[0].CompletedAt = null;
            store.Save(path, project);
            workspace.AllCompletedTasks.Refresh();
            Check(workspace.AllCompletedTasks.IsEmpty, "Reloading a closed file reflects corrected completion state.");
        }
        finally
        {
            reopened.ConfirmCloseAll();
            workspace.ConfirmCloseAll();
        }
    }
}
