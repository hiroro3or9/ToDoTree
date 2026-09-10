namespace ToDoTree.Core.Models;

public sealed class ChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
    public ChecklistItem Clone() => (ChecklistItem)MemberwiseClone();
}
