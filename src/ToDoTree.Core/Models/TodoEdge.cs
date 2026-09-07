using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Models;

/// <summary>依存関係。通過点は線の表示位置だけを指定し、タスク数や依存関係を変えない。</summary>
public sealed class TodoEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public string? Label { get; set; }

    /// <summary>
    /// 線の色。null は既定色。<see cref="ColorPresets"/> の id を入れる。
    /// 選択・最長経路・強調のときは状態色が優先されるので、普段の見え方だけを変える。
    /// </summary>
    public string? ColorId { get; set; }
    public ConnectionSide FromSide { get; set; }
    public ConnectionSide ToSide { get; set; }

    private List<JunctionPoint> _waypoints = [];
    public List<JunctionPoint> Waypoints { get => _waypoints; set => _waypoints = value ?? []; }

    public TodoEdge Clone()
    {
        var copy = (TodoEdge)MemberwiseClone();
        copy.Waypoints = [.. Waypoints];
        return copy;
    }
}

public readonly record struct JunctionPoint(double X, double Y, bool IsSmooth = false)
{
    public Vec2 ToVector() => new(X, Y);
}

/// <summary>接続元の辺。既存の線は位置関係から自動決定する。</summary>
public enum ConnectionSide { Auto, Right, Top, Bottom, Left }
