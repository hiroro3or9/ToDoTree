namespace ToDoTree.Core.Models;

/// <summary>
/// プロジェクトの中だけで通じる名前と、その表示値。
/// ステップの原文にある <c>{Hoge}</c> を、表示するときだけこの値へ置き換える。
/// </summary>
public sealed class ProjectVariable
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public ProjectVariable Clone() => (ProjectVariable)MemberwiseClone();

    public override string ToString() => $"{{{Name}}} = {Value}";
}
