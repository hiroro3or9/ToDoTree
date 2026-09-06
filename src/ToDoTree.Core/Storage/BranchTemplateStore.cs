using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Storage;

/// <summary>部品ごとに独立したファイルを使い、別のウィンドウからの保存と競合させない。</summary>
public sealed class BranchTemplateStore(string directory)
{
    private readonly JsonProjectStore _store = new();

    public (IReadOnlyList<TodoProject> Templates, IReadOnlyList<string> Errors) LoadAll()
    {
        var templates = new List<TodoProject>();
        var errors = new List<string>();
        if (!Directory.Exists(directory)) return (templates, errors);
        foreach (var path in Directory.EnumerateFiles(directory, "*.template.json"))
        {
            try
            {
                var template = System.Text.Json.JsonSerializer.Deserialize<TodoProject>(
                    File.ReadAllText(path), JsonProjectStore.SerializerOptions)
                    ?? throw new InvalidDataException("部品の中身が空です。");
                BranchTemplate.Validate(template);
                templates.Add(template);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (templates.OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList(), errors);
    }

    public TodoProject Save(TodoProject draft, string name)
    {
        var copy = BranchTemplate.Capture(draft, draft.Nodes.Select(n => n.Id), name);
        _store.Save(Path.Combine(directory, $"{copy.Id:N}.template.json"), copy);
        return copy;
    }
}

