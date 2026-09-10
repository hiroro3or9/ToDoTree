using System.Windows;
using System.Windows.Input;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel
{
    public ProcedureViewModel? Procedure { get; private set; }
    public bool IsProcedure => Procedure is not null;
    public bool IsNormalTodo => !IsProcedure;
    public bool IsExecutionView => Procedure is { IsDefinition: false };
    public event Action<TodoProject>? ProcedureCreated;
    public ICommand CreateProcedureCommand => new RelayCommand(CreateProcedure,
        () => !IsProcedure && !IsNaming && (HasSelectedBlock || SelectionCount > 0));
    public ICommand ShowDefinitionCommand => new RelayCommand(() => SetProcedureMode(true));
    public ICommand ShowExecutionCommand => new RelayCommand(() => SetProcedureMode(false));

    private void CreateProcedure()
    {
        if (IsProcedure) return;
        try
        {
            var ids = (SelectedBlock is { } block ? DescendantNodeIds(block.Id) : SelectedNodes.Select(n => n.Id)).ToHashSet();
            var name = SelectedBlock?.Title ?? SelectedNode?.DisplayTitle ?? "新しい作業手順";
            var fragment = BranchTemplate.Capture(_project, ids, name);
            var endpoints = new HashSet<Guid>(ids);
            // Capture remaps IDs, so use the source hierarchy for boundary reporting.
            var hierarchy = new BlockHierarchy(_project);
            foreach (var b in _project.Blocks.Where(b => b.NodeIds.Any(ids.Contains)))
            {
                endpoints.Add(b.Id);
                endpoints.UnionWith(hierarchy.AncestorsOf(b.Id).Select(a => a.Id));
            }
            string NameOf(Guid id) => _project.Nodes.FirstOrDefault(n => n.Id == id)?.Title ?? _project.Blocks.FirstOrDefault(b => b.Id == id)?.Title ?? id.ToString();
            var excluded = _project.Edges.Where(e => endpoints.Contains(e.FromId) != endpoints.Contains(e.ToId)).ToArray();
            var summary = $"「{name}」の {fragment.Nodes.Count} 項目を独立した手順として保存します。\n出発点・ゴールも全体の完了対象です。";
            if (excluded.Length > 0) summary += $"\n\n選択外との接続 {excluded.Length} 件を除外します:\n" + string.Join("\n", excluded.Select(e => $"{NameOf(e.FromId)} → {NameOf(e.ToId)}"));
            if (MessageBox.Show(summary, "作業手順として保存", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            ProcedureCreated?.Invoke(ProcedureService.Create(fragment));
        }
        catch (Exception ex) { StatusMessage = $"手順を作成できませんでした: {ex.Message}"; }
    }

    private void SetProcedureMode(bool definition)
    {
        if (Procedure is null) return;
        if (!Procedure.SavePendingDetails()) return;
        CommitPendingBlockEdit();
        // Publish edited definitions before operations can start from them.
        if (!definition && IsDirty && !Save()) return;
        Procedure.IsDefinition = definition;
        OnPropertyChanged(nameof(IsExecutionView));
        CommandManager.InvalidateRequerySuggested();
    }

    private TodoProject PrepareStorageProject(ProcedureData? candidate = null)
    {
        if (Procedure is null) return _project;
        var data = (candidate ?? Procedure.Data).Clone();
        var definition = ProcedureService.Normalize(GraphSnapshot.Capture(_project));
        if (ProcedureService.Serialize(definition) != ProcedureService.Serialize(data.Definition))
        {
            data.Definition = definition;
            data.Revision++;
        }
        return new TodoProject
        {
            Id = _project.Id, Name = _project.Name, Description = _project.Description,
            DocumentKind = DocumentKind.Procedure, Procedure = data,
        };
    }

    private void AcceptStorageProject(TodoProject stored)
    {
        if (stored.Procedure is not null) Procedure?.Accept(stored.Procedure);
    }

    internal bool CommitProcedure(ProcedureData candidate)
    {
        try
        {
            var document = PrepareStorageProject(candidate);
            // All saves run synchronously on the UI dispatcher, including the existing autosave timer.
            _store.Save(_filePath ?? RecoveryFilePath, document);
            AcceptStorageProject(document);
            IsDirty = _filePath is null;
            DocumentStateChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage = $"実施記録を保存しました（{DateTime.Now:HH:mm:ss}）。";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失敗・操作は未確定です。再試行してください: {ex.Message}";
            return false;
        }
    }

    internal MainViewModel CreateProcedurePreview(GraphSnapshot snapshot, string name) =>
        new(_store, _settings, snapshot.ToProject(name), null, _recoveryDirectory);
}
