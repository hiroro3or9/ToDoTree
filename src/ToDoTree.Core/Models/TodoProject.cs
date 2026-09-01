namespace ToDoTree.Core.Models;

/// <summary>保存単位。ノードと辺の入れ物。</summary>
public sealed class TodoProject
{
    public const int CurrentSchemaVersion = 1;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "新しいプロジェクト";

    public string Description { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<TodoNode> Nodes { get; set; } = [];

    public List<TodoEdge> Edges { get; set; } = [];

    public TodoProject DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        SchemaVersion = SchemaVersion,
        Nodes = [.. Nodes.Select(n => n.Clone())],
        Edges = [.. Edges.Select(e => e.Clone())],
    };
}
