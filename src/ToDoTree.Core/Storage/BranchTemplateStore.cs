using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

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
                if (RepeatService.ValidateSchema(template, template.SchemaVersion) is { } repeatError)
                {
                    throw new InvalidDataException(repeatError);
                }
                if (TaskDetailsValidation.Validate(template, template.SchemaVersion) is { } detailsError)
                    throw new InvalidDataException(detailsError);

                if (template.SchemaVersion < 8 && template.Blocks.Any(b => b.ParentBlockId is not null))
                {
                    throw new InvalidDataException("旧形式を名乗る部品にブロックの親子関係が入っています。");
                }

                // 通常のプロジェクトと同じ検証・移行を通す。部品だけが別経路で
                // 素通りすると、逃がしていない原文が形式10として保存されてしまう。
                if (ProjectVariableService.ValidateSchema(template, template.SchemaVersion) is { } variableError)
                {
                    throw new InvalidDataException(variableError);
                }

                ProjectVariableService.MigrateLegacyText(template, template.SchemaVersion);

                if (template.SchemaVersion is >= 2 and < TodoProject.CurrentSchemaVersion) template.SchemaVersion = TodoProject.CurrentSchemaVersion;
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

