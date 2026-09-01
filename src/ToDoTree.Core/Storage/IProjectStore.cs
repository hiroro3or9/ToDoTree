using ToDoTree.Core.Models;

namespace ToDoTree.Core.Storage;

/// <summary>
/// 保存層の抽象。JSON でも SQLite でも、この形さえ満たせば差し替えられる。
/// </summary>
public interface IProjectStore
{
    /// <summary>ファイルダイアログ用のフィルタ文字列。</summary>
    string FileFilter { get; }

    /// <summary>既定の拡張子（"." を含む）。</summary>
    string DefaultExtension { get; }

    TodoProject Load(string path);

    void Save(string path, TodoProject project);
}
