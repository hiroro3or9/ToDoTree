using System.IO;
using System.Text.Json;
using System.Windows;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.App.Services;

public interface IItemClipboard
{
    bool ContainsItems();
    void Write(TodoProject fragment);
    TodoProject? Read();
}

public sealed class ItemClipboard : IItemClipboard
{
    public const string Format = "ToDoTree.Items.v1";

    public bool ContainsItems() => Clipboard.ContainsData(Format);

    public void Write(TodoProject fragment)
    {
        var data = new DataObject();
        data.SetData(Format, JsonSerializer.Serialize(fragment, JsonProjectStore.SerializerOptions));
        data.SetText(string.Join(Environment.NewLine, fragment.Nodes.Select(n => n.Title)));
        Clipboard.SetDataObject(data, true);
    }

    public TodoProject? Read()
    {
        if (Clipboard.GetData(Format) is not string json) return null;
        var fragment = JsonSerializer.Deserialize<TodoProject>(json, JsonProjectStore.SerializerOptions)
            ?? throw new InvalidDataException("コピーしたアイテムが空です。");
        BranchTemplate.Validate(fragment);
        if (ChoiceService.Validate(fragment, fragment.SchemaVersion) is { } choiceError)
            throw new InvalidDataException(choiceError);
        if (BranchMoveService.ValidateLinks(fragment, fragment.SchemaVersion) is { } linkError)
            throw new InvalidDataException(linkError);
        return fragment;
    }
}
