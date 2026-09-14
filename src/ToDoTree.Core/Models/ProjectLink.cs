namespace ToDoTree.Core.Models;

/// <summary>独立した枝への入口。ファイル名だけでは別文書への誤接続を検知できないためIDも保持する。</summary>
public sealed class ProjectLink
{
    public Guid ProjectId { get; set; }
    public Guid NodeId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public ProjectLink Clone() => (ProjectLink)MemberwiseClone();
}
