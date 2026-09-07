namespace ToDoTree.Core.Models;

/// <summary>プロジェクトにつき1つの、作業を再開する場所。</summary>
public sealed class WorkBookmark
{
    public Guid NodeId { get; set; }
    public string Note { get; set; } = string.Empty;

    public WorkBookmark Clone() => new() { NodeId = NodeId, Note = Note };
}
