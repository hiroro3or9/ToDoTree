namespace ToDoTree.App.ViewModels;

/// <summary>「次にやること」の 1 行。</summary>
public sealed class NextActionViewModel(int rank, NodeViewModel node, string reason)
{
    public int Rank { get; } = rank;

    public NodeViewModel Node { get; } = node;

    /// <summary>なぜこれを勧めるのか。</summary>
    public string Reason { get; } = reason;

    public string RankLabel => Rank.ToString();

    public string Title => Node.Title;
}
