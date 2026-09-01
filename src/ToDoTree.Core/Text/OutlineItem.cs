namespace ToDoTree.Core.Text;

/// <summary>貼り付けられたアウトラインの 1 行。</summary>
public sealed record OutlineItem(
    int Depth,
    string Title,
    IReadOnlyList<string> Tags,
    DateTimeOffset? Due,
    int? EstimateMinutes);
