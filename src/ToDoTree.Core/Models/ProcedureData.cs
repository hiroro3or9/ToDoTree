namespace ToDoTree.Core.Models;

public enum DocumentKind { Todo, Procedure }
public enum RunStatus { Running, Paused, Completed, Abandoned }
public enum RunEventKind { Started, StepChanged, Paused, Resumed, Completed, Abandoned, Corrected, Undo, Redo }

/// <summary>A graph without document metadata or recursive execution history.</summary>
public sealed class GraphSnapshot
{
    public List<TodoNode> Nodes { get; set; } = [];
    public List<TodoEdge> Edges { get; set; } = [];
    public List<TodoBlock> Blocks { get; set; } = [];
    public List<ProjectVariable> Variables { get; set; } = [];

    public static GraphSnapshot Capture(TodoProject project) => new()
    {
        Nodes = [.. project.Nodes.Select(n => n.Clone())],
        Edges = [.. project.Edges.Select(e => e.Clone())],
        Blocks = [.. project.Blocks.Select(b => b.Clone())],
        Variables = [.. project.Variables.Select(v => v.Clone())],
    };
    public TodoProject ToProject(string name) => new()
    {
        Name = name, Nodes = [.. Nodes.Select(n => n.Clone())], Edges = [.. Edges.Select(e => e.Clone())],
        Blocks = [.. Blocks.Select(b => b.Clone())], Variables = [.. Variables.Select(v => v.Clone())],
    };
    public GraphSnapshot Clone() => Capture(ToProject("手順"));
}

public sealed class ProcedureData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Revision { get; set; } = 1;
    public GraphSnapshot Definition { get; set; } = new();
    public List<ProcedureRun> Runs { get; set; } = [];
    public ProcedureData Clone() => new()
    { Id = Id, Revision = Revision, Definition = Definition.Clone(), Runs = [.. Runs.Select(r => r.Clone())] };
}

public sealed class RunState
{
    public GraphSnapshot Graph { get; set; } = new();
    public Dictionary<Guid, string> Notes { get; set; } = [];
    public WorkBookmark? Bookmark { get; set; }
    public RunState Clone() => new() { Graph = Graph.Clone(), Notes = new(Notes), Bookmark = Bookmark?.Clone() };
}

public sealed class RunEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Sequence { get; set; }
    public RunEventKind Kind { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? NodeId { get; set; }
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public RunEvent Clone() => (RunEvent)MemberwiseClone();
}

public sealed class RunCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Sequence { get; set; }
    public DateTimeOffset At { get; set; }
    public RunStatus Status { get; set; }
    public RunState State { get; set; } = new();
    public RunCheckpoint Clone() => new() { Id = Id, Sequence = Sequence, At = At, Status = Status, State = State.Clone() };
}

public sealed class ProcedureRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Revision { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AbandonedAt { get; set; }
    public string AbandonReason { get; set; } = "";
    public GraphSnapshot Definition { get; set; } = new();
    public RunState Current { get; set; } = new();
    public List<RunEvent> Events { get; set; } = [];
    public List<RunCheckpoint> Checkpoints { get; set; } = [];
    public ProcedureRun Clone() => new()
    {
        Id = Id, Name = Name, Revision = Revision, Status = Status, StartedAt = StartedAt, UpdatedAt = UpdatedAt,
        CompletedAt = CompletedAt, AbandonedAt = AbandonedAt, AbandonReason = AbandonReason,
        Definition = Definition.Clone(), Current = Current.Clone(),
        Events = [.. Events.Select(e => e.Clone())], Checkpoints = [.. Checkpoints.Select(c => c.Clone())],
    };
}
