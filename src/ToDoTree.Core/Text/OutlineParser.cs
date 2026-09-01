using System.Globalization;
using System.Text.RegularExpressions;

namespace ToDoTree.Core.Text;

/// <summary>
/// 箇条書きのテキストを、そのままステップの並びに読み替える。
/// 頭の中にあるものを 1 行ずつ打ち込まずに、まとめて流し込めるようにするための入口。
/// </summary>
/// <remarks>
/// 書き方:
/// <code>
/// 要件をまとめる
///   画面のラフを描く
///   データ構造を決める @9/10 ~2h #設計
/// </code>
/// インデントで親子、<c>@日付</c> で期限、<c>~2h</c> や <c>~90m</c> で見積り、<c>#タグ</c> でタグ。
/// </remarks>
public static partial class OutlineParser
{
    private const int TabWidth = 4;

    // 「・」は空白なしで書かれることが多いので、そこだけ空白を任意にする。
    [GeneratedRegex(@"^\s*(?:[-*+]\s+|[・•]\s*|\d+[.)]\s+)")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"(?:^|\s)#([^\s#]+)")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"(?:^|\s)~(\d+(?:\.\d+)?)\s*(h|時間|m|分)?")]
    private static partial Regex EstimatePattern();

    [GeneratedRegex(@"(?:^|\s)@(\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}/\d{1,2})")]
    private static partial Regex DuePattern();

    // 記号を取り除いた跡の空白を 1 つに詰める。
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ExtraSpacePattern();

    public static IReadOnlyList<OutlineItem> Parse(string? text, DateTimeOffset? today = null)
    {
        var result = new List<OutlineItem>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var now = today ?? DateTimeOffset.Now;

        // 実際に現れたインデント幅を並べて、その順位を深さとして扱う。
        // これで「スペース 2 個」でも「タブ 1 つ」でも同じように読める。
        var indentStack = new List<int>();

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var indent = MeasureIndent(rawLine);
            var line = BulletPattern().Replace(rawLine, string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var tags = new List<string>();
            line = TagPattern().Replace(line, match =>
            {
                tags.Add(match.Groups[1].Value);
                return " ";
            });

            int? estimate = null;
            line = EstimatePattern().Replace(line, match =>
            {
                estimate ??= ToMinutes(match.Groups[1].Value, match.Groups[2].Value);
                return " ";
            });

            DateTimeOffset? due = null;
            line = DuePattern().Replace(line, match =>
            {
                due ??= ToDate(match.Groups[1].Value, now);
                return " ";
            });

            var title = ExtraSpacePattern().Replace(line, " ").Trim();
            if (title.Length == 0)
            {
                continue;
            }

            result.Add(new OutlineItem(DepthOf(indent, indentStack), title, tags, due, estimate));
        }

        return result;
    }

    private static int MeasureIndent(string line)
    {
        var width = 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                width++;
            }
            else if (c == '\t')
            {
                width += TabWidth;
            }
            else if (c == '　')
            {
                // 全角スペース
                width += 2;
            }
            else
            {
                break;
            }
        }

        return width;
    }

    private static int DepthOf(int indent, List<int> indentStack)
    {
        while (indentStack.Count > 0 && indent < indentStack[^1])
        {
            indentStack.RemoveAt(indentStack.Count - 1);
        }

        if (indentStack.Count == 0)
        {
            indentStack.Add(indent);
            return 0;
        }

        if (indent > indentStack[^1])
        {
            indentStack.Add(indent);
            return indentStack.Count - 1;
        }

        // 同じ幅 = 同じ深さ
        return indentStack.Count - 1;
    }

    private static int ToMinutes(string number, string unit)
    {
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var minutes = unit is "h" or "時間" ? value * 60 : value;
        return (int)Math.Round(Math.Clamp(minutes, 0, 60 * 24 * 365));
    }

    private static DateTimeOffset? ToDate(string token, DateTimeOffset now)
    {
        var parts = token.Split('-', '/');

        try
        {
            if (parts.Length == 3)
            {
                return new DateTimeOffset(
                    new DateTime(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 0, 0, 0, DateTimeKind.Unspecified),
                    now.Offset);
            }

            if (parts.Length == 2)
            {
                var month = int.Parse(parts[0]);
                var day = int.Parse(parts[1]);
                var candidate = new DateTimeOffset(
                    new DateTime(now.Year, month, day, 0, 0, 0, DateTimeKind.Unspecified),
                    now.Offset);

                // 「1/5」を 12 月に書いたら、ふつうは来年のこと。
                return candidate < now.AddDays(-30) ? candidate.AddYears(1) : candidate;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }

        return null;
    }
}
