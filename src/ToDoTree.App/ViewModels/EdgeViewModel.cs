using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>2 枚のカードを結ぶ線。</summary>
public sealed class EdgeViewModel(TodoEdge model, NodeViewModel from, NodeViewModel to)
{
    public TodoEdge Model { get; } = model;

    public NodeViewModel From { get; } = from;

    public NodeViewModel To { get; } = to;

    /// <summary>選択中のノードに繋がっている線は強調する。</summary>
    public bool IsHighlighted { get; set; }

    /// <summary>最長経路の上の線。</summary>
    public bool IsOnCriticalPath { get; set; }

    /// <summary>クリックで選ばれている線（Delete で外せる）。</summary>
    public bool IsSelected { get; set; }

    /// <summary>先行が片付いている線は薄く描く（もう通過した道）。</summary>
    public bool IsSettled => From.Model.IsSettled;
}
