using System.IO;
using System.Windows.Input;
using ToDoTree.App.Services;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

/// <summary>タブを開かず、保存済みファイルと編集中のプロジェクトから今日の完了を集める。</summary>
public sealed class CompletedTasksViewModel(
    IProjectStore store, AppSettings settings, Func<IEnumerable<MainViewModel>> openDocuments) : ObservableObject
{
    private IReadOnlyList<CompletedTaskRow> _rows = [];
    private string _summary = string.Empty;
    private string _errors = string.Empty;
    private ICommand? _refreshCommand;

    public IReadOnlyList<CompletedTaskRow> Rows => _rows;
    public string Summary => _summary;
    public string Errors => _errors;
    public bool HasErrors => _errors.Length > 0;
    public bool IsEmpty => _rows.Count == 0;
    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(() => Refresh());

    public void Refresh(DateTimeOffset? now = null)
    {
        var date = DateOnly.FromDateTime((now ?? DateTimeOffset.Now).LocalDateTime);
        var rows = new List<CompletedTaskRow>();
        var errors = new List<string>();
        var openPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectCount = 0;

        void Collect(TodoProject project, string? path, bool isOpen)
        {
            if (project.Procedure is not null || project.DocumentKind == DocumentKind.Procedure) return;
            projectCount++;
            var resolver = ProjectVariableResolver.From(project);
            foreach (var node in CompletedTaskQuery.ForDate(project.Nodes, date, TimeZoneInfo.Local))
                rows.Add(new CompletedTaskRow(project.Id, node.Id, project.Name, resolver.Expand(node.Title),
                    resolver.Expand(node.Notes), node.CompletedAt!.Value, path, isOpen));
        }

        // 開いている文書は保存ファイルより優先する。完了の訂正やUndoを即座に反映する。
        foreach (var document in openDocuments().ToArray())
        {
            if (!string.IsNullOrEmpty(document.FilePath)) openPaths.Add(Path.GetFullPath(document.FilePath));
            if (document.IsNormalTodo) Collect(document.Graph.Project, document.FilePath, true);
        }

        var readPaths = new HashSet<string>(openPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var savedPath in settings.KnownProjectPaths.ToArray())
        {
            try
            {
                var path = Path.GetFullPath(savedPath);
                if (!readPaths.Add(path)) continue;
                Collect(store.Load(path), path, false);
            }
            catch (Exception ex)
            {
                // 1ファイルの移動・削除・破損で、他のプロジェクトの実績を隠さない。
                errors.Add($"{savedPath}: {ex.Message}");
            }
        }

        var sorted = rows.OrderByDescending(row => row.CompletedAt)
            .ThenBy(row => row.ProjectName, StringComparer.CurrentCulture)
            .ThenBy(row => row.Title, StringComparer.CurrentCulture).ThenBy(row => row.NodeId).ToArray();
        if (!_rows.SequenceEqual(sorted))
        {
            _rows = sorted;
            OnPropertyChanged(nameof(Rows), nameof(IsEmpty));
        }
        _summary = $"{date:yyyy/MM/dd} ・ {rows.Count} 件完了 ・ {projectCount} プロジェクトを確認";
        _errors = string.Join(Environment.NewLine, errors);
        OnPropertyChanged(nameof(Summary), nameof(Errors), nameof(HasErrors));
    }
}

public sealed record CompletedTaskRow(Guid ProjectId, Guid NodeId, string ProjectName, string Title,
    string Notes, DateTimeOffset CompletedAt, string? FilePath, bool IsOpen)
{
    public string CompletedTimeText => CompletedAt.LocalDateTime.ToString("HH:mm");
    public string SourceLabel => IsOpen ? "開いています" : "閉じています";
    public string FilePathDisplay => FilePath ?? "未保存のプロジェクト";
}
