using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToDoTree.Core.Graph;
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

        // 移行で書き換える前に控える。回数は形式6からなので、
        // 「元のファイルが何形式を名乗っていたか」で判定する必要がある。
        var declaredVersion = project.SchemaVersion;

        if (project.SchemaVersion > TodoProject.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"このファイルは新しい形式です (schemaVersion={project.SchemaVersion})。アプリを更新してください。");
        }

        // 旧形式（ブロックが無い版）は、メモリ上で空のブロック一覧として扱う。
        // 次の保存で新しい形式として書き出され、そこから先は古い版に上書きされなくなる。
        if (project.SchemaVersion < TodoProject.CurrentSchemaVersion)
        {
            if (project.SchemaVersion < 2 && project.Blocks.Count > 0)
            {
                throw new InvalidDataException(
                    $"schemaVersion={project.SchemaVersion} のファイルにブロックが入っています。読み込みを中止しました。");
            }

            project.SchemaVersion = TodoProject.CurrentSchemaVersion;
        }

        // 新しい形式は、壊れた所属情報を黙って捨てずに読み込みごと止める。
        // 途中まで読めた状態で保存してしまうと、元ファイルのブロックが失われるため。
        if (BlockConnections.Validate(project) is { } reason)
        {
            throw new InvalidDataException($"ブロックの情報が壊れています。{reason}");
        }

        // 回数も同じ扱いにする。不正な回数を黙って丸めると、
        // 「完了なのに 2 / 3 回」のまま保存されて後続の待ちが壊れる。
        if (RepeatService.ValidateSchema(project, declaredVersion) is { } repeatError)
        {
            throw new InvalidDataException($"繰り返しの情報が壊れています。{repeatError}");
        }

        if (project.Bookmark is { } bookmark && !project.Nodes.Any(n => n.Id == bookmark.NodeId))
        {
            throw new InvalidDataException("作業のしおりが存在しないステップを指しています。");
        }

        return project;
    }

    public void Save(string path, TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (BlockConnections.Validate(project) is { } error) throw new InvalidDataException(error);
        if (RepeatService.Validate(project) is { } repeatError) throw new InvalidDataException(repeatError);
        project.SchemaVersion = TodoProject.CurrentSchemaVersion;
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
