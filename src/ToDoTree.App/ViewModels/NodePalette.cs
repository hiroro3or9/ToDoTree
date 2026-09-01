using System.Windows.Media;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>状態がひと目で分かるようにするための色。</summary>
public static class NodePalette
{
    public static readonly Brush ReadyFill = Frozen("#FFF1FDF5");
    public static readonly Brush ReadyStroke = Frozen("#FF22C55E");

    public static readonly Brush InProgressFill = Frozen("#FFEFF6FF");
    public static readonly Brush InProgressStroke = Frozen("#FF3B82F6");

    public static readonly Brush BlockedFill = Frozen("#FFFFFFFF");
    public static readonly Brush BlockedStroke = Frozen("#FFD9DEE8");

    public static readonly Brush DoneFill = Frozen("#FFF5F6F8");
    public static readonly Brush DoneStroke = Frozen("#FFCED4DF");

    public static readonly Brush SelectedStroke = Frozen("#FF1D4ED8");
    public static readonly Brush CriticalStroke = Frozen("#FFF97316");
    public static readonly Brush AtRiskBrush = Frozen("#FFEA580C");
    public static readonly Brush OverdueBrush = Frozen("#FFDC2626");
    public static readonly Brush TextBrush = Frozen("#FF1F2430");
    public static readonly Brush SubtleTextBrush = Frozen("#FF7A8194");
    public static readonly Brush DoneTextBrush = Frozen("#FF9AA2B1");

    public static readonly Brush StartAccent = Frozen("#FF8B5CF6");
    public static readonly Brush StepAccent = Frozen("#FFCBD5E1");
    public static readonly Brush MilestoneAccent = Frozen("#FF0EA5E9");
    public static readonly Brush GoalAccent = Frozen("#FFF59E0B");

    public static Brush AccentOf(NodeKind kind) => kind switch
    {
        NodeKind.Start => StartAccent,
        NodeKind.Milestone => MilestoneAccent,
        NodeKind.Goal => GoalAccent,
        _ => StepAccent,
    };

    public static Brush FillOf(Readiness readiness) => readiness switch
    {
        Readiness.Ready => ReadyFill,
        Readiness.InProgress => InProgressFill,
        Readiness.Done or Readiness.Cancelled => DoneFill,
        _ => BlockedFill,
    };

    public static Brush StrokeOf(Readiness readiness) => readiness switch
    {
        Readiness.Ready => ReadyStroke,
        Readiness.InProgress => InProgressStroke,
        Readiness.Done or Readiness.Cancelled => DoneStroke,
        _ => BlockedStroke,
    };

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
