using ToDoTree.Core.Models;

namespace ToDoTree.Core.Text;

/// <summary>変数を展開する対象のフィールド。初版はこの3つだけ。</summary>
public enum VariableFieldKind
{
    NodeTitle,
    NodeNotes,
    InboxTitle,
}

/// <summary>
/// 展開対象のフィールド1つ。読み書きを閉じ込めてあるので、
/// 検索・件数・改名・部品の抽出が同じ列挙をたどれる。
/// </summary>
public sealed class VariableField(
    VariableFieldKind kind,
    Guid ownerId,
    Func<string> read,
    Action<string> write,
    Action touch)
{
    public VariableFieldKind Kind { get; } = kind;

    /// <summary>ステップまたは受信箱の項目の ID。</summary>
    public Guid OwnerId { get; } = ownerId;

    public string Value
    {
        get => read() ?? string.Empty;
        set => write(value);
    }

    /// <summary>原文を書き換えたときに <c>UpdatedAt</c> を進める。</summary>
    public void Touch() => touch();

    public string KindLabel => Kind switch
    {
        VariableFieldKind.NodeTitle => "タイトル",
        VariableFieldKind.NodeNotes => "メモ",
        _ => "受信箱",
    };
}

/// <summary>変数1つの使用箇所。同じフィールド内に2回参照があっても1箇所として数える。</summary>
/// <param name="Kind">フィールドの種類。</param>
/// <param name="OwnerId">ステップまたは受信箱の項目の ID。</param>
/// <param name="OwnerLabel">画面に出す持ち主の名前（展開後）。</param>
/// <param name="Source">変更前の原文。</param>
/// <param name="Display">いまの定義での表示。</param>
/// <param name="Occurrences">このフィールド内での出現回数。</param>
public sealed record VariableUsage(
    VariableFieldKind Kind,
    Guid OwnerId,
    string OwnerLabel,
    string Source,
    string Display,
    int Occurrences)
{
    public string KindLabel => Kind switch
    {
        VariableFieldKind.NodeTitle => "タイトル",
        VariableFieldKind.NodeNotes => "メモ",
        _ => "受信箱",
    };
}

/// <summary>改名で書き換わるフィールド1つ分。</summary>
/// <param name="Field">対象のフィールド。</param>
/// <param name="Before">変更前の原文。</param>
/// <param name="After">変更後の原文。</param>
public sealed record VariableRewrite(VariableField Field, string Before, string After);

/// <summary>
/// 変数の対象フィールド・使用箇所・改名・検証・旧形式の移行をまとめる。
/// WPF には依存しない。設定画面と保存経路の両方がここを通る。
/// </summary>
public static class ProjectVariableService
{
    /// <summary>設定画面に出す、置き換える範囲の説明。</summary>
    public const string ScopeDescription =
        "置き換わるのは、ステップのタイトル・メモと、受信箱の項目名だけです。"
        + "プロジェクト名・説明、ブロック名、接続点名、タグ、チェック項目、ブロックの理由、しおりのメモは原文のまま表示します。";

    /// <summary>展開対象のフィールドを、ステップ→受信箱の順に並べる。</summary>
    public static IReadOnlyList<VariableField> TargetFields(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var fields = new List<VariableField>(project.Nodes.Count * 2 + project.Inbox.Count);
        foreach (var node in project.Nodes)
        {
            if (node is null) continue;
            fields.Add(new VariableField(VariableFieldKind.NodeTitle, node.Id,
                () => node.Title, v => node.Title = v, () => node.UpdatedAt = DateTimeOffset.Now));
            fields.Add(new VariableField(VariableFieldKind.NodeNotes, node.Id,
                () => node.Notes, v => node.Notes = v, () => node.UpdatedAt = DateTimeOffset.Now));
        }

        foreach (var item in project.Inbox)
        {
            if (item is null) continue;
            fields.Add(new VariableField(VariableFieldKind.InboxTitle, item.Id,
                () => item.Title, v => item.Title = v, () => { }));
        }

        return fields;
    }

    /// <summary>ある名前を参照しているフィールドを、対象範囲の並び順で返す。</summary>
    public static IReadOnlyList<VariableUsage> FindUsages(
        TodoProject project, ProjectVariableResolver resolver, string name)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var usages = new List<VariableUsage>();
        if (!ProjectVariableResolver.IsValidName(name)) return usages;

        foreach (var field in TargetFields(project))
        {
            var scan = resolver.Scan(field.Value);
            var occurrences = scan.Segments.Count(s =>
                s.Kind is VariableSegmentKind.Reference or VariableSegmentKind.UndefinedReference
                && string.Equals(s.Name, name, StringComparison.Ordinal));

            if (occurrences == 0) continue;
            usages.Add(new VariableUsage(
                field.Kind, field.OwnerId, OwnerLabel(project, resolver, field),
                scan.Source, scan.Display, occurrences));
        }

        return usages;
    }

    /// <summary>定義の無い参照を、名前ごとの使用箇所数とともに返す（出現順）。</summary>
    public static IReadOnlyList<(string Name, int Fields)> FindUndefinedNames(
        TodoProject project, ProjectVariableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var field in TargetFields(project))
        {
            foreach (var name in resolver.Scan(field.Value).UndefinedNames)
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

        return [.. order.Select(n => (n, counts[n]))];
    }

    /// <summary>名前ごとの使用箇所数。定義一覧の表示に使う。</summary>
    public static IReadOnlyDictionary<string, int> CountUsages(
        TodoProject project, ProjectVariableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in TargetFields(project))
        {
            var scan = resolver.Scan(field.Value);
            foreach (var name in scan.ReferencedNames.Concat(scan.UndefinedNames))
            {
                counts[name] = counts.TryGetValue(name, out var current) ? current + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// 変更前の名前から変更後の名前への対応で、対象フィールドの参照を書き換える計画を立てる。
    /// 変更前の原文を1回走査した結果だけを見るので、名前の交換でも連鎖置換しない。
    /// </summary>
    public static IReadOnlyList<VariableRewrite> PlanRename(
        TodoProject project, ProjectVariableResolver resolver, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(renames);

        var rewrites = new List<VariableRewrite>();
        if (renames.Count == 0) return rewrites;

        foreach (var field in TargetFields(project))
        {
            var before = field.Value;
            var after = Rewrite(resolver, before, renames);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                rewrites.Add(new VariableRewrite(field, before, after));
            }
        }

        return rewrites;
    }

    /// <summary>計画した書き換えを適用する。呼び出し側が1つの履歴単位でまとめる。</summary>
    public static void ApplyRename(IEnumerable<VariableRewrite> rewrites)
    {
        ArgumentNullException.ThrowIfNull(rewrites);

        foreach (var rewrite in rewrites)
        {
            rewrite.Field.Value = rewrite.After;
            rewrite.Field.Touch();
        }
    }

    /// <summary>1つの原文の参照名だけを差し替える。<c>string.Replace</c> は使わない。</summary>
    public static string Rewrite(
        ProjectVariableResolver resolver, string? source, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(renames);

        source ??= string.Empty;
        if (renames.Count == 0 || source.IndexOf('{') < 0) return source;

        var scan = resolver.Scan(source);
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (var segment in scan.Segments)
        {
            if (segment.Name is { } name
                && segment.Kind is VariableSegmentKind.Reference or VariableSegmentKind.UndefinedReference
                && renames.TryGetValue(name, out var renamed))
            {
                builder.Append(ProjectVariableResolver.Reference(renamed));
            }
            else
            {
                builder.Append(source.AsSpan(segment.SourceStart, segment.SourceLength));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 保存対象のタイトル・メモが直接参照している定義だけを複製する。
    /// 部品に未使用の定義まで持ち出さない。未定義の参照は原文のまま残る。
    /// </summary>
    public static List<ProjectVariable> CollectUsed(
        IEnumerable<ProjectVariable> definitions, IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(sources);

        var all = definitions.Where(v => v is not null).ToList();
        var resolver = ProjectVariableResolver.From(all);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var name in resolver.Scan(source).ReferencedNames) used.Add(name);
        }

        // 元の並び順を保つ。設定画面で並べ替えた順が部品にも残る。
        return [.. all.Where(v => used.Contains(v.Name)).Select(v => v.Clone())];
    }

    /// <summary>部品を挿入するときに足りない定義。挿入先に同名があればそちらを優先する。</summary>
    public static List<ProjectVariable> MissingDefinitions(
        TodoProject destination, IEnumerable<ProjectVariable> incoming)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(incoming);

        var existing = destination.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        return [.. incoming.Where(v => v is not null && !existing.Contains(v.Name)).Select(v => v.Clone())];
    }

    /// <summary>挿入先と部品で値が食い違う定義。配置の前に見せる。</summary>
    public static List<(string Name, string DestinationValue, string TemplateValue)> ConflictingDefinitions(
        TodoProject destination, IEnumerable<ProjectVariable> incoming)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(incoming);

        var existing = destination.Variables
            .Where(v => v is not null)
            .GroupBy(v => v.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        return [.. incoming
            .Where(v => v is not null
                && existing.TryGetValue(v.Name, out var current)
                && !string.Equals(current, v.Value, StringComparison.Ordinal))
            .Select(v => (v.Name, existing[v.Name], v.Value))];
    }

    /// <summary>定義一覧そのものの検証。読み込み・保存・設定画面で同じものを使う。</summary>
    public static string? Validate(IReadOnlyList<ProjectVariable>? variables)
    {
        if (variables is null) return "プロジェクト変数の一覧が null です。";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (variable is null) return "プロジェクト変数に空の要素があります。";
            if (!ProjectVariableResolver.IsValidName(variable.Name))
            {
                return $"変数名が不正です: 「{variable.Name}」。"
                     + $"文字または _ で始まる 1〜{ProjectVariableResolver.MaxNameLength} 文字にしてください（空白は使えません）。";
            }

            if (!ProjectVariableResolver.IsValidValue(variable.Value))
            {
                return $"変数「{variable.Name}」の値が不正です。"
                     + $"改行を含まない 1〜{ProjectVariableResolver.MaxValueLength} 文字にしてください（空白だけの値は使えません）。";
            }

            if (!seen.Add(variable.Name)) return $"変数名が重複しています: 「{variable.Name}」。";
        }

        return null;
    }

    /// <summary>
    /// 宣言された形式番号との整合を見る。
    /// 形式9以下を名乗るファイルが変数を持っていたら、形式詐称として拒否する。
    /// </summary>
    public static string? ValidateSchema(TodoProject project, int declaredVersion)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (declaredVersion < 10 && project.Variables.Count > 0)
        {
            return $"schemaVersion={declaredVersion} のファイルにプロジェクト変数が入っています。読み込みを中止しました。";
        }

        return Validate(project.Variables);
    }

    /// <summary>
    /// 形式9以前の原文を形式10へ移す。対象フィールドの波括弧を二重にして、
    /// これまでどおりの表示を保つ。すでに形式10のファイルには何もしない。
    /// </summary>
    public static bool MigrateLegacyText(TodoProject project, int declaredVersion)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (declaredVersion >= 10) return false;

        var changed = false;
        foreach (var field in TargetFields(project))
        {
            var escaped = ProjectVariableResolver.EscapeLegacy(field.Value);
            if (string.Equals(escaped, field.Value, StringComparison.Ordinal)) continue;

            // 移行は表示を変えないための書き換えなので、UpdatedAt は動かさない。
            field.Value = escaped;
            changed = true;
        }

        return changed;
    }

    private static string OwnerLabel(TodoProject project, ProjectVariableResolver resolver, VariableField field)
    {
        if (field.Kind == VariableFieldKind.InboxTitle)
        {
            var item = project.Inbox.FirstOrDefault(i => i is not null && i.Id == field.OwnerId);
            return resolver.Expand(item?.Title);
        }

        var node = project.Nodes.FirstOrDefault(n => n is not null && n.Id == field.OwnerId);
        var title = resolver.Expand(node?.Title);
        return string.IsNullOrWhiteSpace(title) ? "(無題)" : title;
    }
}
