using System.Windows;
using System.Windows.Media;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>キャンバス上の 1 枚のカード。</summary>
public sealed class NodeViewModel(TodoNode model, MainViewModel owner) : ObservableObject
{
    /// <summary>カードの大きさ。辺の描画位置もこの値を使う。</summary>
    public const double CardWidth = 224;

    public const double CardHeight = 88;
    private bool _isSelected;
    private bool _isOnCriticalPath;
    private bool _isEditing;
    private bool _isVisible = true;
    private bool _isCollapsed;
    private int _hiddenCount;
    private ScheduleInfo? _schedule;
    private bool _isRelated;
    private bool _isDimmed;

    public TodoNode Model { get; } = model;

    public Guid Id => Model.Id;

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
            owner.RefreshSidebar();
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
        }
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

            owner.PushUndo();
            Model.Status = value;
            Model.CompletedAt = value == NodeStatus.Done ? DateTimeOffset.Now : null;
            Touch();
            OnPropertyChanged();
            owner.RefreshAll();
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

    public string StatusLabel => Labels.Of(Readiness);

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
                OnPropertyChanged(nameof(BorderBrush), nameof(CardBorderThickness));
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
            var lines = new List<string> { Title };
            if (Model.Notes.Length > 0)
            {
                lines.Add(Model.Notes.Length > 120 ? Model.Notes[..120] + "…" : Model.Notes);
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

    public Brush StatusBrush => NodePalette.StrokeOf(Readiness);

    public double CardOpacity => _isDimmed ? 0.35 : Readiness is Readiness.Done or Readiness.Cancelled ? 0.75 : 1d;

    public TextDecorationCollection? TitleDecorations =>
        Readiness is Readiness.Done or Readiness.Cancelled ? TextDecorations.Strikethrough : null;

    public bool IsOverdue => Model.IsOverdue;

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
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
                OnPropertyChanged(nameof(CollapseLabel));
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
                OnPropertyChanged(nameof(CollapseLabel));
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
        nameof(Readiness),
        nameof(StatusLabel),
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

    /// <summary>自動整列などでモデルの座標を直接書き換えたあとに呼ぶ。</summary>
    public void NotifyPositionChanged() => OnPropertyChanged(nameof(X), nameof(Y), nameof(Center));

    private void Touch()
    {
        Model.UpdatedAt = DateTimeOffset.Now;
        owner.MarkDirty();
    }

    public override string ToString() => Title;
}
