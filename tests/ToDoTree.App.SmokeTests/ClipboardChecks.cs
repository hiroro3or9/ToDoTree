using System.IO;
using System.Windows;
using System.Windows.Controls;
using ToDoTree.App.Controls;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.App.Views;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private sealed class MemoryItemClipboard : IItemClipboard
    {
        public TodoProject? Fragment;
        public bool FailRead;
        public bool ContainsItems() => Fragment is not null;
        public void Write(TodoProject fragment) => Fragment = fragment.DeepClone();
        public TodoProject? Read() => FailRead ? throw new InvalidDataException("壊れたデータ") : Fragment?.DeepClone();
    }

    private static void VerifyClipboard()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts", "clipboard", Guid.NewGuid().ToString("N"));
        var clipboard = new MemoryItemClipboard();
        MainViewModel Create(TodoProject project) => new(new JsonProjectStore(), new AppSettings(), project,
            null, folder, itemClipboard: clipboard);
        var parent = new TodoNode { Title = "{製品}を作る", Notes = "メモ", X = 80, Y = 90 };
        var child = new TodoNode { Title = "内部の作業", ParentTaskId = parent.Id };
        var source = Create(new TodoProject { Nodes = [parent, child],
            Variables = [new ProjectVariable { Name = "製品", Value = "箱" }] });
        Check(!source.CopyItemsCommand.CanExecute(null) && !source.PasteItemsCommand.CanExecute(null),
            "Copy and paste are disabled without a selection and clipboard items.");
        source.SelectOnly(source.Nodes.Single(n => n.Id == parent.Id));
        var beforeCopy = State(source);
        source.CopyItemsCommand.Execute(null);
        Check(State(source) == beforeCopy && History(source, "_undo") == 0 && !source.IsDirty,
            "Copying does not change the source or add undo history.");
        parent.Notes = "コピー後の変更";
        var destinationParent = new TodoNode { Title = "貼り付け先" };
        var target = Create(new TodoProject { Nodes = [destinationParent] });
        target.EnterTask(target.Nodes[0]);
        var beforePaste = State(target);
        target.PasteItems(100, 120);
        var firstPaste = target.SelectedNode!;
        Check(target.Nodes.Count == 3 && target.SelectionCount == 1 && firstPaste.Model.ParentTaskId == destinationParent.Id,
            "Paste enters the current task and selects only the pasted root.");
        Check(firstPaste.Model.Notes == "メモ" && firstPaste.DisplayTitle == "箱を作る",
            "Cross-document paste keeps the copied snapshot and required variables.");
        Check(target.Nodes.Single(n => n.Title == child.Title).Model.ParentTaskId == firstPaste.Id,
            "Nested tasks point to the copied parent.");
        Check(History(target, "_undo") == 1 && target.IsDirty, "Paste is one undoable edit.");
        var afterPaste = State(target);
        target.UndoCommand.Execute(null);
        Check(State(target) == beforePaste, "Undo removes the entire pasted hierarchy and imported variables.");
        target.RedoCommand.Execute(null);
        Check(State(target) == afterPaste, "Redo restores the pasted hierarchy and identities.");
        target.PasteItems(100, 120);
        Check(target.Nodes.Count == 5 && target.Nodes.Select(n => n.Id).Distinct().Count() == 5,
            "Repeated paste creates independent items.");
        Check(target.SelectedNode!.Y > firstPaste.Y, "Repeated paste avoids overlapping existing items.");
        clipboard.FailRead = true;
        var beforeError = State(target); var history = History(target, "_undo");
        target.PasteItems(0, 0);
        Check(State(target) == beforeError && History(target, "_undo") == history
            && target.StatusMessage.Contains("貼り付けできません"), "Invalid clipboard data leaves the project and history intact.");
        clipboard.FailRead = false;

        var view = new GraphView { DataContext = target };
        var host = new Window { Content = view, Width = 900, Height = 650, Opacity = 0,
            ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        foreach (var key in new[] { "NodeMenu", "BlockMenu", "CanvasMenu" })
        {
            var menu = (ContextMenu)view.FindResource(key);
            menu.DataContext = target;
            menu.PlacementTarget = view;
            menu.IsOpen = true;
            menu.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Check(menu.Items.OfType<MenuItem>().Any(m => ReferenceEquals(m.Command, target.PasteItemsHereCommand)),
                $"{key} provides a bound paste command.");
            menu.IsOpen = false;
        }
        var beforeShortcut = target.Nodes.Count;
        view.HandleItemClipboardShortcut(paste: true);
        Check(target.Nodes.Count == beforeShortcut + 2, "Canvas paste dispatches through the viewport event.");
        host.Close();

        var store = new BranchTemplateStore(Path.Combine(folder, "templates"));
        var template = store.Save(clipboard.Fragment!, "検証用の部品");
        var library = new TemplateLibraryWindow(null, store) { ShowActivated = false, Opacity = 0 };
        Check(((Button)library.FindName("DeleteButton")).IsEnabled, "The library enables delete for the selected template.");
        library.Show();
        RenderFeatureWindow(library, "template-library-delete-light");
        ThemeManager.Apply(AppTheme.Dark);
        RenderFeatureWindow(library, "template-library-delete-dark");
        ThemeManager.Apply(AppTheme.Light);
        library.Close();
        store.Delete(template.Id);
        var emptyLibrary = new TemplateLibraryWindow(null, store);
        Check(!((Button)emptyLibrary.FindName("DeleteButton")).IsEnabled
            && !((Button)emptyLibrary.FindName("InsertButton")).IsEnabled,
            "Reopening the library after deletion shows no selectable template.");
        emptyLibrary.Close();
    }
}
