using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ToDoTree.App.Services;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxHistory = 100;

    private readonly IProjectStore _store;
    private readonly AppSettings _settings;
    private readonly string _recoveryDirectory;
    private readonly List<TodoProject> _undo = [];
    private readonly List<TodoProject> _redo = [];
    private readonly Dictionary<Guid, NodeViewModel> _byId = [];

    private TodoProject _project;
    private TodoGraph _graph;
    private string? _filePath;
    private bool _isDirty;
    private bool _rebuildingSidebar;
    private NodeViewModel? _selectedNode;
    private string _searchText = string.Empty;
    private bool _hideCompleted;
    private string _statusMessage = "Enter で次のステップ、ドラッグで移動、右端の丸から線を引いて繋げます。";
    private string _lastUndoKey = string.Empty;
    private DateTime _lastUndoAt = DateTime.MinValue;
    private ProgressSummary _progress = ProgressSummary.Empty;

    /// <summary>1 つのプロジェクトタブが持つ編集状態。</summary>
    public MainViewModel(
        IProjectStore store,
        AppSettings settings,
        TodoProject project,
        string? filePath,
        string recoveryDirectory,
        Guid? documentId = null,
        bool isDirty = false)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _graph = new TodoGraph(_project);
        _filePath = filePath;
        _recoveryDirectory = recoveryDirectory;
        DocumentId = documentId ?? Guid.NewGuid();
        Direction = _settings.Direction;

        SaveCommand = new RelayCommand(() => Save());
        SaveAsCommand = new RelayCommand(() => SaveAs());
        AddRootCommand = new RelayCommand(() => AddNode(null, sibling: false));
        AddChildCommand = new RelayCommand(() => AddNode(SelectedNode, sibling: false));
        AddSiblingCommand = new RelayCommand(() => AddNode(SelectedNode, sibling: true), () => SelectedNode is not null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => SelectedNode is not null);
        ToggleDoneCommand = new RelayCommand(ToggleDone, () => SelectedNode is not null);
        AutoLayoutCommand = new RelayCommand(AutoLayout);
        ToggleDirectionCommand = new RelayCommand(ToggleDirection);
        ZoomFitCommand = new RelayCommand(() => ZoomToFitRequested?.Invoke(this, EventArgs.Empty));
        ZoomInCommand = new RelayCommand(() => ZoomStepRequested?.Invoke(this, 1.2));
        ZoomOutCommand = new RelayCommand(() => ZoomStepRequested?.Invoke(this, 1 / 1.2));
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        FocusNodeCommand = new RelayCommand(parameter =>
        {
            if (parameter is NodeViewModel node)
            {
                SelectedNode = node;
                CenterOnRequested?.Invoke(this, node);
            }
        });
        RemoveParentLinkCommand = new RelayCommand(parameter => RemoveLink(parameter as NodeViewModel, isParent: true));
        RemoveChildLinkCommand = new RelayCommand(parameter => RemoveLink(parameter as NodeViewModel, isParent: false));

        InitializeSelection();
        InitializeView();
        InitializePlanning();
        LoadProject(project, filePath);
        IsDirty = isDirty;
    }

    // ---- ビューに向けたお知らせ ----

    /// <summary>線を引き直してほしい。</summary>
    public event EventHandler? VisualsChanged;

    public event EventHandler? ZoomToFitRequested;

    public event EventHandler<double>? ZoomStepRequested;

    public event EventHandler<NodeViewModel>? CenterOnRequested;

    /// <summary>タブ名、保存先、未保存状態などが変わった。</summary>
    public event EventHandler? DocumentStateChanged;

    /// <summary>別タブですでに使っている保存先への上書きを防ぐため、ワークスペースが設定する。</summary>
    public Func<string, bool>? CanSaveToPath { get; set; }

    // ---- コレクション ----

    public ObservableCollection<NodeViewModel> Nodes { get; } = [];

    public ObservableCollection<EdgeViewModel> Edges { get; } = [];

    /// <summary>左側の一覧（並べ替え・絞り込み済み）。</summary>
    public ObservableCollection<NodeViewModel> SidebarNodes { get; } = [];

    public IReadOnlyList<NodeStatus> StatusValues { get; } = Enum.GetValues<NodeStatus>();

    public IReadOnlyList<NodeKind> KindValues { get; } = Enum.GetValues<NodeKind>();

    // ---- コマンド ----

    public ICommand SaveCommand { get; }

    public ICommand SaveAsCommand { get; }

    public ICommand AddRootCommand { get; }

    public ICommand AddChildCommand { get; }

    public ICommand AddSiblingCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ToggleDoneCommand { get; }

    public ICommand AutoLayoutCommand { get; }

    public ICommand ToggleDirectionCommand { get; }

    public ICommand ZoomFitCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand ClearSearchCommand { get; }

    public ICommand FocusNodeCommand { get; }

    public ICommand RemoveParentLinkCommand { get; }

    public ICommand RemoveChildLinkCommand { get; }

    // ---- 状態 ----

    public TodoGraph Graph => _graph;

    public Guid DocumentId { get; }

    public Guid ProjectId => _project.Id;

    public string? FilePath => _filePath;

    public string TabTitle => $"{_project.Name}{(IsDirty ? " *" : string.Empty)}";

    public string FilePathDisplay => string.IsNullOrEmpty(_filePath) ? "未保存のプロジェクト" : _filePath;

    /// <summary>タブを切り替えたときにキャンバス位置を戻すための一時状態。</summary>
    public double ViewZoom { get; set; } = 1;

    public double ViewPanX { get; set; }

    public double ViewPanY { get; set; }

    public bool HasViewportState { get; set; }

    public LayoutDirection Direction { get; private set; } = LayoutDirection.LeftToRight;

    public string DirectionLabel => Direction == LayoutDirection.LeftToRight ? "横に流す" : "縦に流す";

    public string ProjectName
    {
        get => _project.Name;
        set
        {
            if (_project.Name == value)
            {
                return;
            }

            PushUndo("projectName");
            _project.Name = value;
            MarkDirty();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle), nameof(TabTitle));
            DocumentStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string WindowTitle => $"{_project.Name}{(IsDirty ? " *" : string.Empty)} — ToDoTree";

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(WindowTitle), nameof(TabTitle));
                DocumentStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>いま主役になっているステップ（右の詳細パネルはこれを映す）。</summary>
    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            // 一覧を組み替えている最中の「選択解除」は無視する。
            if (_rebuildingSidebar && value is null)
            {
                return;
            }

            // 同じものを選び直したときは、複数選択を 1 つに畳む。
            if (ReferenceEquals(_selectedNode, value) && _selection.Count <= 1)
            {
                return;
            }

            SelectOnly(value);
        }
    }

    public bool HasSelection => _selectedNode is not null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                UpdateHighlights();
                RefreshSidebar();
                NotifyVisualsChanged();
            }
        }
    }

    public bool HideCompleted
    {
        get => _hideCompleted;
        set
        {
            if (SetProperty(ref _hideCompleted, value))
            {
                RefreshSidebar();
                RefreshVisibility();
                NotifyVisualsChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ProgressSummary Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressRatio));
            OnPropertyChanged(nameof(ReadyText));
        }
    }

    public string ProgressText => $"進捗 {_progress.Done} / {_progress.Total}（{_progress.Percent:0}%）";

    public double ProgressRatio => _progress.Percent / 100d;

    public string ReadyText =>
        $"着手できる {_progress.Ready} 件 ・ 進行中 {_progress.InProgress} 件 ・ 待ち {_progress.Blocked} 件"
        + (_progress.Overdue > 0 ? $" ・ 期限超過 {_progress.Overdue} 件" : string.Empty);

    // ---- 読み込み・保存 ----

    private void LoadProject(TodoProject project, string? path, Guid? selectId = null)
    {
        _project = project;
        _graph = new TodoGraph(project);
        _filePath = path;

        Nodes.Clear();
        _byId.Clear();
        foreach (var model in project.Nodes)
        {
            var vm = new NodeViewModel(model, this);
            Nodes.Add(vm);
            _byId[model.Id] = vm;
        }

        RebuildEdges();

        _selection.Clear();
        _connectSourceId = null;
        _selectedNode = null;

        if (selectId is { } id && _byId.TryGetValue(id, out var restore))
        {
            _selection.Add(restore.Id);
            _selectedNode = restore;
            restore.IsSelected = true;
        }

        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasMultipleSelected));
        OnPropertyChanged(nameof(SelectionSummary));

        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectId), nameof(FilePath), nameof(FilePathDisplay), nameof(TabTitle));
        IsDirty = false;
        RefreshAll();
    }

    public bool Save()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            return SaveAs();
        }

        return SaveTo(_filePath);
    }

    public bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = _store.FileFilter,
            Title = "名前を付けて保存",
            FileName = SanitizeFileName(_project.Name) + _store.DefaultExtension,
        };

        return dialog.ShowDialog() == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        if (CanSaveToPath?.Invoke(path) == false)
        {
            MessageBox.Show(
                "このファイルは別のタブですでに開いています。別の保存先を選んでください。",
                "ToDoTree",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        try
        {
            _store.Save(path, _project);
            _filePath = path;
            DeleteRecoveryFile();
            IsDirty = false;
            _settings.LastFilePath = path;
            _settings.Save();
            OnPropertyChanged(nameof(FilePath), nameof(FilePathDisplay));
            DocumentStateChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage = $"{Path.GetFileName(path)} に保存しました（{DateTime.Now:HH:mm}）。";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存できませんでした。\n\n{ex.Message}", "ToDoTree", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>閉じる前に保存を確認する。閉じてよければ true。</summary>
    public bool ConfirmDiscard()
    {
        if (!IsDirty)
        {
            return true;
        }

        var answer = MessageBox.Show(
            $"「{ProjectName}」に保存していない変更があります。保存しますか？",
            "ToDoTree",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return answer switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Where(c => !invalid.Contains(c))]).Trim();
        return string.IsNullOrEmpty(cleaned) ? "project" : cleaned;
    }
}
