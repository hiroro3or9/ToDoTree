using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using ToDoTree.App.Services;
using ToDoTree.Core.Graph;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IItemClipboard _itemClipboard = new ItemClipboard();
    private ICommand? _copyItemsCommand, _pasteItemsCommand, _pasteItemsHereCommand;
    public event Action? PasteItemsRequested;

    public ICommand CopyItemsCommand => _copyItemsCommand ??= new RelayCommand(CopyItems,
        () => !IsExecutionView && !IsNaming && !IsConnecting && (HasSelectedBlock || SelectionCount > 0));
    public ICommand PasteItemsCommand => _pasteItemsCommand ??= new RelayCommand(
        () => PasteItemsRequested?.Invoke(), CanPasteItems);
    public ICommand PasteItemsHereCommand => _pasteItemsHereCommand ??= new RelayCommand(
        () => PasteItems(_menuX, _menuY), CanPasteItems);

    private bool CanPasteItems()
    {
        if (IsExecutionView || IsNaming || IsConnecting) return false;
        try { return _itemClipboard.ContainsItems(); }
        catch (ExternalException) { return false; }
    }

    private void CopyItems()
    {
        if (!CopyItemsCommand.CanExecute(null)) return;
        try
        {
            foreach (var node in Nodes) node.CommitNotes();
            var ids = SelectedBlock is { } block ? DescendantNodeIds(block.Id) : SelectedNodes.Select(n => n.Id);
            var fragment = BranchTemplate.Capture(_project, ids, "コピーしたアイテム", resetWorkState: false);
            _itemClipboard.Write(fragment);
            StatusMessage = $"{fragment.Nodes.Count} 件のアイテムをコピーしました。Ctrl+V で貼り付けできます。";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidDataException or IOException or ArgumentException or JsonException)
        {
            StatusMessage = $"コピーできませんでした：{ex.Message}";
        }
    }

    public void PasteItems(double x, double y)
    {
        if (IsExecutionView || IsNaming || IsConnecting) return;
        try
        {
            if (_itemClipboard.Read() is not { } fragment) return;
            InsertFragment(fragment, x, y, resetWorkState: false);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidDataException or IOException or ArgumentException or JsonException)
        {
            StatusMessage = $"貼り付けできませんでした：{ex.Message}";
        }
    }
}
