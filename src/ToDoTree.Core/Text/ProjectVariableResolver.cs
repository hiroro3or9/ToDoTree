using System.Globalization;
using System.Text;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Text;

/// <summary>解析した1区間の種類。</summary>
public enum VariableSegmentKind
{
    /// <summary>ふつうの文字。エスケープを戻したあとの文字列を持つ。</summary>
    Literal,

    /// <summary>定義のある参照。<see cref="VariableSegment.Text"/> は展開後の値。</summary>
    Reference,

    /// <summary>定義の無い参照。<see cref="VariableSegment.Text"/> は原文どおりの <c>{名前}</c>。</summary>
    UndefinedReference,
}

/// <summary>
/// 原文を左から1回走査して切り出した1区間。
/// 原文の位置（<see cref="SourceStart"/>）と表示の位置（<see cref="DisplayStart"/>）を
/// 別に持つので、強調やカーソル合わせを表示側で計算し直さなくてよい。
/// </summary>
/// <param name="Kind">区間の種類。</param>
/// <param name="Text">表示に出す文字列。</param>
/// <param name="Name">参照の変数名。<see cref="VariableSegmentKind.Literal"/> では null。</param>
/// <param name="SourceStart">原文での開始位置。</param>
/// <param name="SourceLength">原文での長さ。</param>
/// <param name="DisplayStart">展開後の文字列での開始位置。</param>
public readonly record struct VariableSegment(
    VariableSegmentKind Kind,
    string Text,
    string? Name,
    int SourceStart,
    int SourceLength,
    int DisplayStart)
{
    public int DisplayLength => Text.Length;
}

/// <summary>原文1つ分の解析結果。</summary>
/// <param name="Source">解析した原文。</param>
/// <param name="Display">展開後の文字列。</param>
/// <param name="Segments">左から並んだ区間。</param>
public sealed record VariableScan(string Source, string Display, IReadOnlyList<VariableSegment> Segments)
{
    /// <summary>原文が展開・エスケープ解除のどちらかで書き換わった。</summary>
    public bool HasSubstitution => !string.Equals(Source, Display, StringComparison.Ordinal);

    /// <summary>定義のある参照を1つ以上含む。</summary>
    public bool HasReference => Segments.Any(s => s.Kind == VariableSegmentKind.Reference);

    /// <summary>定義の無い参照を1つ以上含む。</summary>
    public bool HasUndefined => Segments.Any(s => s.Kind == VariableSegmentKind.UndefinedReference);

    /// <summary>この原文が使っている、定義済みの変数名（出現順・重複なし）。</summary>
    public IReadOnlyList<string> ReferencedNames => NamesOf(VariableSegmentKind.Reference);

    /// <summary>この原文にある、定義の無い変数名（出現順・重複なし）。</summary>
    public IReadOnlyList<string> UndefinedNames => NamesOf(VariableSegmentKind.UndefinedReference);

    private List<string> NamesOf(VariableSegmentKind kind)
    {
        var seen = new List<string>();
        foreach (var segment in Segments)
        {
            if (segment.Kind == kind && segment.Name is { } name && !seen.Contains(name, StringComparer.Ordinal))
            {
                seen.Add(name);
            }
        }

        return seen;
    }
}

/// <summary>
/// <c>{Hoge}</c> を定義値へ置き換える、画面に依存しない処理。
///
/// 走査は左から1回だけ。エスケープ（<c>{{</c> と <c>}}</c>）を先に処理し、
/// 展開した値は二度と走査しない。値の中に <c>{Other}</c> があってもそのまま残る。
/// 「置換した結果がまた置換される」を持ち込むと、値の変更で結果が連鎖して変わり、
/// 使用箇所の集計も改名も当てにならなくなる。
///
/// 名前の比較は <see cref="StringComparer.Ordinal"/>。正規化も空白の除去もしない。
/// </summary>
public sealed class ProjectVariableResolver
{
    /// <summary>変数名の長さの上限。</summary>
    public const int MaxNameLength = 64;

    /// <summary>定義値の長さの上限（UTF-16 コード単位）。</summary>
    public const int MaxValueLength = 4096;

    private readonly Dictionary<string, string> _values;

    private ProjectVariableResolver(Dictionary<string, string> values) => _values = values;

    /// <summary>定義を持たないリゾルバー。参照はすべて未定義になる。</summary>
    public static ProjectVariableResolver Empty { get; } = new([]);

    /// <summary>定義の数。</summary>
    public int Count => _values.Count;

    /// <summary>定義済みの名前（順序は保証しない）。</summary>
    public IReadOnlyCollection<string> Names => _values.Keys;

    /// <summary>
    /// プロジェクトの定義からリゾルバーを作る。
    /// 静的な「いまのプロジェクト」を持たせず、呼び出し側がスナップショットを渡す。
    /// 重複した名前は先に現れたほうを使う（読み込みでは別途拒否する）。
    /// </summary>
    public static ProjectVariableResolver From(TodoProject? project) =>
        From(project?.Variables);

    public static ProjectVariableResolver From(IEnumerable<ProjectVariable>? variables)
    {
        if (variables is null) return Empty;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (variable is null || !IsValidName(variable.Name)) continue;
            values.TryAdd(variable.Name, variable.Value ?? string.Empty);
        }

        return values.Count == 0 ? Empty : new ProjectVariableResolver(values);
    }

    /// <summary>この名前に定義がある。</summary>
    public bool IsDefined(string name) => _values.ContainsKey(name);

    /// <summary>定義値。無ければ null。</summary>
    public string? ValueOf(string name) => _values.TryGetValue(name, out var value) ? value : null;

    /// <summary>表示用の文字列だけを取り出す。原文は変更しない。</summary>
    public string Expand(string? source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        // 波括弧がまったく無ければ、解析も割り当ても要らない。
        // 表示の更新はステップの数だけ走るので、この近道は効く。
        if (source.IndexOf('{') < 0 && source.IndexOf('}') < 0) return source;

        return Scan(source).Display;
    }

    /// <summary>原文を区間へ切り分ける。強調・件数・改名はすべてこの結果を使う。</summary>
    public VariableScan Scan(string? source)
    {
        source ??= string.Empty;
        var segments = new List<VariableSegment>();
        var display = new StringBuilder(source.Length);
        var literal = new StringBuilder();
        var literalSourceStart = 0;
        var literalDisplayStart = 0;

        void FlushLiteral(int sourceEnd)
        {
            if (literal.Length == 0) return;
            var text = literal.ToString();
            segments.Add(new VariableSegment(
                VariableSegmentKind.Literal, text, null,
                literalSourceStart, sourceEnd - literalSourceStart, literalDisplayStart));
            literal.Clear();
        }

        void AppendLiteral(char c, int sourceIndex)
        {
            if (literal.Length == 0)
            {
                literalSourceStart = sourceIndex;
                literalDisplayStart = display.Length;
            }

            literal.Append(c);
            display.Append(c);
        }

        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            // エスケープを先に見る。{{Hoge}} は参照ではなく、文字としての {Hoge}。
            if ((c == '{' || c == '}') && i + 1 < source.Length && source[i + 1] == c)
            {
                AppendLiteral(c, i);
                i += 2;
                continue;
            }

            if (c == '{' && TryReadName(source, i, out var name, out var afterBrace))
            {
                FlushLiteral(i);
                var length = afterBrace - i;
                if (_values.TryGetValue(name, out var value))
                {
                    segments.Add(new VariableSegment(
                        VariableSegmentKind.Reference, value, name, i, length, display.Length));
                    display.Append(value);
                }
                else
                {
                    var raw = source[i..afterBrace];
                    segments.Add(new VariableSegment(
                        VariableSegmentKind.UndefinedReference, raw, name, i, length, display.Length));
                    display.Append(raw);
                }

                i = afterBrace;
                continue;
            }

            // 不完全・不正な記法（`{Hoge` や `{a b}`）は、入力どおりに残す。
            AppendLiteral(c, i);
            i++;
        }

        FlushLiteral(source.Length);
        return new VariableScan(source, display.ToString(), segments);
    }

    /// <summary>
    /// <paramref name="start"/> の <c>{</c> から始まる参照を読む。
    /// 変数名として正しい並びが <c>}</c> で閉じたときだけ true。
    /// </summary>
    private static bool TryReadName(string source, int start, out string name, out int afterBrace)
    {
        name = string.Empty;
        afterBrace = start;

        var i = start + 1;
        if (i >= source.Length || !IsNameStart(source[i])) return false;

        i++;
        while (i < source.Length && IsNamePart(source[i])) i++;

        if (i >= source.Length || source[i] != '}') return false;

        var length = i - start - 1;
        if (length > MaxNameLength) return false;

        name = source[(start + 1)..i];
        afterBrace = i + 1;
        return true;
    }

    /// <summary>変数名として使える並び。日本語の名前もそのまま使える。</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (!IsNameStart(name[0])) return false;

        for (var i = 1; i < name.Length; i++)
        {
            if (!IsNamePart(name[i])) return false;
        }

        return true;
    }

    /// <summary>定義値として使える文字列。改行と空白だけの値は拒否する。</summary>
    public static bool IsValidValue(string? value) =>
        value is { Length: > 0 and <= MaxValueLength }
        && !value.Any(c => c is '\r' or '\n')
        && !string.IsNullOrWhiteSpace(value);

    private static bool IsNameStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsNamePart(char c) =>
        c == '_' || char.IsLetterOrDigit(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>参照として書くときの表記。設定画面のコピー用。</summary>
    public static string Reference(string name) => "{" + name + "}";

    /// <summary>
    /// 形式9以前の原文を形式10へ移す。対象フィールドの波括弧をすべて二重にして、
    /// これまでどおりの見た目を保つ。移行後の文字列をもう一度通しても二重にならないよう、
    /// 呼び出し側は宣言版で1回だけ実行する。
    /// </summary>
    public static string EscapeLegacy(string? source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? string.Empty;
        if (source.IndexOf('{') < 0 && source.IndexOf('}') < 0) return source;

        var builder = new StringBuilder(source.Length + 8);
        foreach (var c in source)
        {
            builder.Append(c);
            if (c is '{' or '}') builder.Append(c);
        }

        return builder.ToString();
    }
}
