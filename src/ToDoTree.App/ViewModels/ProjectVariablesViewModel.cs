using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// 「プロジェクト変数…」の下書き。
///
/// 画面の中では実プロジェクトに触れない。保存を押したときだけ、定義一覧と
/// 「変更前の名前 → 変更後の名前」の対応を <see cref="MainViewModel.ApplyProjectVariables"/> へ渡す。
/// 途中の入力が自動保存や履歴に混ざらないようにするため。
/// </summary>
public sealed class ProjectVariablesViewModel : ObservableObject
{
    private readonly TodoProject _project;
    private readonly ProjectVariableResolver _saved;
    private VariableRowViewModel? _selectedRow;
    private ICommand? _addCommand;
    private ICommand? _copyReferenceCommand;
    private ICommand? _defineUndefinedCommand;

    public ProjectVariablesViewModel(TodoProject project, IEnumerable<ProjectVariable> definitions)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _saved = ProjectVariableResolver.From(project);

        var counts = ProjectVariableService.CountUsages(project, _saved);
        foreach (var variable in definitions)
        {
            counts.TryGetValue(variable.Name, out var used);
            Rows.Add(Track(VariableRowViewModel.Existing(variable.Name, variable.Value, used)));
        }

        Rows.CollectionChanged += (_, _) => Revalidate();
        SelectedRow = Rows.FirstOrDefault();
        Revalidate();
    }

    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];

    /// <summary>選んだ変数の使用箇所（変更前・変更後）。</summary>
    public ObservableCollection<VariableUsagePreview> Usages { get; } = [];

    /// <summary>定義の無い参照。ここから定義を足せる。</summary>
    public ObservableCollection<UndefinedVariableRow> Undefined { get; } = [];

    public string ProjectName => _project.Name;

    public string ScopeDescription => ProjectVariableService.ScopeDescription;

    public string Summary =>
        $"{Rows.Count(r => !r.IsDeleted)} 件の変数 ・ 未定義の参照 {Undefined.Count} 件";

    /// <summary>保存できないときの理由。空なら保存できる。</summary>
    public string ValidationMessage { get; private set; } = string.Empty;

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    public bool CanSave => ValidationMessage.Length == 0;

    public bool HasUsages => Usages.Count > 0;

    public bool HasUndefined => Undefined.Count > 0;

    public string UsageHeader => SelectedRow is { } row
        ? $"「{row.Name}」の使用箇所 {Usages.Count} 件"
        : "使用箇所";

    public VariableRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value)) RefreshUsages();
        }
    }

    public ICommand AddCommand => _addCommand ??= new RelayCommand(() =>
    {
        var row = Track(VariableRowViewModel.Added(UniqueName()));
        Rows.Add(row);
        SelectedRow = row;
    });

    /// <summary>参照の書き方をそのままコピーする（初版では入力補完の代わり）。</summary>
    public ICommand CopyReferenceCommand => _copyReferenceCommand ??= new RelayCommand(value =>
    {
        if (value is not VariableRowViewModel row) return;
        try
        {
            System.Windows.Clipboard.SetText(ProjectVariableResolver.Reference(row.Name));
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // 他のアプリがクリップボードを掴んでいるだけ。下書きは壊さない。
        }
    });

    /// <summary>未定義の名前を、そのまま定義として足す。</summary>
    public ICommand DefineUndefinedCommand => _defineUndefinedCommand ??= new RelayCommand(value =>
    {
        if (value is not UndefinedVariableRow undefined) return;
        var row = Track(VariableRowViewModel.Added(undefined.Name, undefined.Fields));
        Rows.Add(row);
        SelectedRow = row;
    });

    /// <summary>保存する定義。削除した行は落とす。</summary>
    public List<ProjectVariable> Definitions =>
        [.. Rows.Where(r => !r.IsDeleted).Select(r => new ProjectVariable { Name = r.Name, Value = r.Value })];

    /// <summary>変更前の名前から変更後の名前への対応。追加した行と削除した行は含めない。</summary>
    public Dictionary<string, string> Renames
    {
        get
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in Rows)
            {
                if (row.IsDeleted || row.OriginalName is not { } original) continue;
                if (!string.Equals(original, row.Name, StringComparison.Ordinal)) map[original] = row.Name;
            }

            return map;
        }
    }

    public void Remove(VariableRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // 追加したばかりの行は、下書きから消しても失うものが無い。
        // もとからある定義は、参照が未定義になることを見せてから保存で確定する。
        if (row.OriginalName is null) Rows.Remove(row);
        else row.IsDeleted = !row.IsDeleted;

        Revalidate();
    }

    private VariableRowViewModel Track(VariableRowViewModel row)
    {
        row.Changed += Revalidate;
        return row;
    }

    private string UniqueName()
    {
        var names = Rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        for (var i = 1; ; i++)
        {
            var candidate = $"変数{i}";
            if (names.Add(candidate)) return candidate;
        }
    }

    private void Revalidate()
    {
        ValidationMessage = ProjectVariableService.Validate(Definitions) ?? string.Empty;
        RefreshUndefined();
        RefreshUsages();
        OnPropertyChanged(nameof(ValidationMessage), nameof(HasValidationMessage), nameof(CanSave), nameof(Summary));
    }

    private ProjectVariableResolver DraftResolver() => ProjectVariableResolver.From(Definitions);

    private void RefreshUndefined()
    {
        Undefined.Clear();

        // 改名を先に反映した原文で見る。改名しただけの参照を「未定義になった」と誤って出さない。
        var renames = Renames;
        var draft = DraftResolver();
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var field in ProjectVariableService.TargetFields(_project))
        {
            var rewritten = ProjectVariableService.Rewrite(_saved, field.Value, renames);
            foreach (var name in draft.Scan(rewritten).UndefinedNames)
            {
                if (counts.TryGetValue(name, out var current))
                {
                    counts[name] = current + 1;
                }
                else
                {
                    order.Add(name);
                    counts[name] = 1;
                }
            }
        }

        foreach (var name in order) Undefined.Add(new UndefinedVariableRow(name, counts[name]));
        OnPropertyChanged(nameof(HasUndefined), nameof(Summary));
    }

    private void RefreshUsages()
    {
        Usages.Clear();
        if (SelectedRow is { } row)
        {
            var renames = Renames;
            var draft = DraftResolver();

            // 変更前の名前と変更後の名前の両方で拾う。改名先にもとから未定義の参照が
            // あった場合、それも解決されることを見せるため。
            var targets = new HashSet<string>(StringComparer.Ordinal) { row.Name };
            if (row.OriginalName is { } original) targets.Add(original);

            foreach (var field in ProjectVariableService.TargetFields(_project))
            {
                var source = field.Value;
                var scan = _saved.Scan(source);
                var occurrences = scan.Segments.Count(s =>
                    s.Name is { } name && targets.Contains(name)
                    && s.Kind is VariableSegmentKind.Reference or VariableSegmentKind.UndefinedReference);

                if (occurrences == 0) continue;

                var after = draft.Expand(ProjectVariableService.Rewrite(_saved, source, renames));
                Usages.Add(new VariableUsagePreview(
                    field.KindLabel, scan.Display, after, occurrences,
                    row.IsDeleted || !targets.Contains(row.Name) ? "参照は未定義になります" : string.Empty));
            }
        }

        OnPropertyChanged(nameof(HasUsages), nameof(UsageHeader));
    }
}

/// <summary>設定画面の 1 行。改名の対応づけのために、変更前の名前を持ち続ける。</summary>
public sealed class VariableRowViewModel : ObservableObject
{
    private string _name;
    private string _value;
    private bool _isDeleted;
    private ICommand? _deleteCommand;

    private VariableRowViewModel(string? originalName, string name, string value, int usageCount)
    {
        OriginalName = originalName;
        _name = name;
        _value = value;
        UsageCount = usageCount;
    }

    /// <summary>もとからある定義。改名の対応づけのために、変更前の名前を持ち続ける。</summary>
    public static VariableRowViewModel Existing(string name, string value, int usageCount) =>
        new(name, name, value, usageCount);

    /// <summary>この画面で足した定義。保存するまで、書き換える参照は持たない。</summary>
    public static VariableRowViewModel Added(string name, int undefinedFields = 0) =>
        new(null, name, "値", undefinedFields);

    /// <summary>この下書きの中だけで使う識別子。保存形式には出さない。</summary>
    public Guid DraftId { get; } = Guid.NewGuid();

    /// <summary>読み込んだときの名前。null なら画面で追加した行。</summary>
    public string? OriginalName { get; }

    public event Action? Changed;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsRenamed), nameof(StateText), nameof(Reference));
                Changed?.Invoke();
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? string.Empty)) Changed?.Invoke();
        }
    }

    /// <summary>保存すると消える行。参照の原文は残り、未定義になる。</summary>
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (SetProperty(ref _isDeleted, value))
            {
                OnPropertyChanged(nameof(StateText), nameof(DeleteLabel), nameof(HasState));
                Changed?.Invoke();
            }
        }
    }

    /// <summary>変更前の使用箇所数（フィールド単位）。</summary>
    public int UsageCount { get; }

    public string UsageText => UsageCount == 0 ? "未使用" : $"{UsageCount} 箇所";

    public bool IsRenamed => OriginalName is { } original && !string.Equals(original, Name, StringComparison.Ordinal);

    public string Reference => ProjectVariableResolver.Reference(Name);

    public string DeleteLabel => IsDeleted ? "取り消す" : "削除";

    public string StateText => (IsDeleted, IsRenamed, OriginalName is null) switch
    {
        (true, _, _) when UsageCount > 0 => $"削除予定 ・ {UsageCount} 箇所の参照は未定義になります",
        (true, _, _) => "削除予定",
        (_, true, _) => $"「{OriginalName}」から改名 ・ {UsageCount} 箇所の参照を書き換えます",
        (_, _, true) => "追加",
        _ => string.Empty,
    };

    public bool HasState => StateText.Length > 0;

    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(parameter =>
    {
        if (parameter is ProjectVariablesViewModel owner) owner.Remove(this);
    });
}

/// <summary>使用箇所 1 件の、変更前と変更後の表示。</summary>
/// <param name="KindLabel">タイトル・メモ・受信箱のどれか。</param>
/// <param name="Before">いまの定義での表示。</param>
/// <param name="After">保存したあとの表示。</param>
/// <param name="Occurrences">このフィールド内での出現回数。</param>
/// <param name="Warning">未定義になるなどの注意書き。空なら出さない。</param>
public sealed record VariableUsagePreview(
    string KindLabel, string Before, string After, int Occurrences, string Warning)
{
    public bool HasWarning => Warning.Length > 0;

    public string OccurrenceText => Occurrences > 1 ? $"{KindLabel}（{Occurrences} 回）" : KindLabel;

    public bool IsChanged => !string.Equals(Before, After, StringComparison.Ordinal);
}

/// <summary>定義の無い参照 1 件。</summary>
public sealed record UndefinedVariableRow(string Name, int Fields)
{
    public string Text => $"{ProjectVariableResolver.Reference(Name)} ・ {Fields} 箇所";
}
