using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Models;

namespace ToDoTree.App;

public partial class MainWindow
{
    private Point? _inboxDragStart;
    private void OnInboxInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (sender is FrameworkElement { DataContext: MainViewModel vm } && vm.AddInboxCommand.CanExecute(null))
            vm.AddInboxCommand.Execute(null);
        e.Handled = true;
    }
    private void OnChecklistInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (sender is FrameworkElement { DataContext: NodeViewModel vm } && vm.AddChecklistCommand.CanExecute(null))
            vm.AddChecklistCommand.Execute(null);
        e.Handled = true;
    }
    private void OnInboxDragStart(object sender, MouseButtonEventArgs e) => _inboxDragStart = e.GetPosition(this);
    private void OnInboxDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _inboxDragStart = null; return; }
        if (_inboxDragStart is not { } start || sender is not TextBlock { DataContext: InboxItem item } source
            || _workspace?.ActiveDocument is not { } vm) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _inboxDragStart = null;
        DragDrop.DoDragDrop(source, new DataObject(MainViewModel.InboxDragFormat, new InboxDragData(vm.DocumentId, item.Id)), DragDropEffects.Move);
        e.Handled = true;
    }
}
