using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>「次にやること」の候補と、そう考えた理由。</summary>
public sealed record NextAction(TodoNode Node, double Score, string Reason);

/// <summary>
/// 着手できるステップが複数あるとき、どれから手を付けるべきかを構造から決める。
/// 依存関係を持っているからこそ計算できる部分（詰まりの解消度・最長経路）を重く見る。
/// </summary>
public static class NextActionPlanner
{
    public static IReadOnlyList<NextAction> Suggest(TodoGraph graph, int count = 3, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var today = now ?? DateTimeOffset.Now;
        var criticalPath = graph.CriticalPath().Select(n => n.Id).ToHashSet();

        var candidates = graph.Nodes
            .Where(n =>
            {
                var readiness = graph.ReadinessOf(n);
                return readiness is Readiness.Ready or Readiness.InProgress;
            })
            .Select(node => Evaluate(graph, node, criticalPath, today))
            .OrderByDescending(a => a.Score)
            .ThenBy(a => a.Node.Title, StringComparer.CurrentCulture)
            .ToList();

        return count <= 0 ? candidates : [.. candidates.Take(count)];
    }

    private static NextAction Evaluate(TodoGraph graph, TodoNode node, HashSet<Guid> criticalPath, DateTimeOffset now)
    {
        var score = 0d;
        var reasons = new List<(int Weight, string Text)>();

        if (criticalPath.Contains(node.Id))
        {
            score += 50;
            reasons.Add((50, "最長経路の上にある"));
        }

        if (node.Due is { } due)
        {
            var days = (due.Date - now.Date).TotalDays;
            if (days < 0)
            {
                score += 60;
                reasons.Add((60, $"期限を {Math.Abs((int)days)} 日過ぎている"));
            }
            else if (days <= 3)
            {
                score += 40;
                reasons.Add((40, days < 1 ? "今日が期限" : $"あと {(int)days} 日で期限"));
            }
            else if (days <= 7)
            {
                score += 20;
                reasons.Add((20, "期限が近い"));
            }
        }

        var unlocked = graph.NodesUnlockedBy(node.Id).Count;
        if (unlocked > 0)
        {
            var weight = Math.Min(40, unlocked * 12);
            score += weight;
            reasons.Add((weight, $"終えると {unlocked} つが動き出す"));
        }

        var downstream = graph.Descendants(node.Id).Count;
        if (downstream > 0)
        {
            var weight = Math.Min(20, downstream * 1.5);
            score += weight;
            if (downstream >= 4)
            {
                reasons.Add(((int)weight, $"この先に {downstream} ステップ控えている"));
            }
        }

        if (node.Status == NodeStatus.InProgress)
        {
            score += 15;
            reasons.Add((15, "すでに手を付けている"));
        }

        var estimate = node.EstimateMinutes ?? GraphAnalysis.DefaultEstimateMinutes;
        if (estimate <= 30)
        {
            score += 8;
            reasons.Add((8, "すぐ終わる"));
        }

        var text = reasons.Count == 0
            ? "いま着手できる"
            : string.Join("、", reasons.OrderByDescending(r => r.Weight).Take(2).Select(r => r.Text));

        return new NextAction(node, score, text);
    }
}
