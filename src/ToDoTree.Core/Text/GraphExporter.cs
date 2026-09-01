using System.Text;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Text;

/// <summary>
/// グラフを、他の場所に貼れる形にして出す。
/// Mermaid で書き出せば GitHub や Notion にそのまま図として貼れる。
/// </summary>
public static class GraphExporter
{
    public static string ToMermaid(TodoProject project, bool horizontal = true)
    {
        ArgumentNullException.ThrowIfNull(project);

        var graph = new TodoGraph(project);
        var ids = new Dictionary<Guid, string>();
        var index = 1;

        foreach (var node in project.Nodes)
        {
            ids[node.Id] = "n" + index++;
        }

        var builder = new StringBuilder();
        builder.AppendLine(horizontal ? "graph LR" : "graph TD");

        foreach (var node in project.Nodes)
        {
            var (open, close) = node.Kind switch
            {
                NodeKind.Start or NodeKind.Goal => ("([\"", "\"])"),
                NodeKind.Milestone => ("{{\"", "\"}}"),
                _ => ("[\"", "\"]"),
            };

            builder.Append("    ").Append(ids[node.Id]).Append(open).Append(Escape(node.Title)).AppendLine(close);
        }

        foreach (var edge in project.Edges)
        {
            if (!ids.TryGetValue(edge.FromId, out var from) || !ids.TryGetValue(edge.ToId, out var to))
            {
                continue;
            }

            builder.Append("    ").Append(from);
            builder.Append(string.IsNullOrWhiteSpace(edge.Label) ? " --> " : $" -->|{Escape(edge.Label!)}| ");
            builder.AppendLine(to);
        }

        AppendClasses(builder, graph, ids);
        return builder.ToString();
    }

    public static string ToMarkdown(TodoProject project, DateTimeOffset? now = null, bool includeDiagram = true, int recentDays = 7)
    {
        ArgumentNullException.ThrowIfNull(project);

        var today = now ?? DateTimeOffset.Now;
        var graph = new TodoGraph(project);
        var progress = graph.Progress();
        var builder = new StringBuilder();

        builder.Append("# ").AppendLine(project.Name);
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(project.Description))
        {
            builder.AppendLine(project.Description);
            builder.AppendLine();
        }

        builder.AppendLine(
            $"**進捗** {progress.Done} / {progress.Total}（{progress.Percent:0}%）" +
            $" ・ 着手できる {progress.Ready} 件 ・ 進行中 {progress.InProgress} 件 ・ 待ち {progress.Blocked} 件");
        builder.AppendLine();

        var suggestions = NextActionPlanner.Suggest(graph, count: 3, now: today);
        if (suggestions.Count > 0)
        {
            builder.AppendLine("## 次にやること");
            builder.AppendLine();
            var rank = 1;
            foreach (var action in suggestions)
            {
                builder.AppendLine($"{rank++}. **{action.Node.Title}** — {action.Reason}");
            }

            builder.AppendLine();
        }

        if (includeDiagram && project.Nodes.Count > 0)
        {
            builder.AppendLine("## 全体図");
            builder.AppendLine();
            builder.AppendLine("```mermaid");
            builder.Append(ToMermaid(project));
            builder.AppendLine("```");
            builder.AppendLine();
        }

        var recent = project.Nodes
            .Where(n => n.Status == NodeStatus.Done && n.CompletedAt is { } at && (today - at).TotalDays <= recentDays)
            .OrderByDescending(n => n.CompletedAt)
            .ToList();

        if (recent.Count > 0)
        {
            builder.AppendLine($"## 直近 {recentDays} 日で終えたこと");
            builder.AppendLine();
            foreach (var node in recent)
            {
                builder.AppendLine($"- {node.Title}（{node.CompletedAt!.Value.LocalDateTime:M/d}）");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## ステップ");
        builder.AppendLine();

        var order = graph.TopologicalOrder() ?? [.. project.Nodes];
        foreach (var node in order)
        {
            var box = node.Status == NodeStatus.Done ? "[x]" : "[ ]";
            var title = node.Status == NodeStatus.Cancelled ? $"~~{node.Title}~~" : $"**{node.Title}**";
            builder.Append($"- {box} {title}");

            var notes = new List<string> { Labels(graph, node) };

            var parents = graph.ParentsOf(node.Id).Select(p => p.Title).ToList();
            if (parents.Count > 0)
            {
                notes.Add("先行: " + string.Join(", ", parents));
            }

            if (node.Due is { } due)
            {
                notes.Add($"期限 {due.LocalDateTime:M/d}");
            }

            if (node.EstimateMinutes is { } minutes && minutes > 0)
            {
                notes.Add(minutes >= 60 ? $"{minutes / 60d:0.#}h" : $"{minutes}分");
            }

            if (node.Tags.Count > 0)
            {
                notes.Add("#" + string.Join(" #", node.Tags));
            }

            builder.AppendLine(" — " + string.Join(" / ", notes));
        }

        return builder.ToString();
    }

    private static string Labels(TodoGraph graph, TodoNode node) => graph.ReadinessOf(node) switch
    {
        Readiness.Ready => "着手できる",
        Readiness.InProgress => "進行中",
        Readiness.Blocked => "待ち",
        Readiness.Done => "完了",
        _ => "取り消し",
    };

    private static void AppendClasses(StringBuilder builder, TodoGraph graph, Dictionary<Guid, string> ids)
    {
        var groups = new Dictionary<string, List<string>>
        {
            ["done"] = [],
            ["doing"] = [],
            ["ready"] = [],
            ["blocked"] = [],
        };

        foreach (var node in graph.Nodes)
        {
            var key = graph.ReadinessOf(node) switch
            {
                Readiness.Done or Readiness.Cancelled => "done",
                Readiness.InProgress => "doing",
                Readiness.Ready => "ready",
                _ => "blocked",
            };

            groups[key].Add(ids[node.Id]);
        }

        builder.AppendLine();
        builder.AppendLine("    classDef done fill:#f1f5f9,stroke:#cbd5e1,color:#94a3b8;");
        builder.AppendLine("    classDef doing fill:#eff6ff,stroke:#3b82f6;");
        builder.AppendLine("    classDef ready fill:#f0fdf4,stroke:#22c55e;");
        builder.AppendLine("    classDef blocked fill:#ffffff,stroke:#d9dee8;");

        foreach (var (key, members) in groups)
        {
            if (members.Count > 0)
            {
                builder.AppendLine($"    class {string.Join(",", members)} {key};");
            }
        }
    }

    /// <summary>Mermaid のラベルで意味を持つ文字を逃がす。</summary>
    private static string Escape(string text) => text
        .Replace("#", "#35;", StringComparison.Ordinal)
        .Replace("\"", "#quot;", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", "<br/>", StringComparison.Ordinal);
}
