namespace ToDoTree.Core.Models;

/// <summary>まだグラフに配置していない、プロジェクトごとの仮置き項目。</summary>
public sealed class InboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public InboxItem Clone() => (InboxItem)MemberwiseClone();
}
