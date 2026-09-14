namespace ToDoTree.Core.Models;

/// <summary>元のグラフから独立した、達成時点の記録。</summary>
public sealed class AchievementSpecimen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "作ったもの";
    public string Reflection { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public List<TodoNode> Nodes { get; set; } = [];
    public List<TodoEdge> Edges { get; set; } = [];
    public List<Guid> SkippedNodeIds { get; set; } = [];

    public AchievementSpecimen Clone() => new()
    {
        Id = Id, GoalId = GoalId, Title = Title, Category = Category, Reflection = Reflection,
        CompletedAt = CompletedAt, CapturedAt = CapturedAt,
        Nodes = [.. Nodes.Select(n => n.Clone())], Edges = [.. Edges.Select(e => e.Clone())],
        SkippedNodeIds = [.. SkippedNodeIds],
    };
}
