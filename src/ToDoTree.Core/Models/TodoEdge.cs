namespace ToDoTree.Core.Models;

/// <summary>依存関係。<see cref="FromId"/> が片付くと <see cref="ToId"/> に進める。</summary>
public sealed class TodoEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromId { get; set; }

    public Guid ToId { get; set; }

    public string? Label { get; set; }

    public TodoEdge Clone() => (TodoEdge)MemberwiseClone();
}
