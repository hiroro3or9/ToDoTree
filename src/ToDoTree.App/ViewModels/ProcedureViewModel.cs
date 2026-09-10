using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

public sealed record RunChoice(Guid Id, string Label) { public override string ToString() => Label; }
public sealed record CheckpointChoice(Guid? Id, string Label) { public override string ToString() => Label; }
public sealed record ProcedureCheck(Guid Id, string Text, bool IsChecked);

public sealed class ProcedureStep
{
    public required TodoNode Node { get; init; }
    public required string Title { get; init; }
    public required string Instructions { get; init; }
    public required string Dependency { get; init; }
    public string Status => Labels.Of(Node.Status);
    public string Summary => $"{Status}" + (Node.Repeat is { } r ? $" · {r.CompletedCount}/{r.TargetCount} 回" : "");
    public string Completed => Node.CompletedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") ?? "—";
}

public sealed class ProcedureViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly List<RunState> _undo = [], _redo = [];
    private bool _isDefinition;
    private RunChoice? _selectedRun;
    private CheckpointChoice? _selectedCheckpoint;
    private ProcedureStep? _selectedStep;
    private bool _refreshing;
    private bool _loadingDetails;
    private string _feedback = "";
    public ProcedureData Data { get; private set; }
    public MainViewModel? Preview { get; private set; }
    public ObservableCollection<RunChoice> Runs { get; } = [];
    public ObservableCollection<CheckpointChoice> Checkpoints { get; } = [];
    public ObservableCollection<ProcedureStep> Steps { get; } = [];
    public ObservableCollection<ProcedureCheck> Checks { get; } = [];
    public event Action? StartRequested;

    public ProcedureViewModel(MainViewModel owner, ProcedureData data)
    {
        _owner = owner; Data = data;
        StartCommand = new RelayCommand(() => StartRequested?.Invoke(), () => CanStart);
        PauseCommand = new RelayCommand(() =>
        {
            if (SavePendingDetails() && CanEdit)
                Transition(() => ProcedureService.Pause(Data, CurrentRun!.Id, SelectedStep!.Node.Id, PauseNote, DateTimeOffset.Now));
        }, () => CanEdit && SelectedStep is not null);
        ResumeCommand = new RelayCommand(Resume, () => IsCurrent && CurrentRun?.Status == RunStatus.Paused);
        AbandonCommand = new RelayCommand(() =>
        {
            if (SavePendingDetails() && MessageBox.Show("今回の実績を残して打ち切ります。次は未実施から始められます。", "今回を打ち切る",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                Transition(() => ProcedureService.Abandon(Data, CurrentRun!.Id, PauseNote, DateTimeOffset.Now));
        }, () => IsCurrent && CurrentRun?.Status is RunStatus.Running or RunStatus.Paused);
        CorrectCommand = new RelayCommand(() => Transition(() => ProcedureService.Correct(Data, CurrentRun!.Id,
            SelectedStep!.Node.Id, DateTimeOffset.Now)), () => IsCurrent && CurrentRun?.Status == RunStatus.Completed &&
            Data.Runs.LastOrDefault()?.Id == CurrentRun?.Id && SelectedStep is not null);
        AdvanceCommand = new RelayCommand(() => Edit((node, _, now) =>
        {
            if (node.Repeat is not null) Reject(RepeatService.Advance(node, now));
            else ProcedureService.SetStatus(node, NodeStatus.Done, now);
        }), () => CanEdit && SelectedStep is { Node.Status: not (NodeStatus.Done or NodeStatus.Cancelled) });
        StartStepCommand = new RelayCommand(() => Edit((n, _, now) => ProcedureService.SetStatus(n, NodeStatus.InProgress, now)), () => CanEdit && SelectedStep is not null);
        ResetStepCommand = new RelayCommand(() => Edit((n, _, now) => ProcedureService.SetStatus(n, NodeStatus.NotStarted, now)), () => CanEdit && SelectedStep is not null);
        CancelStepCommand = new RelayCommand(() => Edit((n, _, now) => ProcedureService.SetStatus(n, NodeStatus.Cancelled, now)), () => CanEdit && SelectedStep is not null);
        ApplyDetailsCommand = new RelayCommand(() => ApplyDetails(), () => CanEdit && SelectedStep is not null);
        ToggleCheckCommand = new RelayCommand(p =>
        {
            if (p is ProcedureCheck check) Edit((n, _, _) => { var item = n.Checklist.Single(c => c.Id == check.Id); item.IsChecked = !item.IsChecked; });
        }, _ => CanEdit);
        UndoCommand = new RelayCommand(() => Restore(false), () => CanEdit && _undo.Count > 0);
        RedoCommand = new RelayCommand(() => Restore(true), () => CanEdit && _redo.Count > 0);
        LatestCommand = new RelayCommand(() => { SelectedRun = Runs.FirstOrDefault(); SelectedCheckpoint = Checkpoints.FirstOrDefault(); });
    }

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand AbandonCommand { get; }
    public ICommand CorrectCommand { get; }
    public ICommand AdvanceCommand { get; }
    public ICommand StartStepCommand { get; }
    public ICommand ResetStepCommand { get; }
    public ICommand CancelStepCommand { get; }
    public ICommand ApplyDetailsCommand { get; }
    public ICommand ToggleCheckCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand LatestCommand { get; }

    public bool IsDefinition { get => _isDefinition; set => SetProperty(ref _isDefinition, value); }
    public bool CanStart => !Data.Runs.Any(r => r.Status is RunStatus.Running or RunStatus.Paused);
    public ProcedureRun? CurrentRun => Data.Runs.FirstOrDefault(r => r.Id == SelectedRun?.Id);
    public bool IsCurrent => SelectedCheckpoint?.Id is null;
    public bool CanEdit => IsCurrent && CurrentRun?.Status == RunStatus.Running;
    public bool IsReadOnly => !CanEdit;
    public bool HasRun => CurrentRun is not null;
    public bool IsPauseNoteReadOnly => !IsCurrent || CurrentRun?.Status is not (RunStatus.Running or RunStatus.Paused);
    public string AdvanceLabel => HasRepeat ? "1回達成" : "完了";
    public string Feedback { get => _feedback; private set => SetProperty(ref _feedback, value); }
    public string RevisionLabel => $"手順 第{Data.Revision}版";
    public string StartLabel => Data.Runs.Count == 0 ? "実施を開始…" : "もう一度実施…";
    private string ProgressLabel => $"{Steps.Count(s => s.Node.Status == NodeStatus.Done)} / {Steps.Count} ステップ完了" +
        (Steps.Sum(s => s.Node.Checklist.Count) is var total && total > 0
            ? $" · チェック {Steps.Sum(s => s.Node.Checklist.Count(c => c.IsChecked))} / {total}" : "");
    public string Summary => !IsCurrent && CurrentRun is { } historical
        ? $"{historical.Name} · {SelectedCheckpoint?.Label} · 第{historical.Revision}版 · {ProgressLabel}"
        : CurrentRun is { } run
        ? $"{run.Name} · {StatusLabel(run.Status)} · 開始 {run.StartedAt.ToLocalTime():yyyy/MM/dd HH:mm}" +
          (run.CompletedAt is { } done ? $" · 完了 {done.ToLocalTime():yyyy/MM/dd HH:mm}" : "") +
          (run.AbandonedAt is { } stop ? $" · 打切 {stop.ToLocalTime():yyyy/MM/dd HH:mm} {run.AbandonReason}" : "") +
          $" · 第{run.Revision}版 · {ProgressLabel}"
        : "手順から今回の実施を開始してください。";
    public string BookmarkLabel => DisplayState?.Bookmark is { } bookmark
        ? $"再開メモ: {bookmark.Note}" : "";
    public string ReadOnlyLabel => IsCurrent ? (CanEdit ? "実施中 — 手順説明と構造は固定されています" : "この記録は読み取り専用です")
        : $"{SelectedCheckpoint?.Label} の保存状態（読み取り専用）";
    public string EventLog => CurrentRun is { } run ? string.Join("\n", run.Events
        .Where(e => SelectedCheckpoint?.Id is not { } id || e.Sequence <= run.Checkpoints.Single(c => c.Id == id).Sequence).Reverse()
        .Select(e => $"{e.Sequence}. {e.At.ToLocalTime():yyyy/MM/dd HH:mm:ss}  {EventLabel(e.Kind)}" +
            (e.NodeId is { } id ? " · " + Steps.FirstOrDefault(s => s.Node.Id == id)?.Title : ""))) : "";
    public RunState? DisplayState => CurrentRun is not { } run ? null : SelectedCheckpoint?.Id is { } id
        ? run.Checkpoints.Single(c => c.Id == id).State : run.Current;

    public RunChoice? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (!_refreshing && _selectedRun?.Id != value?.Id && !SavePendingDetails()) { OnPropertyChanged(); return; }
            if (!SetProperty(ref _selectedRun, value) || _refreshing) return;
            _undo.Clear(); _redo.Clear();
            RefreshCheckpoints(null); RefreshSteps();
        }
    }
    public CheckpointChoice? SelectedCheckpoint
    {
        get => _selectedCheckpoint;
        set
        {
            if (!_refreshing && _selectedCheckpoint?.Id != value?.Id && !SavePendingDetails()) { OnPropertyChanged(); return; }
            if (SetProperty(ref _selectedCheckpoint, value) && !_refreshing) RefreshSteps();
        }
    }
    public ProcedureStep? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (!_loadingDetails && !_refreshing && _selectedStep?.Node.Id != value?.Node.Id && !SavePendingDetails()) { OnPropertyChanged(); return; }
            if (!SetProperty(ref _selectedStep, value)) return;
            Checks.Clear();
            _loadingDetails = true;
            if (value is { } step)
            {
                foreach (var check in step.Node.Checklist) Checks.Add(new(check.Id, check.Title, check.IsChecked));
                DraftNote = DisplayState?.Notes.GetValueOrDefault(step.Node.Id) ?? "";
                DraftReason = step.Node.BlockReason; DraftBlocked = step.Node.IsManuallyBlocked;
                DraftDue = step.Node.Due?.LocalDateTime;
                DraftCount = step.Node.Repeat?.CompletedCount.ToString(CultureInfo.InvariantCulture) ?? "";
                if (Preview is not null)
                {
                    Preview.SelectedNode = Preview.Nodes.FirstOrDefault(n => n.Id == step.Node.Id);
                    if (Preview.SelectedNode is { } node) Preview.FocusNodeCommand.Execute(node);
                }
            }
            _loadingDetails = false;
            OnPropertyChanged(nameof(DraftNote), nameof(DraftReason), nameof(DraftBlocked), nameof(DraftDue), nameof(DraftCount), nameof(HasRepeat), nameof(AdvanceLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _draftNote = "", _draftReason = "", _draftCount = "";
    private bool _draftBlocked;
    private DateTime? _draftDue;
    public string DraftNote { get => _draftNote; set { if (SetProperty(ref _draftNote, value)) DraftChanged(); } }
    public string DraftReason { get => _draftReason; set { if (SetProperty(ref _draftReason, value)) DraftChanged(); } }
    public bool DraftBlocked { get => _draftBlocked; set { if (SetProperty(ref _draftBlocked, value)) DraftChanged(); } }
    public DateTime? DraftDue { get => _draftDue; set { if (SetProperty(ref _draftDue, value)) DraftChanged(); } }
    public string DraftCount { get => _draftCount; set { if (SetProperty(ref _draftCount, value)) DraftChanged(); } }
    private void DraftChanged()
    {
        if (_loadingDetails || !CanEdit) return;
        _owner.MarkDirty();
        Feedback = "入力内容は未保存です";
    }
    public bool HasPendingDetails => CanEdit && SelectedStep is { } step &&
        (DraftNote != (DisplayState?.Notes.GetValueOrDefault(step.Node.Id) ?? "") || DraftReason != step.Node.BlockReason ||
         DraftBlocked != step.Node.IsManuallyBlocked || DraftDue != step.Node.Due?.LocalDateTime ||
         (step.Node.Repeat is { } repeat && (!int.TryParse(DraftCount, out var count) || count != repeat.CompletedCount)));
    public bool SavePendingDetails() => !HasPendingDetails || ApplyDetails();
    public string PauseNote { get; set; } = "";
    public bool HasRepeat => SelectedStep?.Node.Repeat is not null;

    public void Initialize() => Accept(Data);

    public void Accept(ProcedureData data)
    {
        var selectedId = SelectedRun?.Id;
        var checkpoint = SelectedCheckpoint?.Id;
        var pauseNote = PauseNote;
        Data = data;
        _refreshing = true;
        Runs.Clear();
        foreach (var run in data.Runs.AsEnumerable().Reverse())
            Runs.Add(new(run.Id, $"{run.StartedAt.ToLocalTime():yyyy/MM/dd HH:mm} · {run.Name} · {StatusLabel(run.Status)}"));
        _selectedRun = Runs.FirstOrDefault(r => r.Id == selectedId) ?? Runs.FirstOrDefault();
        _refreshing = false;
        OnPropertyChanged(nameof(SelectedRun));
        RefreshCheckpoints(checkpoint); RefreshSteps();
        if (selectedId is not null && CurrentRun?.Id == selectedId)
        { PauseNote = pauseNote; OnPropertyChanged(nameof(PauseNote)); }
    }

    private void RefreshCheckpoints(Guid? id)
    {
        _refreshing = true;
        Checkpoints.Clear(); Checkpoints.Add(new(null, "最新の保存状態"));
        if (CurrentRun is { } run)
            foreach (var c in run.Checkpoints.AsEnumerable().Reverse())
                Checkpoints.Add(new(c.Id, $"{c.At.ToLocalTime():yyyy/MM/dd HH:mm:ss} · {(c.Status == RunStatus.Paused ? "中断" : "訂正前の完了")}"));
        _selectedCheckpoint = Checkpoints.FirstOrDefault(c => c.Id == id) ?? Checkpoints[0];
        _refreshing = false;
        OnPropertyChanged(nameof(SelectedCheckpoint));
    }

    private void RefreshSteps()
    {
        _loadingDetails = true;
        var id = SelectedStep?.Node.Id;
        Steps.Clear();
        if (DisplayState is { } state)
        {
            var project = state.Graph.ToProject(CurrentRun!.Name);
            var graph = new TodoGraph(project);
            var resolver = ProjectVariableResolver.From(project);
            Preview = _owner.CreateProcedurePreview(state.Graph, CurrentRun!.Name);
            // Kahn order follows dependencies; ties preserve the original graph order.
            foreach (var node in graph.TopologicalOrder() ?? [])
            {
                var waiting = graph.ParentsOf(node.Id).Where(n => !n.IsSettled).Select(n => resolver.Scan(n.Title).Display).ToArray();
                Steps.Add(new() { Node = node, Title = resolver.Scan(node.Title).Display, Instructions = resolver.Scan(node.Notes).Display,
                    Dependency = waiting.Length > 0 ? "先行に未完了あり: " + string.Join("、", waiting) : "先行の待ちはありません" });
            }
            PauseNote = state.Bookmark?.Note ?? "";
        }
        else Preview = null;
        _loadingDetails = true;
        _selectedStep = null;
        SelectedStep = Steps.FirstOrDefault(s => s.Node.Id == id)
            ?? Steps.FirstOrDefault(s => s.Node.Id == DisplayState?.Bookmark?.NodeId)
            ?? Steps.FirstOrDefault(s => s.Node.Status != NodeStatus.Done) ?? Steps.FirstOrDefault();
        _loadingDetails = false;
        if (Steps.Count == 0) Checks.Clear();
        OnPropertyChanged(nameof(Preview), nameof(SelectedStep), nameof(Summary), nameof(CanEdit), nameof(IsReadOnly), nameof(HasRun), nameof(IsPauseNoteReadOnly), nameof(CanStart), nameof(StartLabel),
            nameof(ReadOnlyLabel), nameof(BookmarkLabel), nameof(EventLog), nameof(RevisionLabel), nameof(PauseNote));
        CommandManager.InvalidateRequerySuggested();
    }

    public bool StartRun(string name, IEnumerable<ProjectVariable> variables, DateTimeOffset? due)
    {
        var succeeded = Transition(() => ProcedureService.Start(Data, name, variables, due, DateTimeOffset.Now));
        if (succeeded) { SelectedRun = Runs.First(); SelectedCheckpoint = Checkpoints.First(); }
        return succeeded;
    }

    private void Resume()
    {
        var nodeId = CurrentRun?.Current.Bookmark?.NodeId;
        if (Transition(() => ProcedureService.Resume(Data, CurrentRun!.Id, DateTimeOffset.Now)))
            SelectedStep = Steps.FirstOrDefault(s => s.Node.Id == nodeId) ?? SelectedStep;
    }

    private bool Transition(Func<ProcedureData> action)
    {
        try
        {
            if (!_owner.CommitProcedure(action())) { Feedback = _owner.StatusMessage; return false; }
            _undo.Clear(); _redo.Clear(); Feedback = "保存済み"; return true;
        }
        catch (Exception ex) { Feedback = ex.Message; return false; }
    }

    private bool Edit(Action<TodoNode, RunState, DateTimeOffset> change, bool includeDraft = true)
    {
        if (!CanEdit || SelectedStep is null) return false;
        var before = CurrentRun!.Current.Clone();
        try
        {
            var now = DateTimeOffset.Now;
            var candidate = ProcedureService.ChangeStep(Data, CurrentRun.Id, SelectedStep.Node.Id, (n, s) =>
            {
                if (includeDraft && HasPendingDetails) WriteDetails(n, s, now);
                change(n, s, now);
            }, now);
            if (ProcedureService.Serialize(candidate) == ProcedureService.Serialize(Data)) return true;
            if (!_owner.CommitProcedure(candidate)) { Feedback = _owner.StatusMessage; return false; }
            _redo.Clear();
            if (CanEdit) { _undo.Add(before); if (_undo.Count > 100) _undo.RemoveAt(0); }
            else _undo.Clear();
            Feedback = CurrentRun?.Status == RunStatus.Completed ? "すべてのステップが完了しました。実施記録を保存しました。" : "保存済み";
            CommandManager.InvalidateRequerySuggested();
            return true;
        }
        catch (Exception ex) { Feedback = ex.Message; return false; }
    }

    private bool ApplyDetails()
    {
        return Edit(WriteDetails, includeDraft: false);
    }

    private void WriteDetails(TodoNode node, RunState state, DateTimeOffset now)
    {
            if (node.Repeat is { } repeat)
            {
                if (!int.TryParse(DraftCount, out var number)) throw new InvalidOperationException("達成回数を整数で入力してください。");
                Reject(RepeatService.Configure(node, repeat.TargetCount, number, now));
            }
            if (DraftBlocked && node.IsSettled) throw new InvalidOperationException("完了・取り消しの項目には手動ブロックを設定できません。");
            node.IsManuallyBlocked = DraftBlocked; node.BlockReason = DraftReason;
            node.Due = DraftDue is { } date ? new DateTimeOffset(date) : null;
            state.Notes[node.Id] = DraftNote;
    }

    private void Restore(bool redo)
    {
        if (!CanEdit) return;
        if (!SavePendingDetails()) return;
        var source = redo ? _redo : _undo; var target = redo ? _undo : _redo;
        if (source.Count == 0) return;
        var before = CurrentRun!.Current.Clone();
        try
        {
            var candidate = ProcedureService.RestoreEdit(Data, CurrentRun.Id, source[^1], redo, DateTimeOffset.Now);
            if (!_owner.CommitProcedure(candidate)) { Feedback = _owner.StatusMessage; return; }
            source.RemoveAt(source.Count - 1); target.Add(before);
            Feedback = redo ? "やり直して保存しました。" : "操作を取り消して保存しました。";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) { Feedback = ex.Message; }
    }
    private static void Reject(RepeatResult result) { if (result.Error is { } error) throw new InvalidOperationException(error); }
    private static string StatusLabel(RunStatus status) => status switch
    { RunStatus.Running => "実施中", RunStatus.Paused => "中断中", RunStatus.Completed => "完了", _ => "打切" };
    private static string EventLabel(RunEventKind kind) => kind switch
    {
        RunEventKind.Started => "実施を開始", RunEventKind.StepChanged => "ステップを記録", RunEventKind.Paused => "中断",
        RunEventKind.Resumed => "再開", RunEventKind.Completed => "全体が完了", RunEventKind.Abandoned => "打切",
        RunEventKind.Corrected => "完了を訂正", RunEventKind.Undo => "操作を取り消し", _ => "やり直し",
    };
}
