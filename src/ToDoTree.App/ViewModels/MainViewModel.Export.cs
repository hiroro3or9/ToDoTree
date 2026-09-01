using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

/// <summary>先を読む（完了見込み）、外に持ち出す（書き出し）、細かく刻む（分割）。</summary>
public sealed partial class MainViewModel
{
    private string _forecastText = string.Empty;
    private bool _isBehindSchedule;

    public ICommand ExportCommand { get; private set; } = null!;

    public ICommand CopyMermaidCommand { get; private set; } = null!;

    public ICommand SplitCommand { get; private set; } = null!;

    /// <summary>「この調子だといつ終わるか」。</summary>
    public string ForecastText
    {
        get => _forecastText;
        private set => SetProperty(ref _forecastText, value);
    }

    public bool IsBehindSchedule
    {
        get => _isBehindSchedule;
        private set
        {
            if (SetProperty(ref _isBehindSchedule, value))
            {
                OnPropertyChanged(nameof(ForecastBrush));
            }
        }
    }

    public Brush ForecastBrush => IsBehindSchedule ? NodePalette.OverdueBrush : NodePalette.SubtleTextBrush;

    private void UpdateForecast()
    {
        var forecast = ForecastService.Compute(_graph);

        if (!forecast.HasWork)
        {
            ForecastText = Nodes.Count == 0 ? string.Empty : "残っているステップはありません。";
            IsBehindSchedule = false;
            return;
        }

        var remaining = forecast.RemainingMinutes >= 60
            ? $"{forecast.RemainingMinutes / 60d:0.#}h"
            : $"{forecast.RemainingMinutes}分";

        var pace = forecast.PaceFromHistory ? "これまでのペースなら" : "1 日 4 時間なら";
        var text = $"残り {remaining} ・ {pace} {forecast.EstimatedFinish!.Value.LocalDateTime:M/d} 完了見込み";

        if (forecast.SlackDays is { } slack)
        {
            text += slack >= 0 ? $"（期限まで {slack} 日の余裕）" : $"（期限に {-slack} 日足りません）";
        }

        ForecastText = text;
        IsBehindSchedule = forecast.IsLate;
    }

    // ---- 書き出し ----

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "書き出す",
            Filter = "Markdown (*.md)|*.md|Mermaid (*.mmd)|*.mmd",
            FileName = SanitizeFileName(_project.Name) + ".md",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var asMermaid = Path.GetExtension(dialog.FileName).Equals(".mmd", StringComparison.OrdinalIgnoreCase);
            var text = asMermaid
                ? GraphExporter.ToMermaid(_project, Direction == LayoutDirection.LeftToRight)
                : GraphExporter.ToMarkdown(_project);

            File.WriteAllText(dialog.FileName, text);
            StatusMessage = $"{Path.GetFileName(dialog.FileName)} に書き出しました。";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"書き出せませんでした。\n\n{ex.Message}", "ToDoTree", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyMermaid()
    {
        try
        {
            Clipboard.SetText(GraphExporter.ToMermaid(_project, Direction == LayoutDirection.LeftToRight));
            StatusMessage = "Mermaid をコピーしました。GitHub や Notion にそのまま貼れます。";
        }
        catch
        {
            StatusMessage = "コピーできませんでした。";
        }
    }

    // ---- 分割 ----

    private void SplitStep()
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        var dialog = new OutlineInputWindow();
        dialog.Configure(
            "ステップを分割",
            $"「{node.Title}」を細かいステップに割ります",
            "ここに書いたステップがこの下に入り、いまの後続にはそのまま繋がります。インデントで親子になります。",
            "分割する");

        if (Application.Current?.MainWindow is { } owner)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var items = OutlineParser.Parse(dialog.OutlineText);
        if (items.Count == 0)
        {
            StatusMessage = "分割するステップが見つかりませんでした。";
            return;
        }

        PushUndo();
        var created = StepSplitter.Split(_graph, node.Id, items);
        AbsorbCreated(created, selectFirst: false);
        SelectedNode = node;
        StatusMessage = $"{created.Count} 個のステップに割りました。Ctrl+Z で元に戻せます。";
    }
}
