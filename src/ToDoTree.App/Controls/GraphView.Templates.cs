using System.IO;
using System.Windows;
using ToDoTree.App.Views;
using ToDoTree.Core.Models;
using ToDoTree.App.ViewModels;

namespace ToDoTree.App.Controls;

public partial class GraphView
{
    private TemplateLibraryWindow? _templateLibrary;

    private void OpenTemplateLibrary(TodoProject? draft)
    {
        _templateLibrary?.Close();
        var library = new TemplateLibraryWindow(draft) { Owner = Window.GetWindow(this) };
        _templateLibrary = library;
        library.Closed += (_, _) => { if (ReferenceEquals(_templateLibrary, library)) _templateLibrary = null; };
        library.InsertRequested += template =>
        {
            var point = Viewport.TranslatePoint(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), Surface);
            PlaceTemplate(template, point);
        };
        library.Show();
    }

    private void OnTemplateDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(MainViewModel.InboxDragFormat) is InboxDragData inbox)
        {
            e.Effects = _viewModel is { IsNaming: false, IsConnecting: false } vm
                && vm.DocumentId == inbox.DocumentId && vm.InboxItems.Any(i => i.Id == inbox.ItemId)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = e.Data.GetDataPresent(TemplateLibraryWindow.DragFormat)
            && _viewModel is { IsNaming: false, IsConnecting: false }
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTemplateDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (_viewModel is not { IsNaming: false, IsConnecting: false }) return;
        if (e.Data.GetData(MainViewModel.InboxDragFormat) is InboxDragData inbox)
        {
            var point = e.GetPosition(Surface);
            if (inbox.DocumentId == _viewModel.DocumentId && _viewModel.PlaceInboxItem(inbox.ItemId, point.X, point.Y))
                e.Effects = DragDropEffects.Move;
            return;
        }
        if (e.Data.GetData(TemplateLibraryWindow.DragFormat) is TodoProject template)
        {
            if (PlaceTemplate(template, e.GetPosition(Surface))) e.Effects = DragDropEffects.Copy;
        }
    }

    private bool PlaceTemplate(TodoProject template, Point point)
    {
        if (_viewModel is not { IsNaming: false, IsConnecting: false }) return false;
        try
        {
            _viewModel.InsertTemplate(template, point.X, point.Y);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            _viewModel.StatusMessage = $"部品を配置できませんでした：{ex.Message}";
            return false;
        }
    }
}

