using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private sealed class ProcedureTestStore : IProjectStore
    {
        public bool Fail { get; set; }
        public TodoProject? Saved { get; private set; }
        public int Saves { get; private set; }
        public string FileFilter => "JSON|*.json";
        public string DefaultExtension => ".json";
        public TodoProject Load(string path) => Saved!.DeepClone();
        public void Save(string path, TodoProject project)
        {
            if (Fail) throw new IOException("検証用の保存失敗");
            ProcedureValidation.ValidateDocument(project, project.SchemaVersion);
            Saved = project.DeepClone(); Saves++;
        }
    }

    private static void VerifyProcedures()
    {
        var source = new TodoProject { Name = "毎週のバックアップ", Variables = [new() { Name = "保存先", Value = "共有サーバー" }], Nodes = [
            new() { Title = "バックアップを作成", Notes = "対象ファイルをまとめ、保存できる状態にします。", X = 50, Y = 160,
                Checklist = [new() { Title = "ファイルの数を確認する" }] },
            new() { Title = "{保存先}へ転送", Notes = "転送の完了を確認します。", X = 400, Y = 160 },
            new() { Title = "復元を2回確認", Notes = "テスト環境でファイルを開き、内容を確認します。", X = 750, Y = 160,
                Repeat = new() { TargetCount = 2 } },
        ] };
        var graph = new TodoGraph(source);
        graph.Connect(source.Nodes[0].Id, source.Nodes[1].Id); graph.Connect(source.Nodes[1].Id, source.Nodes[2].Id);
        var store = new ProcedureTestStore();
        var owner = new MainViewModel(store, new AppSettings(), ProcedureService.Create(source),
            Path.Combine(AppContext.BaseDirectory, "artifacts", "procedure-test.json"), AppContext.BaseDirectory);
        var vm = owner.Procedure!;
        Check(owner.IsExecutionView && vm.CanStart && vm.Steps.Count == 0, "A procedure opens on its execution page without fake tasks.");
        Check(vm.StartRun("9月第2週", source.Variables, null), "Starting persists the first run.");
        Check(store.Saved!.Procedure!.Runs.Count == 1 && store.Saved.Nodes.Count == 0, "Only the envelope owns persisted run data.");
        Check(vm.Steps.Count == 3 && vm.SelectedStep?.Node.Id == source.Nodes[0].Id, "Execution follows the graph order.");
        var firstId = vm.SelectedStep!.Node.Id;
        vm.DraftNote = "対象を確認しました。";
        owner.AutoSave();
        Check(!vm.HasPendingDetails && store.Saved.Procedure.Runs[0].Current.Notes[firstId] == "対象を確認しました。", "Autosave flushes execution notes.");
        vm.AdvanceCommand.Execute(null);
        Check(vm.CurrentRun!.Status == RunStatus.Running && vm.SelectedStep!.Node.Status == NodeStatus.Done, "A completed card does not bypass an unchecked checklist.");
        vm.ToggleCheckCommand.Execute(vm.Checks.Single());
        Check(vm.SelectedStep!.Node.Checklist.Single().IsChecked, "Checklist commands persist through the run service.");
        vm.SelectedStep = vm.Steps[1];
        vm.DraftNote = "転送まで完了。復元環境を待っています。";
        vm.AdvanceCommand.Execute(null);
        Check(vm.CurrentRun.Current.Notes[source.Nodes[1].Id].StartsWith("転送まで"), "Completing a step includes its pending note in the same save.");
        vm.SelectedStep = vm.Steps[2];
        vm.AdvanceCommand.Execute(null);
        Check(vm.SelectedStep!.Node.Repeat!.CompletedCount == 1, "Only one repeat achievement is recorded.");
        vm.UndoCommand.Execute(null);
        Check(vm.SelectedStep!.Node.Repeat!.CompletedCount == 0 && vm.CurrentRun.Events[^1].Kind == RunEventKind.Undo, "Run undo retains the event trail.");
        vm.RedoCommand.Execute(null);
        Check(vm.SelectedStep!.Node.Repeat!.CompletedCount == 1, "Run redo restores repeat progress.");
        vm.DraftBlocked = true; vm.DraftReason = "テスト環境が使用中"; vm.PauseNote = "環境が空いたら2回目を確認する";
        vm.PauseCommand.Execute(null);
        Check(vm.CurrentRun.Status == RunStatus.Paused && vm.CurrentRun.Checkpoints.Count == 1, "Pause writes a durable checkpoint.");
        Check(vm.CurrentRun.Checkpoints[0].State.Graph.Nodes[2].BlockReason == "テスト環境が使用中", "Pause includes pending blocking details.");
        Check(!vm.CanEdit && !vm.AdvanceCommand.CanExecute(null), "Paused runs expose no progress mutations.");

        var restored = new MainViewModel(store, new AppSettings(), store.Load(""), owner.FilePath, AppContext.BaseDirectory);
        var resumed = restored.Procedure!;
        Check(resumed.CurrentRun!.Status == RunStatus.Paused && resumed.SelectedStep!.Node.Id == source.Nodes[2].Id, "Reopening recovers the bookmarked node.");
        resumed.ResumeCommand.Execute(null);
        Check(resumed.SelectedStep!.Node.Repeat!.CompletedCount == 1 && resumed.CanEdit, "Resuming keeps the completed count.");
        resumed.DraftBlocked = false; resumed.ApplyDetailsCommand.Execute(null);
        var beforeFailure = ProcedureService.Serialize(resumed.Data);
        store.Fail = true;
        resumed.AdvanceCommand.Execute(null);
        Check(ProcedureService.Serialize(resumed.Data) == beforeFailure && resumed.CanEdit, "Failed final save leaves the final achievement and completion uncommitted.");
        Check(resumed.Feedback.Contains("保存失敗"), "Failed persistence is visible.");
        store.Fail = false;
        resumed.AdvanceCommand.Execute(null);
        Check(resumed.CurrentRun!.Status == RunStatus.Completed && !resumed.CanEdit, "Retry completes exactly once.");
        Check(resumed.CurrentRun.Events.Count(e => e.Kind == RunEventKind.Completed) == 1, "Retry adds no duplicate completion events.");
        resumed.CorrectCommand.Execute(null);
        Check(resumed.CurrentRun.Status == RunStatus.Running && resumed.CurrentRun.Checkpoints.Count == 2, "Explicit correction retains the completed snapshot.");
        resumed.AdvanceCommand.Execute(null); resumed.AdvanceCommand.Execute(null);
        Check(resumed.StartRun("9月第3週", source.Variables, null), "The next execution can begin after completion.");
        Check(resumed.CurrentRun!.Current.Graph.Nodes.All(n => n.Status == NodeStatus.NotStarted) && resumed.Data.Runs.Count == 2,
            "The new run starts clean and the previous run remains.");
        var currentRunId = resumed.CurrentRun.Id;
        resumed.SelectedRun = resumed.Runs.Last();
        resumed.SelectedCheckpoint = resumed.Checkpoints.Last();
        Check(!resumed.CanEdit && resumed.ReadOnlyLabel.Contains("読み取り専用"), "Historical checkpoints are read-only.");
        Check(resumed.SelectedStep is not null && resumed.Steps[2].Node.Repeat!.CompletedCount == 1, "Historical inspection renders the paused progress.");
        Check(!resumed.CorrectCommand.CanExecute(null), "A run with a successor cannot be reopened.");
        resumed.LatestCommand.Execute(null);
        Check(resumed.CurrentRun!.Id == currentRunId && resumed.CanEdit, "Returning to the latest run does not restore historical data over it.");

        resumed.DraftNote = "保存失敗でも保持する入力";
        store.Fail = true;
        var selected = resumed.SelectedStep!.Node.Id;
        resumed.SelectedStep = resumed.Steps[1];
        Check(resumed.SelectedStep!.Node.Id == selected && resumed.HasPendingDetails, "Navigation does not discard a note whose save failed.");
        store.Fail = false; resumed.SelectedStep = resumed.Steps[1];
        Check(resumed.SelectedStep!.Node.Id == source.Nodes[1].Id && !resumed.HasPendingDetails, "Navigation retries pending input before switching steps.");

        var firstDefinition = ProcedureService.Serialize(resumed.Data.Runs[0].Definition);
        restored.ShowDefinitionCommand.Execute(null);
        restored.Nodes[0].Title = "更新した作成手順";
        restored.AutoSave();
        Check(resumed.Data.Revision == 2 && ProcedureService.Serialize(resumed.Data.Runs[0].Definition) == firstDefinition,
            "Editing the definition publishes a revision without modifying previous runs.");
        restored.ShowExecutionCommand.Execute(null);
        Check(restored.IsExecutionView, "The normal graph editor and execution view share one project tab.");

        var view = new ProcedureView { DataContext = resumed, Width = 1200, Height = 740 };
        var host = new Window { Content = view, Width = 1200, Height = 740, Opacity = 0,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        host.Show();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        RenderTaskDetails(host, "procedure-running-light");
        ThemeManager.Apply(AppTheme.Dark);
        RenderTaskDetails(host, "procedure-running-dark");
        Check(Descendants(view).OfType<Button>().Any(b => Equals(b.Content, "中断する")), "The procedure page exposes the pause command.");
        var notesBox = Descendants(view).OfType<TextBox>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "今回の実施メモ");
        notesBox.Text = "画面から入力したメモ";
        notesBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        restored.AutoSave();
        Check(!resumed.HasPendingDetails && resumed.CurrentRun!.Current.Notes[resumed.SelectedStep!.Node.Id] == "画面から入力したメモ",
            "The live text binding survives autosave and view refresh.");
        notesBox.Text = "失敗時の未保存入力";
        notesBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        var stepList = Descendants(view).OfType<ListBox>().Single();
        var stableSelection = resumed.SelectedStep!.Node.Id;
        store.Fail = true; stepList.SelectedItem = resumed.Steps.First(s => s.Node.Id != stableSelection);
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Check(resumed.HasPendingDetails && ((ProcedureStep)stepList.SelectedItem).Node.Id == stableSelection,
            "A rejected selection is restored in the actual list control.");
        store.Fail = false; resumed.ApplyDetailsCommand.Execute(null);
        resumed.SelectedRun = resumed.Runs.Last();
        resumed.SelectedCheckpoint = resumed.Checkpoints.Last();
        RenderTaskDetails(host, "procedure-history-dark");
        host.Close(); ThemeManager.Apply(AppTheme.Light);
        var main = new ToDoTree.App.MainWindow(new TaskDetailsWorkspace(restored))
        { Width = 1440, Height = 960, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        main.Show(); main.UpdateLayout();
        var procedureControl = Descendants(main).OfType<ProcedureView>().Single();
        Check(procedureControl.IsVisible && !((FrameworkElement)main.FindName("Graph")).IsVisible, "Execution view replaces the normal graph in the main window.");
        RenderTaskDetails(main, "procedure-main-history");
        restored.ShowDefinitionCommand.Execute(null); main.UpdateLayout();
        Check(!procedureControl.IsVisible && ((FrameworkElement)main.FindName("Graph")).IsVisible, "Definition mode restores the existing graph editor.");
        RenderTaskDetails(main, "procedure-main-definition");
        main.Close();
    }
}
