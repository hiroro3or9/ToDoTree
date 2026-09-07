using System.IO;
using System.Windows;
using ToDoTree.App.Views;
using ToDoTree.Core.Models;

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

