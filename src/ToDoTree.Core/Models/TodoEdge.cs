namespace ToDoTree.Core.Models;

/// <summary>依存関係。<see cref="FromId"/> が片付くと <see cref="ToId"/> に進める。</summary>
public sealed class TodoEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FromId { get; set; }

    public Guid ToId { get; set; }

    public string? Label { get; set; }

    public ConnectionSide FromSide { get; set; }

    public ConnectionSide ToSide { get; set; }

    public TodoEdge Clone() => (TodoEdge)MemberwiseClone();
}

/// <summary>接続元の辺。既存の線は位置関係から自動決定する。</summary>
public enum ConnectionSide { Auto, Right, Top, Bottom, Left }
