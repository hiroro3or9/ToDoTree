using System.Windows.Media;
using ToDoTree.App.Services;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>
/// 状態がひと目で分かるようにするための色。
/// 実体は Themes/Palette.Light.xaml / Palette.Dark.xaml にあり、いま適用中のテーマから引く。
/// 切り替えたあとは <see cref="MainViewModel.RefreshAll"/> で通知し直せば塗り替わる。
/// </summary>
public static class NodePalette
{
    public static Brush SelectedStroke => ThemeManager.BrushOf("Node.Selected.Stroke");

    public static Brush CriticalStroke => ThemeManager.BrushOf("Node.Critical.Stroke");

    public static Brush AtRiskBrush => ThemeManager.BrushOf("Node.AtRisk");

    public static Brush OverdueBrush => ThemeManager.BrushOf("Node.Overdue");

    public static Brush TextBrush => ThemeManager.BrushOf("Node.Text");

    public static Brush SubtleTextBrush => ThemeManager.BrushOf("Node.TextSubtle");

    public static Brush DoneTextBrush => ThemeManager.BrushOf("Node.TextDone");

    public static Brush ConnectorFill => ThemeManager.BrushOf("Node.Connector.Fill");

    /// <summary>ミニマル表示で、選択中の行に敷く地色。</summary>
    public static Brush RowSelectedFill => ThemeManager.BrushOf("Node.Row.Selected.Fill");

    public static Brush RowSelectedStroke => ThemeManager.BrushOf("Node.Row.Selected.Stroke");

    /// <summary>種別を表すカード左端の色帯。</summary>
    public static Brush AccentOf(NodeKind kind) => kind switch
    {
        NodeKind.Start => ThemeManager.BrushOf("Kind.Start"),
        NodeKind.Milestone => ThemeManager.BrushOf("Kind.Milestone"),
        NodeKind.Goal => ThemeManager.BrushOf("Kind.Goal"),
        _ => ThemeManager.BrushOf("Kind.Step"),
    };

    public static Brush FillOf(Readiness readiness) => readiness switch
    {
        Readiness.Ready => ThemeManager.BrushOf("Node.Ready.Fill"),
        Readiness.InProgress => ThemeManager.BrushOf("Node.Progress.Fill"),
        Readiness.Done or Readiness.Cancelled => ThemeManager.BrushOf("Node.Done.Fill"),
        _ => ThemeManager.BrushOf("Node.Blocked.Fill"),
    };

    public static Brush StrokeOf(Readiness readiness) => readiness switch
    {
        Readiness.Ready => ThemeManager.BrushOf("Node.Ready.Stroke"),
        Readiness.InProgress => ThemeManager.BrushOf("Node.Progress.Stroke"),
        Readiness.Done or Readiness.Cancelled => ThemeManager.BrushOf("Node.Done.Stroke"),
        _ => ThemeManager.BrushOf("Node.Blocked.Stroke"),
    };
}
