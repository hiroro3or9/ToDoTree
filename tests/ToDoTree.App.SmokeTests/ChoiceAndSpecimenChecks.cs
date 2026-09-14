using System.Windows;
using System.Windows.Controls;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyChoicesAndSpecimens()
    {
        var source = new TodoNode { Title = "時計を手に入れる", Status = NodeStatus.Done, X = 40, Y = 150 };
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), new TodoProject { Name = "自分の実績帳", Nodes = [source] }, null, AppContext.BaseDirectory);
        vm.SelectedNode = vm.Nodes[0];
        var before = State(vm); vm.AddChoiceBranch();
        Check(vm.Nodes.Count == 3 && vm.Nodes.Skip(1).All(n => n.IsPendingBranch), "Adding alternatives gates both paths.");
        vm.Undo(); Check(State(vm) == before, "Adding the choice is one undo."); vm.Redo();
        var sourceId = source.Id;
        var edge = vm.Graph.Project.Edges[0];
        var reasonEdge = vm.Graph.Project.Edges[1];
        vm.ApplyChoice(sourceId, true, edge.Id, new Dictionary<Guid, string> { [reasonEdge.Id] = "仕組みを学ぶため、今回は見送り" });
        var choiceState = State(vm);
        var skipped = vm.Nodes.Single(n => n.Id == reasonEdge.ToId);
        skipped.Status = NodeStatus.Done;
        Check(skipped.Model.Status == NodeStatus.NotStarted && skipped.CardOpacity < 0.5, "Skipped tasks cannot be completed and are faded.");
        Check(skipped.CardTooltip.Contains("仕組みを学ぶ"), "Decision reasons appear on the skipped card.");
        vm.Undo(); Check(vm.Graph.Find(sourceId)!.SelectedChoiceEdgeId is null, "Undo restores the unresolved choice.");
        vm.Redo(); Check(State(vm) == choiceState, "Redo restores reasons and selection together.");

        var goalVm = vm.AddNodeAt(680, 150);
        goalVm.IsEditing = false; goalVm.Title = "自作時計が完成"; goalVm.Kind = NodeKind.Goal;
        var goal = goalVm.Model;
        vm.TryConnect(edge.ToId, goal.Id); vm.TryConnect(reasonEdge.ToId, goal.Id);
        vm.Nodes.Single(n => n.Id == edge.ToId).Status = NodeStatus.Done;
        goalVm.Status = NodeStatus.Done;
        var specimen = vm.CaptureSpecimen(goal.Id, "作ったもの", "配線と時刻合わせを学んだ。次はケースにもこだわりたい。");
        Check(vm.Graph.Project.Specimens.Count == 1, "Capture adds a specimen.");
        vm.Undo(); Check(vm.Graph.Project.Specimens.Count == 0, "Capture can be undone.");
        vm.Redo(); Check(vm.Graph.Project.Specimens.Count == 1, "Capture can be redone.");

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeManager.Apply(theme); vm.RefreshAll();
            var suffix = theme == AppTheme.Light ? "light" : "dark";
            var album = new SpecimenAlbumWindow(vm) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            album.Show(); album.UpdateLayout();
            Check(((ItemsControl)album.FindName("Cards")).Items.Count == 1, "Album shows a saved specimen.");
            RenderFeatureWindow(album, $"specimens-{suffix}");
            ((TextBox)album.FindName("Search")).Text = "該当しない";
            Check(((ItemsControl)album.FindName("Cards")).Items.Count == 0, "Album search filters entries.");
            album.Close();
            var choice = new ChoiceWindow(vm, vm.Nodes.Single(n => n.Id == sourceId)) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            choice.Show(); choice.UpdateLayout();
            Check(((ListBox)choice.FindName("OptionsList")).SelectedValue is Guid selected && selected == edge.Id, "Choice dialog restores the chosen path.");
            RenderFeatureWindow(choice, $"choices-{suffix}"); choice.Close();
            var capture = new CaptureSpecimenWindow(vm, vm.Nodes.Single(n => n.Id == goal.Id)) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            capture.Show(); capture.UpdateLayout(); RenderFeatureWindow(capture, $"capture-specimen-{suffix}"); capture.Close();
            var view = new GraphView { DataContext = vm, Width = 1120, Height = 540 };
            view.Measure(new Size(1120, 540)); view.Arrange(new Rect(0, 0, 1120, 540)); view.UpdateLayout();
            Render(view, $"choice-graph-{suffix}");
        }
        ThemeManager.Apply(AppTheme.Light);
        var dialogHistory = History(vm, "_undo");
        var edit = new ChoiceWindow(vm, vm.Nodes.Single(n => n.Id == sourceId)) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        ExecuteFeatureDialog(edit, () =>
        {
            ((ListBox)edit.FindName("OptionsList")).SelectedValue = reasonEdge.Id;
            var text = Descendants(edit).OfType<TextBox>().Single(t => t.DataContext is ChoiceOption option && option.Id == edge.Id);
            text.Text = "時間を優先して今回は購入する";
            ((Button)edit.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Check(vm.Graph.Find(sourceId)!.SelectedChoiceEdgeId == reasonEdge.Id && History(vm, "_undo") == dialogHistory + 1,
            "The actual dialog saves its selected row in one undo.");
        Check(vm.Graph.Project.Edges.Single(e => e.Id == edge.Id).DecisionReason == "時間を優先して今回は購入する",
            "The actual reason textbox commits its binding before saving.");
        vm.Undo();
        var unchanged = State(vm);
        var cancelled = new ChoiceWindow(vm, vm.Nodes.Single(n => n.Id == sourceId)) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        ExecuteFeatureDialog(cancelled, () => { ((ListBox)cancelled.FindName("OptionsList")).SelectedValue = reasonEdge.Id; cancelled.Close(); });
        Check(State(vm) == unchanged, "Closing the choice dialog leaves the project untouched.");

        vm.RemoveSpecimen(vm.Graph.Project.Specimens[0].Id);
        var captureDialog = new CaptureSpecimenWindow(vm, vm.Nodes.Single(n => n.Id == goal.Id)) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        ExecuteFeatureDialog(captureDialog, () =>
        {
            ((ComboBox)captureDialog.FindName("Category")).SelectedIndex = 1;
            ((TextBox)captureDialog.FindName("Reflection")).Text = "組み立ての順番を学んだ";
            ((Button)captureDialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Check(vm.Graph.Project.Specimens.Single() is { Category: "学んだこと", Reflection: "組み立ての順番を学んだ" },
            "The capture dialog commits the selected category and reflection.");
    }

    private static void ExecuteFeatureDialog(Window window, Action action)
    {
        Exception? failure = null;
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; window.Close(); }
        }));
        window.ShowDialog();
        if (failure is not null) throw new InvalidOperationException("Feature dialog interaction failed.", failure);
    }

    private static void RenderFeatureWindow(Window window, string name)
    {
        window.UpdateLayout();
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Render((System.Windows.Media.Visual)window.Content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts"); System.IO.Directory.CreateDirectory(directory);
        using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
