using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;
using ToDoTree.App.Services;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>キャンバス上の 1 枚のカード。</summary>
public sealed partial class NodeViewModel(TodoNode model, MainViewModel owner) : ObservableObject
{
    /// <summary>
    /// 箱の大きさ。辺の描画位置・矩形選択・全体表示・ミニマップも、この値を見ている。
    /// 表示モードで変わるので定数ではなく <see cref="NodeMetrics"/> を通す。
    /// </summary>
    public static double CardWidth => NodeMetrics.Width;

    public static double CardHeight => NodeMetrics.Height;
    private bool _isSelected;
    private bool _isOnCriticalPath;
    private bool _isEditing;
    private bool _isVisible = true;
    private bool _isCollapsed;
    private int _hiddenCount;
    private ScheduleInfo? _schedule;
    private bool _isRelated;
    private bool _isDimmed;
    private bool _isInSelectedBlock;
    private string? _notesDraft;

    public TodoNode Model { get; } = model;

    public Guid Id => Model.Id;

    public bool HasBookmark => owner.Graph.Project.Bookmark?.NodeId == Id;

    // ---- ユーザーが編集する値 ----

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value)
            {
                return;
            }

            owner.PushUndo($"title:{Id}");
            Model.Title = value;
            Touch();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle), nameof(HasTitlePreview), nameof(CardTooltip),
                nameof(UndefinedVariableText), nameof(HasUndefinedVariables));
            owner.RefreshSidebar();
            owner.RefreshBookmark();
        }
    }

    /// <summary>
    /// 画面に出す名前。原文の <c>{Hoge}</c> をプロジェクト変数の値へ置き換えたもの。
    /// 編集欄はいつも原文（<see cref="Title"/>）を使い、非編集時だけこちらを出す。
    /// </summary>
    public string DisplayTitle => owner.VariableResolver.Expand(Model.Title);

    /// <summary>展開後のメモ。カードのツールチップと検索が見る。</summary>
    public string DisplayNotes => owner.VariableResolver.Expand(Model.Notes);

    /// <summary>
    /// 詳細パネルにプレビューを出す。原文と表示が違うときのほか、
    /// 未定義の参照だけを含むときも出す（なぜ置き換わらないのかを、その場で示すため）。
    /// </summary>
    public bool HasTitlePreview =>
        !string.Equals(Model.Title, DisplayTitle, StringComparison.Ordinal) || HasUndefinedVariables;

    public bool HasNotesPreview => !string.Equals(NotesDraft, DisplayNotesDraft, StringComparison.Ordinal);

    /// <summary>定義の無い参照の名前。カードの警告とツールチップに出す。</summary>
    public IReadOnlyList<string> UndefinedVariables
    {
        get
        {
            var resolver = owner.VariableResolver;
            var names = new List<string>(resolver.Scan(Model.Title).UndefinedNames);
            foreach (var name in resolver.Scan(Model.Notes).UndefinedNames)
            {
                if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }

            return names;
        }
    }

    public bool HasUndefinedVariables => UndefinedVariables.Count > 0;

    /// <summary>色だけに頼らず、文字でも未定義を伝える。</summary>
    public string UndefinedVariableText =>
        UndefinedVariables.Count == 0 ? string.Empty : "未定義: " + string.Join("、", UndefinedVariables);

    /// <summary>ツールチップに添える「使用中の変数」。定義のある参照だけを並べる。</summary>
    public string VariableSummary
    {
        get
        {
            var resolver = owner.VariableResolver;
            var names = resolver.Scan(Model.Title).ReferencedNames
                .Concat(resolver.Scan(Model.Notes).ReferencedNames)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return names.Count == 0 ? string.Empty : "変数: " + string.Join("、", names);
        }
    }

    public string Notes
    {
        get => Model.Notes;
        set
        {
            if (Model.Notes == value)
            {
                return;
            }

            owner.PushUndo($"notes:{Id}");
            Model.Notes = value;
            Touch();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayNotes), nameof(CardTooltip),
                nameof(UndefinedVariableText), nameof(HasUndefinedVariables));
        }
    }

    /// <summary>
    /// 詳細パネルのメモ入力欄。毎入力でプレビューだけを更新し、原文への確定は
    /// これまでどおりフォーカスが外れたとき（<see cref="CommitNotes"/>）に行う。
    /// 入力のたびに原文を書き換えると、履歴の粒度が今までと変わってしまう。
    /// </summary>
    public string NotesDraft
    {
        get => _notesDraft ?? Model.Notes;
        set
        {
            var text = value ?? string.Empty;
            if (NotesDraft == text)
            {
                return;
            }

            _notesDraft = text;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayNotesDraft), nameof(HasNotesPreview));
        }
    }

    /// <summary>入力中のメモを展開したもの。プレビュー専用。</summary>
    public string DisplayNotesDraft => owner.VariableResolver.Expand(NotesDraft);

    /// <summary>入力欄から離れたときに原文へ確定する。</summary>
    public void CommitNotes()
    {
        if (_notesDraft is not { } draft)
        {
            return;
        }

        _notesDraft = null;
        Notes = draft;
        OnPropertyChanged(nameof(NotesDraft), nameof(DisplayNotesDraft), nameof(HasNotesPreview));
    }

    public NodeStatus Status
    {
        get => Model.Status;
        set
        {
            if (Model.Status == value)
            {
                return;
            }

            // 回数つきの項目は、状態だけを書き換えられない。
            // ここを素通りさせると「3 回中 1 回なのに完了」が作れてしまう。
            if (Model.Repeat is not null)
            {
                owner.ApplyRepeatStatus(this, value);
                OnPropertyChanged();
                return;
            }

            owner.PushUndo();
            var impact = CompletionImpact.Calculate(owner.Graph, [Id]);
            Model.Status = value;
            Model.CompletedAt = value == NodeStatus.Done ? DateTimeOffset.Now : null;
            Touch();
            OnPropertyChanged();
            owner.RefreshAll();
            if (value == NodeStatus.Done) owner.PlayCompletion(impact);
            owner.AnnounceUnlocked(this);
        }
    }

    public NodeKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Model.Kind == value)
            {
                return;
            }

            owner.PushUndo();
            Model.Kind = value;
            Touch();
            OnPropertyChanged();
            owner.RefreshAll();
        }
    }

    public DateTime? DueDate
    {
        get => Model.Due?.LocalDateTime;
        set
        {
            var current = Model.Due?.LocalDateTime;
            if (current == value)
            {
                return;
            }

            owner.PushUndo();
            Model.Due = value is null ? null : new DateTimeOffset(value.Value);
            Touch();
            OnPropertyChanged();
            owner.RefreshAll();
        }
    }

    public string EstimateText
    {
        get => Model.EstimateMinutes?.ToString() ?? string.Empty;
        set
        {
            int? parsed = int.TryParse(value, out var minutes) && minutes >= 0 ? minutes : null;
            if (Model.EstimateMinutes == parsed)
            {
                OnPropertyChanged();
                return;
            }

            owner.PushUndo($"estimate:{Id}");
            Model.EstimateMinutes = parsed;
            Touch();
            OnPropertyChanged();
            RefreshDerived();
        }
    }

    public string TagsText
    {
        get => string.Join(", ", Model.Tags);
        set
        {
            var tags = (value ?? string.Empty)
                .Split([',', '、'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            if (tags.SequenceEqual(Model.Tags))
            {
                return;
            }

            owner.PushUndo($"tags:{Id}");
            Model.Tags = tags;
            Touch();
            OnPropertyChanged();
            RefreshDerived();
        }
    }

    public double X
    {
        get => Model.X;
        set
        {
            if (Math.Abs(Model.X - value) < 0.01)
            {
                return;
            }

            Model.X = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Center));
            owner.NotifyVisualsChanged();
        }
    }

    public double Y
    {
        get => Model.Y;
        set
        {
            if (Math.Abs(Model.Y - value) < 0.01)
            {
                return;
            }

            Model.Y = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Center));
            owner.NotifyVisualsChanged();
        }
    }

    public bool IsPinned
    {
        get => Model.IsPinned;
        set
        {
            if (Model.IsPinned == value)
            {
                return;
            }

            Model.IsPinned = value;
            Touch();
            OnPropertyChanged();
        }
    }

    // ---- 表示のための派生値 ----

    public Point Center => new(X + CardWidth / 2, Y + CardHeight / 2);

    public Readiness Readiness => owner.Graph.ReadinessOf(Model);

    public string StatusLabel => IsManuallyBlocked ? "ブロック中" : Labels.Of(Readiness) + (Model.Status == NodeStatus.InProgress && owner.Graph.ParentsOf(Id).Any(n => !n.IsSettled) ? "・先行に未完了あり" : "");

    public string CompletedTimeText => Model.CompletedAt is { } completedAt
        ? $"{completedAt.LocalDateTime:HH:mm} 完了" : string.Empty;

    // ---- 回数で完了する項目 ----

    /// <summary>回数で完了する項目。null 判定はここに集約する。</summary>
    public bool IsRepeating => Model.Repeat is not null;

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public Geometry RepeatLoopPath => RepeatLoopVisuals.Path;
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public Geometry RepeatLoopArrow => RepeatLoopVisuals.Arrow;
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public double RepeatLoopWidth => RepeatLoopVisuals.Width;
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public double RepeatLoopHeight => RepeatLoopVisuals.Height;
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public Thickness RepeatLoopMargin => RepeatLoopVisuals.Margin;
    public Rect VisualBounds => RepeatLoopVisuals.Bounds(X, Y, IsRepeating);


    /// <summary>カードのバッジに出す「2 / 3 回」。通常の項目では空。</summary>
    public string RepeatText => RepeatService.Describe(Model);

    /// <summary>ミニマル表示に添える「2/3」。狭いので単位は落とす。</summary>
    public string RepeatCompactText =>
        Model.Repeat is { } repeat ? $"{repeat.CompletedCount}/{repeat.TargetCount}" : string.Empty;

    /// <summary>いま「1回達成」を押せる（取り消し中と上限では押せない）。</summary>
    public bool CanAdvanceRepeat => RepeatService.CanAdvance(Model);

    /// <summary>目標に届いている。ボタンをチェック表示へ変える。</summary>
    public bool IsRepeatFull => Model.Repeat is { IsFull: true };

    /// <summary>次の1回で完了する。</summary>
    public bool IsRepeatFinalNext => RepeatService.IsFinalNext(Model);

    /// <summary>色だけに頼らず、回数とチェックでも状態を伝える。</summary>
    public string RepeatActionGlyph => IsRepeatFull ? "✓ 達成済み" : "＋1 回";

    /// <summary>読み上げ名。ボタンだけを聞いても、いま何回目かが分かるようにする。</summary>
    public string RepeatActionName => Model.Repeat is { } repeat
        ? IsRepeatFull
            ? $"{DisplayTitle}は達成済み、{repeat.CompletedCount}回、目標{repeat.TargetCount}回"
            : $"{DisplayTitle}を1回達成、現在{repeat.CompletedCount}回、目標{repeat.TargetCount}回"
        : string.Empty;

    /// <summary>加算ボタンのツールチップ。最終回だけ完了になることを明示する。</summary>
    public string RepeatActionTooltip => Model.Repeat is { } repeat
        ? Model.Status == NodeStatus.Cancelled
            ? $"取り消し中です。{repeat.CompletedCount} / {repeat.TargetCount} 回は残しています"
            : IsRepeatFull
                ? $"{repeat.TargetCount} 回すべて達成しました"
                : IsRepeatFinalNext
                    ? $"次の1回で完了：{repeat.CompletedCount + 1} / {repeat.TargetCount} 回"
                    : $"次の1回：{repeat.CompletedCount + 1} / {repeat.TargetCount} 回"
        : string.Empty;

    /// <summary>バッジの文字色。完了と取り消しは他の文字と同じく落とす。</summary>
    public Brush RepeatTextBrush => Readiness is Readiness.Done or Readiness.Cancelled
        ? NodePalette.DoneTextBrush
        : NodePalette.RepeatText;

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "WPFのDataContext経由のインスタンスバインディングに使用するため。")]
    public Brush RepeatBadgeFill => NodePalette.RepeatBadgeFill;

    public Brush RepeatLoopBrush => Readiness is Readiness.Done or Readiness.Cancelled
        ? NodePalette.DoneTextBrush
        : NodePalette.RepeatLoop;

    public string KindLabel => Labels.Of(Kind);

    public Brush Fill => NodePalette.FillOf(Readiness);

    public Brush KindAccent => NodePalette.AccentOf(Kind);

    public Brush BorderBrush => IsSelected
        ? NodePalette.SelectedStroke
        : IsOnCriticalPath ? NodePalette.CriticalStroke : NodePalette.StrokeOf(Readiness);

    public Thickness CardBorderThickness =>
        new(IsSelected || IsOnCriticalPath ? 2.5 : Kind is NodeKind.Goal or NodeKind.Start ? 2 : 1.4);

    /// <summary>最長経路の上にある（ここが遅れると全体が遅れる）。</summary>
    public bool IsOnCriticalPath
    {
        get => _isOnCriticalPath;
        set
        {
            if (SetProperty(ref _isOnCriticalPath, value))
            {
                OnPropertyChanged(nameof(BorderBrush), nameof(CardBorderThickness), nameof(HasRing), nameof(RingBrush));
            }
        }
    }

    /// <summary>期限から逆算した「いつまでに着手・完了すべきか」。</summary>
    public ScheduleInfo? Schedule
    {
        get => _schedule;
        set
        {
            _schedule = value;
            OnPropertyChanged(nameof(IsAtRisk), nameof(AlertText), nameof(HasAlert), nameof(AlertBrush), nameof(CardTooltip));
        }
    }

    /// <summary>逆算した開始日をすでに過ぎている。</summary>
    public bool IsAtRisk => _schedule?.AtRisk == true && !Model.IsSettled;

    public string AlertText => IsOverdue ? "期限超過" : IsAtRisk ? "要着手" : string.Empty;

    public bool HasAlert => AlertText.Length > 0;

    public Brush AlertBrush => IsOverdue ? NodePalette.OverdueBrush : NodePalette.AtRiskBrush;

    public string CardTooltip
    {
        get
        {
            var lines = new List<string> { DisplayTitle };

            // 変数を使っているカードでは、置き換わる前の原文も添える。
            // 表示だけを見て「なぜこの名前なのか」を追えないと、値を直す先が分からない。
            if (HasTitlePreview) lines.Add($"原文：{Title}");
            if (VariableSummary is { Length: > 0 } summary) lines.Add(summary);
            if (UndefinedVariableText is { Length: > 0 } undefined) lines.Add(undefined);
            if (IsManuallyBlocked) lines.Add(string.IsNullOrWhiteSpace(BlockReason) ? "ブロック中（理由未入力）" : $"ブロック中：{BlockReason}");
            if (Model.Checklist.Count > 0) lines.Add(ChecklistSummary);
            if (DisplayNotes.Length > 0)
            {
                lines.Add(DisplayNotes.Length > 120 ? DisplayNotes[..120] + "…" : DisplayNotes);
            }

            if (_schedule?.LatestStart is { } start)
            {
                lines.Add($"{start.LocalDateTime:M/d} までに着手しないと間に合いません");
            }

            return string.Join("\n", lines);
        }
    }

    public Brush TitleBrush => Readiness is Readiness.Done or Readiness.Cancelled
        ? NodePalette.DoneTextBrush
        : NodePalette.TextBrush;

    public Brush StatusBrush => IsManuallyBlocked ? NodePalette.AtRiskBrush : NodePalette.StrokeOf(Readiness);

    public double CardOpacity => _isDimmed ? 0.35 : Readiness is Readiness.Done or Readiness.Cancelled ? 0.75 : 1d;

    public TextDecorationCollection? TitleDecorations =>
        Readiness is Readiness.Done or Readiness.Cancelled ? TextDecorations.Strikethrough : null;

    public bool IsOverdue => Model.IsOverdue;

    // ---- ミニマル表示（丸ひとつ）のための値 ----

    /// <summary>丸の塗り。進行中と完了だけ塗りつぶし、あとは中抜きにする。</summary>
    public Brush DotFill => Readiness switch
    {
        Readiness.InProgress => NodePalette.StrokeOf(Readiness.InProgress),
        Readiness.Done or Readiness.Cancelled => NodePalette.DoneTextBrush,
        _ => Brushes.Transparent,
    };

    public Brush DotStroke => StatusBrush;

    /// <summary>着手できるものだけ輪郭を太くして、目が先に行くようにする。</summary>
    public double DotStrokeThickness => Readiness switch
    {
        Readiness.Ready => 2.2,
        Readiness.InProgress or Readiness.Done or Readiness.Cancelled => 0,
        _ => 1.5,
    };

    /// <summary>進行中の丸のまわりに敷く、薄い輪。</summary>
    public bool HasHalo => Readiness == Readiness.InProgress;

    /// <summary>丸の外側の輪。最長経路と種別の両方に当たるときは、最長経路を優先する。</summary>
    public bool HasRing => IsOnCriticalPath || Kind is NodeKind.Start or NodeKind.Milestone or NodeKind.Goal;

    public Brush RingBrush => IsOnCriticalPath ? NodePalette.CriticalStroke : NodePalette.AccentOf(Kind);

    /// <summary>畳んでいるときに名前のうしろへ添える「＋3」。</summary>
    public string CollapsedBadge => IsCollapsed && HiddenCount > 0 ? $"＋{HiddenCount}" : string.Empty;

    public bool HasCollapsedBadge => CollapsedBadge.Length > 0;

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (Model.Checklist.Count > 0) parts.Add(ChecklistSummary);
            if (Model.Due is { } due)
            {
                parts.Add($"〆 {due.LocalDateTime:M/d}");
            }

            if (Model.EstimateMinutes is { } minutes && minutes > 0)
            {
                parts.Add(minutes >= 60 ? $"{minutes / 60d:0.#}h" : $"{minutes}分");
            }

            if (Model.Tags.Count > 0)
            {
                parts.Add("#" + string.Join(" #", Model.Tags));
            }

            return string.Join("   ", parts);
        }
    }

    public bool HasMeta => MetaText.Length > 0;

    public int GroupOrder => Readiness switch
    {
        Readiness.InProgress => 0,
        Readiness.Ready => 1,
        Readiness.Blocked => 2,
        Readiness.Done => 3,
        _ => 4,
    };

    public string GroupLabel => Readiness switch
    {
        Readiness.InProgress => "進行中",
        Readiness.Ready => "着手できる",
        Readiness.Blocked => "待ち",
        Readiness.Done => "完了",
        _ => "取り消し",
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BorderBrush), nameof(CardBorderThickness), nameof(ZIndex));
            }
        }
    }

    /// <summary>選択ノードの上流・下流にいる（薄く強調する）。</summary>
    public bool IsRelated
    {
        get => _isRelated;
        set
        {
            if (SetProperty(ref _isRelated, value))
            {
                OnPropertyChanged(nameof(ZIndex));
            }
        }
    }

    /// <summary>選択中のブロックに入っている（どれが所属かを控えめに示す）。</summary>
    public bool IsInSelectedBlock
    {
        get => _isInSelectedBlock;
        set => SetProperty(ref _isInSelectedBlock, value);
    }

    /// <summary>検索でヒットしなかったので目立たせない。</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        set
        {
            if (SetProperty(ref _isDimmed, value))
            {
                OnPropertyChanged(nameof(CardOpacity));
            }
        }
    }

    public int ZIndex => IsEditing ? 400 : IsSelected ? 300 : IsRelated ? 200 : 100;

    /// <summary>カードの上で名前を書き換えている最中。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(ZIndex));
            }
        }
    }

    /// <summary>いま画面に出ているか（折りたたみ・絞り込み・完了隠しの結果）。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>この先を畳んでいる。</summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetProperty(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(CollapseLabel), nameof(CollapsedBadge), nameof(HasCollapsedBadge));
            }
        }
    }

    /// <summary>畳んだことで隠れているステップの数。</summary>
    public int HiddenCount
    {
        get => _hiddenCount;
        set
        {
            if (SetProperty(ref _hiddenCount, value))
            {
                OnPropertyChanged(nameof(CollapseLabel), nameof(CollapsedBadge), nameof(HasCollapsedBadge));
            }
        }
    }

    public string CollapseLabel => IsCollapsed
        ? (HiddenCount > 0 ? $"▸ {HiddenCount}" : "▸")
        : "▾";

    /// <summary>畳める（後続がある）。</summary>
    public bool CanCollapse => Children.Count > 0;

    public IReadOnlyList<NodeViewModel> Parents => owner.ParentsOf(this);

    public IReadOnlyList<NodeViewModel> Children => owner.ChildrenOf(this);

    public bool HasParents => Parents.Count > 0;

    public bool HasChildren => Children.Count > 0;

    /// <summary>状態やグラフが変わったあと、表示用の値をまとめて更新する。</summary>
    public void RefreshDerived() => OnPropertyChanged(
        nameof(IsManuallyBlocked), nameof(CanBlock), nameof(BlockReason), nameof(BlockToggleLabel),
        nameof(ChecklistSummary), nameof(BlockingCauses), nameof(BlockingSummary),
        nameof(HasBookmark),
        nameof(Readiness),
        nameof(StatusLabel),
        nameof(CompletedTimeText),
        nameof(KindLabel),
        nameof(Fill),
        nameof(KindAccent),
        nameof(BorderBrush),
        nameof(CardBorderThickness),
        nameof(TitleBrush),
        nameof(StatusBrush),
        nameof(CardOpacity),
        nameof(TitleDecorations),
        nameof(IsOverdue),
        nameof(DotFill),
        nameof(DotStroke),
        nameof(DotStrokeThickness),
        nameof(HasHalo),
        nameof(HasRing),
        nameof(RingBrush),
        nameof(CollapsedBadge),
        nameof(HasCollapsedBadge),
        nameof(IsRepeating),
        nameof(RepeatLoopPath),
        nameof(RepeatLoopArrow),
        nameof(RepeatLoopWidth),
        nameof(RepeatLoopHeight),
        nameof(RepeatLoopMargin),
        nameof(VisualBounds),
        nameof(RepeatText),
        nameof(RepeatCompactText),
        nameof(CanAdvanceRepeat),
        nameof(IsRepeatFull),
        nameof(IsRepeatFinalNext),
        nameof(RepeatActionGlyph),
        nameof(RepeatActionName),
        nameof(RepeatActionTooltip),
        nameof(RepeatTextBrush),
        nameof(RepeatBadgeFill),
        nameof(RepeatLoopBrush),
        nameof(IsAtRisk),
        nameof(AlertText),
        nameof(HasAlert),
        nameof(AlertBrush),
        nameof(CardTooltip),
        nameof(MetaText),
        nameof(HasMeta),
        nameof(GroupLabel),
        nameof(GroupOrder),
        nameof(Status),
        nameof(Kind),
        nameof(Title),
        nameof(DisplayTitle),
        nameof(DisplayNotes),
        nameof(DisplayNotesDraft),
        nameof(HasTitlePreview),
        nameof(HasNotesPreview),
        nameof(UndefinedVariables),
        nameof(HasUndefinedVariables),
        nameof(UndefinedVariableText),
        nameof(VariableSummary),
        nameof(DueDate),
        nameof(EstimateText),
        nameof(TagsText),
        nameof(IsPinned),
        nameof(Parents),
        nameof(Children),
        nameof(HasParents),
        nameof(HasChildren),
        nameof(CanCollapse),
        nameof(ZIndex));

    /// <summary>
    /// 変数の値が変わったときに呼ぶ。表示用の文字列だけを更新し、
    /// 状態・接続・配置には触れない（値を直すたびに図を作り直さないため）。
    /// </summary>
    public void RefreshDisplayText() => OnPropertyChanged(
        nameof(DisplayTitle),
        nameof(DisplayNotes),
        nameof(DisplayNotesDraft),
        nameof(HasTitlePreview),
        nameof(HasNotesPreview),
        nameof(UndefinedVariables),
        nameof(HasUndefinedVariables),
        nameof(UndefinedVariableText),
        nameof(VariableSummary),
        nameof(CardTooltip));

    /// <summary>自動整列などでモデルの座標を直接書き換えたあとに呼ぶ。</summary>
    public void NotifyPositionChanged() => OnPropertyChanged(nameof(X), nameof(Y), nameof(Center), nameof(VisualBounds));

    private void Touch()
    {
        Model.UpdatedAt = DateTimeOffset.Now;
        owner.MarkDirty();
    }

    public override string ToString() => DisplayTitle;
}
