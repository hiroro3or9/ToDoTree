using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ToDoTree.App.Services;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.App.ViewModels;

/// <summary>開いているプロジェクトタブと、現在選択中のタブを管理する。</summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ToDoTree");

    private static readonly string RecoveryDirectory = Path.Combine(AppDataDirectory, "autosave");

    private static readonly string LegacyAutoSavePath = Path.Combine(AppDataDirectory, "autosave.todotree.json");

    private readonly IProjectStore _store;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _autoSaveTimer;
    private MainViewModel? _activeDocument;
    private bool _restoringSession;

    public WorkspaceViewModel()
        : this(new JsonProjectStore(), AppSettings.Load())
    {
    }

    internal WorkspaceViewModel(IProjectStore store, AppSettings settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new RelayCommand(OpenProject);
        CloseProjectCommand = new RelayCommand(
            parameter => CloseProject(parameter as MainViewModel),
            parameter => parameter is MainViewModel);

        _restoringSession = true;
        RestoreSession();
        _restoringSession = false;
        PersistSession();

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoSaveTimer.Tick += (_, _) => AutoSaveAll();
        _autoSaveTimer.Start();
    }

    public ObservableCollection<MainViewModel> Documents { get; } = [];

    public MainViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (!SetProperty(ref _activeDocument, value))
            {
                return;
            }

            OnPropertyChanged(nameof(WindowTitle));
            if (!_restoringSession)
            {
                PersistSession();
            }
        }
    }

    public string WindowTitle => ActiveDocument?.WindowTitle ?? "ToDoTree";

    public ICommand NewProjectCommand { get; }

    public ICommand OpenProjectCommand { get; }

    public ICommand CloseProjectCommand { get; }

    private void RestoreSession()
    {
        var restoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in _settings.OpenProjects)
        {
            try
            {
                var documentId = state.DocumentId == Guid.Empty ? Guid.NewGuid() : state.DocumentId;
                if (!string.IsNullOrWhiteSpace(state.FilePath))
                {
                    var path = Path.GetFullPath(state.FilePath);
                    if (!File.Exists(path) || !restoredPaths.Add(path))
                    {
                        continue;
                    }

                    AddDocument(_store.Load(path), path, documentId, isDirty: false, state);
                    continue;
                }

                var recoveryPath = RecoveryPath(documentId);
                if (File.Exists(recoveryPath))
                {
                    AddDocument(_store.Load(recoveryPath), null, documentId, isDirty: true, state);
                }
            }
            catch
            {
                // 1 ファイルを開けなくても、残りのタブは復元する。
            }
        }

        // 旧版の「最後に開いた 1 ファイル」もそのまま引き継ぐ。
        if (Documents.Count == 0
            && !_settings.HasWorkspaceSession
            && !string.IsNullOrWhiteSpace(_settings.LastFilePath)
            && File.Exists(_settings.LastFilePath))
        {
            try
            {
                var path = Path.GetFullPath(_settings.LastFilePath);
                AddDocument(_store.Load(path), path);
            }
            catch
            {
                // 後続の自動保存またはサンプルへフォールバックする。
            }
        }

        if (Documents.Count == 0 && File.Exists(LegacyAutoSavePath))
        {
            TryRestoreLegacyAutoSave();
        }

        if (Documents.Count == 0)
        {
            var sample = AddDocument(SampleProject.Create(), null);
            sample.StatusMessage = "サンプルを表示しています。新規ボタンで自分のプロジェクトを始められます。";
        }

        ActiveDocument = Documents.FirstOrDefault(d => d.DocumentId == _settings.ActiveDocumentId)
                         ?? Documents[0];

        if (Documents.Count > 1 && ActiveDocument is not null)
        {
            ActiveDocument.StatusMessage = $"前回開いていた {Documents.Count} 件のプロジェクトを復元しました。";
        }
    }

    private void TryRestoreLegacyAutoSave()
    {
        try
        {
            var project = _store.Load(LegacyAutoSavePath);
            var document = AddDocument(project, null, isDirty: true);

            // 新しいタブ別の退避先へ移してから旧ファイルを消す。
            _store.Save(document.RecoveryFilePath, project);
            File.Delete(LegacyAutoSavePath);
            document.StatusMessage = "前回の未保存プロジェクトを復元しました。";
        }
        catch
        {
            // 復元できなければサンプルへフォールバックする。
        }
    }

    private MainViewModel AddDocument(
        TodoProject project,
        string? filePath,
        Guid? documentId = null,
        bool isDirty = false,
        ProjectSessionState? state = null)
    {
        var document = new MainViewModel(
            _store,
            _settings,
            project,
            filePath,
            RecoveryDirectory,
            documentId,
            isDirty);

        if (state is not null)
        {
            document.ViewZoom = state.Zoom;
            document.ViewPanX = state.PanX;
            document.ViewPanY = state.PanY;
            document.HasViewportState = state.HasViewportState;
        }

        document.CanSaveToPath = candidate => Documents.All(other =>
            ReferenceEquals(other, document)
            || string.IsNullOrEmpty(other.FilePath)
            || !string.Equals(
                Path.GetFullPath(other.FilePath),
                Path.GetFullPath(candidate),
                StringComparison.OrdinalIgnoreCase));
        document.DocumentStateChanged += OnDocumentStateChanged;
        Documents.Add(document);
        return document;
    }

    private void NewProject()
    {
        var project = new TodoProject { Name = "新しいプロジェクト" };
        var graph = new TodoGraph(project);
        var start = graph.AddNode(new TodoNode { Title = "スタート", Kind = NodeKind.Start, X = 80, Y = 200 });
        var goal = graph.AddNode(new TodoNode { Title = "ゴール", Kind = NodeKind.Goal, X = 560, Y = 200 });
        graph.Connect(start.Id, goal.Id);

        var document = AddDocument(project, null);
        document.SelectOnly(document.Nodes.FirstOrDefault(node => node.Id == start.Id));
        document.StatusMessage = "新しいプロジェクトを作りました。スタートを選んで Enter でステップを足していきましょう。";
        ActiveDocument = document;
        PersistSession();
    }

    private void OpenProject()
    {
        var dialog = new OpenFileDialog { Filter = _store.FileFilter, Title = "プロジェクトを開く" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = Path.GetFullPath(dialog.FileName);
        var alreadyOpen = Documents.FirstOrDefault(document =>
            !string.IsNullOrEmpty(document.FilePath)
            && string.Equals(Path.GetFullPath(document.FilePath), path, StringComparison.OrdinalIgnoreCase));

        if (alreadyOpen is not null)
        {
            ActiveDocument = alreadyOpen;
            alreadyOpen.StatusMessage = $"{Path.GetFileName(path)} はすでに開いています。";
            return;
        }

        try
        {
            var document = AddDocument(_store.Load(path), path);
            document.StatusMessage = $"{Path.GetFileName(path)} を開きました。";
            ActiveDocument = document;
            PersistSession();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"開けませんでした。\n\n{ex.Message}", "ToDoTree", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseProject(MainViewModel? document)
    {
        if (document is null || !Documents.Contains(document) || !document.ConfirmDiscard())
        {
            return;
        }

        var index = Documents.IndexOf(document);
        var wasActive = ReferenceEquals(ActiveDocument, document);
        document.DeleteRecoveryFile();
        document.DocumentStateChanged -= OnDocumentStateChanged;
        Documents.Remove(document);

        if (Documents.Count == 0)
        {
            var project = new TodoProject { Name = "新しいプロジェクト" };
            var graph = new TodoGraph(project);
            var start = graph.AddNode(new TodoNode { Title = "スタート", Kind = NodeKind.Start, X = 80, Y = 200 });
            var goal = graph.AddNode(new TodoNode { Title = "ゴール", Kind = NodeKind.Goal, X = 560, Y = 200 });
            graph.Connect(start.Id, goal.Id);
            ActiveDocument = AddDocument(project, null);
        }
        else if (wasActive)
        {
            ActiveDocument = Documents[Math.Min(index, Documents.Count - 1)];
        }

        PersistSession();
    }

    /// <summary>アプリを閉じる前に、すべてのタブの未保存状態を確認する。</summary>
    public bool ConfirmCloseAll()
    {
        var discardedUnsaved = new HashSet<Guid>();

        foreach (var document in Documents.Where(document => document.IsDirty))
        {
            if (!document.ConfirmDiscard())
            {
                return false;
            }

            // 「保存しない」を選ぶと IsDirty は残る。終了後に復元しないよう印を付ける。
            if (document.IsDirty && string.IsNullOrEmpty(document.FilePath))
            {
                discardedUnsaved.Add(document.DocumentId);
            }
        }

        foreach (var document in Documents.Where(document => discardedUnsaved.Contains(document.DocumentId)))
        {
            document.DeleteRecoveryFile();
        }

        PersistSession(discardedUnsaved);
        _autoSaveTimer.Stop();
        return true;
    }

    private void AutoSaveAll()
    {
        foreach (var document in Documents.ToArray())
        {
            document.AutoSave();
        }

        PersistSession();
    }

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        PersistSession();
    }

    private void PersistSession(IReadOnlySet<Guid>? excludedUnsaved = null)
    {
        if (_restoringSession)
        {
            return;
        }

        _settings.OpenProjects = [.. Documents
            .Where(document => !string.IsNullOrEmpty(document.FilePath)
                               || (File.Exists(document.RecoveryFilePath)
                                   && (excludedUnsaved is null || !excludedUnsaved.Contains(document.DocumentId))))
            .Select(document => new ProjectSessionState
            {
                DocumentId = document.DocumentId,
                FilePath = document.FilePath,
                Zoom = document.ViewZoom,
                PanX = document.ViewPanX,
                PanY = document.ViewPanY,
                HasViewportState = document.HasViewportState,
            })];

        _settings.ActiveDocumentId = ActiveDocument is not null
                                     && _settings.OpenProjects.Any(state => state.DocumentId == ActiveDocument.DocumentId)
            ? ActiveDocument.DocumentId
            : _settings.OpenProjects.FirstOrDefault()?.DocumentId;
        _settings.LastFilePath = ActiveDocument?.FilePath;
        _settings.HasWorkspaceSession = true;
        _settings.Save();
    }

    private static string RecoveryPath(Guid documentId) =>
        Path.Combine(RecoveryDirectory, $"{documentId:N}.todotree.json");
}
