using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Storage;

/// <summary>
/// 1 プロジェクト = 1 JSON ファイル。
/// 一時ファイルに書いてから置き換えるので、保存の途中で落ちても元ファイルは壊れない。
/// </summary>
public sealed class JsonProjectStore : IProjectStore
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // 日本語をそのまま読める形で書き出す。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FileFilter => "ToDoTree プロジェクト (*.todotree.json)|*.todotree.json|JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*";

    public string DefaultExtension => ".todotree.json";

    public TodoProject Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"ファイルが見つかりません: {path}", path);
        }

        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<TodoProject>(json, SerializerOptions)
                      ?? throw new InvalidDataException("プロジェクトを読み込めませんでした（中身が空です）。");

        if (project.SchemaVersion > TodoProject.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"このファイルは新しい形式です (schemaVersion={project.SchemaVersion})。アプリを更新してください。");
        }

        project.Nodes ??= [];
        project.Edges ??= [];
        return project;
    }

    public void Save(string path, TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(project, SerializerOptions);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);

        if (File.Exists(path))
        {
            var backup = path + ".bak";
            File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}
