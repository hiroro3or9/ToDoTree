using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>操作の前後で見える、回数と状態のひとまとまり。</summary>
/// <param name="Completed">達成回数。通常項目では 0。</param>
/// <param name="Target">目標回数。通常項目では 0。</param>
/// <param name="Status">そのときの状態。</param>
public readonly record struct RepeatState(int Completed, int Target, NodeStatus Status)
{
    public bool IsRepeating => Target > 0;

    public static RepeatState Of(TodoNode node) =>
        new(node.Repeat?.CompletedCount ?? 0, node.Repeat?.TargetCount ?? 0, node.Status);

    public override string ToString() => IsRepeating ? $"{Completed} / {Target} 回" : "回数なし";
}

/// <summary>
/// 回数の操作の結果。呼び出し側は「実際に変わったか」「完了に移ったか」で
/// 履歴・演出・案内を出し分ける。
/// </summary>
/// <param name="Applied">モデルを変更した。false なら履歴も積まない。</param>
/// <param name="Error">拒否した理由。null なら「変更の必要がなかった」だけ。</param>
public readonly record struct RepeatResult(bool Applied, string? Error, RepeatState Before, RepeatState After)
{
    /// <summary>この操作で新しく完了へ移った（完了予告・演出の対象）。</summary>
    public bool BecameDone => Before.Status != NodeStatus.Done && After.Status == NodeStatus.Done;

    /// <summary>この操作で完了から外れた（後続がふたたび待ちになりうる）。</summary>
    public bool LeftDone => Before.Status == NodeStatus.Done && After.Status != NodeStatus.Done;

    public bool IsRejected => Error is not null;

    internal static RepeatResult Reject(TodoNode node, string error)
    {
        var state = RepeatState.Of(node);
        return new(false, error, state, state);
    }

    internal static RepeatResult Unchanged(TodoNode node)
    {
        var state = RepeatState.Of(node);
        return new(false, null, state, state);
    }
}

/// <summary>
/// 「指定回数の達成で完了する項目」の検証と状態遷移。
///
/// 回数と状態と完了日時は必ずここで一緒に動かす。個別に書き換える経路を残すと、
/// 「3 / 3 回なのに進行中」「完了なのに完了日時が無い」が作れてしまう。
///
/// 時刻は引数で受け取る。ひとつの操作の UpdatedAt と CompletedAt を揃えられ、
/// テストからも固定した時刻で確かめられる。
/// 無効な操作は変更前に拒否するので、モデル・履歴・未保存状態は動かない。
///
/// 先行の未完了は記録の禁止条件にしない（手動の状態変更と同じ扱い）。
/// 「先行に未完了あり」の表示だけで伝える。
/// </summary>
public static class RepeatService
{
    /// <summary>この項目は回数で完了する。</summary>
    public static bool IsRepeating(TodoNode? node) => node?.Repeat is not null;

    /// <summary>いま「1回達成」を受け付けられる。</summary>
    public static bool CanAdvance(TodoNode? node) =>
        node?.Repeat is { } repeat && node.Status != NodeStatus.Cancelled && repeat.CompletedCount < repeat.TargetCount;

    /// <summary>いま「1回戻す」を受け付けられる。</summary>
    public static bool CanStepBack(TodoNode? node) =>
        node?.Repeat is { } repeat && node.Status != NodeStatus.Cancelled && repeat.CompletedCount > 0;

    /// <summary>次の1回で完了する（完了予告・演出を出す回）。</summary>
    public static bool IsFinalNext(TodoNode? node) =>
        node?.Repeat is { } repeat && node.Status != NodeStatus.Cancelled && repeat.IsFinalNext && !repeat.IsFull;

    /// <summary>カードのバッジや書き出しに添える「2 / 3 回」。通常項目では空。</summary>
    public static string Describe(TodoNode? node) =>
        node?.Repeat is { } repeat ? $"{repeat.CompletedCount} / {repeat.TargetCount} 回" : string.Empty;

    // ---- 状態遷移 ----

    /// <summary>
    /// 設定・訂正の入力そのものが使えるか。使えない理由があれば返す。
    /// 設定画面は入力のたびにこれを見て「適用」の可否を決める。
    /// </summary>
    public static string? ValidateInput(int target, int completed)
    {
        if (target < RepeatProgress.MinTarget || target > RepeatProgress.MaxTarget)
        {
            return $"目標回数は {RepeatProgress.MinTarget}〜{RepeatProgress.MaxTarget} の範囲で指定してください。";
        }

        if (completed < 0) return "達成回数は 0 以上にしてください。";
        if (completed > target) return "達成回数は目標回数より大きくできません。";
        return null;
    }

    /// <summary>
    /// 設定・訂正を適用したあとの状態。モデルは変えないので、設定画面の予告に使える。
    /// <see cref="Configure"/> と同じ判断をここに一本化してある。
    /// </summary>
    public static NodeStatus PreviewStatus(NodeStatus current, int target, int completed) =>
        // 取り消し中の設定変更は取り消しのまま。回数だけを直せるようにする。
        current == NodeStatus.Cancelled ? NodeStatus.Cancelled
        : completed >= target ? NodeStatus.Done
        : completed > 0 ? NodeStatus.InProgress
        : current == NodeStatus.InProgress ? NodeStatus.InProgress
        : NodeStatus.NotStarted;

    /// <summary>繰り返しを設定する・目標と達成回数を訂正する。</summary>
    public static RepeatResult Configure(TodoNode node, int target, int completed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (ValidateInput(target, completed) is { } invalid) return RepeatResult.Reject(node, invalid);

        var before = RepeatState.Of(node);
        var status = PreviewStatus(node.Status, target, completed);

        if (before.IsRepeating && before.Completed == completed && before.Target == target && before.Status == status)
        {
            return RepeatResult.Unchanged(node);
        }

        return Commit(node, before, new RepeatProgress { TargetCount = target, CompletedCount = completed }, status, now);
    }

    /// <summary>繰り返しを解除する。いまの状態はそのまま通常項目へ引き継ぐ。</summary>
    public static RepeatResult Clear(TodoNode node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is null) return RepeatResult.Unchanged(node);

        var before = RepeatState.Of(node);
        node.Repeat = null;
        node.UpdatedAt = now;
        return new RepeatResult(true, null, before, RepeatState.Of(node));
    }

    /// <summary>1回達成する。最後の1回でだけ完了へ移る。</summary>
    public static RepeatResult Advance(TodoNode node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is not { } repeat) return RepeatResult.Reject(node, "繰り返しが設定されていません。");
        if (node.Status == NodeStatus.Cancelled)
        {
            return RepeatResult.Reject(node, "取り消し中は回数を増やせません。再開してから記録してください。");
        }

        // 上限での再操作は「何も起きない」。履歴も積まない。
        if (repeat.CompletedCount >= repeat.TargetCount) return RepeatResult.Unchanged(node);

        var before = RepeatState.Of(node);
        var completed = repeat.CompletedCount + 1;
        var status = completed >= repeat.TargetCount ? NodeStatus.Done : NodeStatus.InProgress;
        return Commit(node, before, new RepeatProgress { TargetCount = repeat.TargetCount, CompletedCount = completed }, status, now);
    }

    /// <summary>1回戻す。0回まで戻すと未着手へ返る。</summary>
    public static RepeatResult StepBack(TodoNode node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is not { } repeat) return RepeatResult.Reject(node, "繰り返しが設定されていません。");
        if (node.Status == NodeStatus.Cancelled)
        {
            return RepeatResult.Reject(node, "取り消し中は回数を戻せません。再開してから訂正してください。");
        }

        if (repeat.CompletedCount <= 0) return RepeatResult.Unchanged(node);

        var before = RepeatState.Of(node);
        var completed = repeat.CompletedCount - 1;
        var status = completed == 0 ? NodeStatus.NotStarted : NodeStatus.InProgress;
        return Commit(node, before, new RepeatProgress { TargetCount = repeat.TargetCount, CompletedCount = completed }, status, now);
    }

    /// <summary>着手する。回数は増やさない。</summary>
    public static RepeatResult Begin(TodoNode node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is not { } repeat) return RepeatResult.Reject(node, "繰り返しが設定されていません。");
        if (node.Status == NodeStatus.Cancelled)
        {
            return RepeatResult.Reject(node, "取り消し中です。再開してから着手してください。");
        }

        if (node.Status == NodeStatus.Done)
        {
            return RepeatResult.Reject(node, "すでに完了しています。回数を訂正すると進行中へ戻せます。");
        }

        if (node.Status == NodeStatus.InProgress) return RepeatResult.Unchanged(node);

        var before = RepeatState.Of(node);
        return Commit(node, before, repeat.Clone(), NodeStatus.InProgress, now);
    }

    /// <summary>取り消す。回数は保持したまま、後続の待ちだけ解消する。</summary>
    public static RepeatResult Cancel(TodoNode node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is not { } repeat) return RepeatResult.Reject(node, "繰り返しが設定されていません。");
        if (node.Status == NodeStatus.Cancelled) return RepeatResult.Unchanged(node);

        var before = RepeatState.Of(node);
        return Commit(node, before, repeat.Clone(), NodeStatus.Cancelled, now);
    }

    /// <summary>取り消しから再開する。保持していた回数から状態を決め直す。</summary>
    public static RepeatResult Resume(TodoNode node, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is not { } repeat) return RepeatResult.Reject(node, "繰り返しが設定されていません。");
        if (node.Status != NodeStatus.Cancelled) return RepeatResult.Unchanged(node);

        var before = RepeatState.Of(node);
        var status = repeat.CompletedCount >= repeat.TargetCount ? NodeStatus.Done
            : repeat.CompletedCount > 0 ? NodeStatus.InProgress
            : NodeStatus.NotStarted;

        // 完了になる場合の完了日時は再開時刻。取り消し前の日時は消えている。
        return Commit(node, before, repeat.Clone(), status, now);
    }

    private static RepeatResult Commit(TodoNode node, RepeatState before, RepeatProgress? repeat, NodeStatus status, DateTimeOffset now)
    {
        node.Repeat = repeat;
        node.Status = status;

        if (status == NodeStatus.Done)
        {
            // 完了のまま目標だけ直したときは、もとの完了日時を保つ。
            // 新しく完了になったとき（と、日時が欠けているとき）だけ打ち直す。
            if (before.Status != NodeStatus.Done || node.CompletedAt is null) node.CompletedAt = now;
        }
        else
        {
            node.CompletedAt = null;
        }

        node.UpdatedAt = now;
        return new RepeatResult(true, null, before, RepeatState.Of(node));
    }

    // ---- 検証 ----

    /// <summary>1 項目の不変条件。壊れていれば理由を返す。</summary>
    public static string? Validate(TodoNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Repeat is not { } repeat) return null;

        var name = string.IsNullOrWhiteSpace(node.Title) ? "(無題)" : node.Title;

        if (repeat.TargetCount < RepeatProgress.MinTarget || repeat.TargetCount > RepeatProgress.MaxTarget)
        {
            return $"「{name}」の目標回数 {repeat.TargetCount} が {RepeatProgress.MinTarget}〜{RepeatProgress.MaxTarget} の範囲外です。";
        }

        if (repeat.CompletedCount < 0 || repeat.CompletedCount > repeat.TargetCount)
        {
            return $"「{name}」の達成回数 {repeat.CompletedCount} が 0〜{repeat.TargetCount} の範囲外です。";
        }

        // 取り消しは回数を保持したまま後続の待ちを解消する。回数と状態の一致は求めない。
        if (node.Status != NodeStatus.Cancelled)
        {
            var full = repeat.CompletedCount == repeat.TargetCount;
            if (full != (node.Status == NodeStatus.Done))
            {
                return $"「{name}」は {repeat.CompletedCount} / {repeat.TargetCount} 回ですが状態が {node.Status} です。";
            }

            if (repeat.CompletedCount > 0 && node.Status == NodeStatus.NotStarted)
            {
                return $"「{name}」は {repeat.CompletedCount} 回達成していますが未着手になっています。";
            }
        }

        if (node.Status == NodeStatus.Done && node.CompletedAt is null)
        {
            return $"「{name}」は完了していますが完了日時がありません。";
        }

        if (node.Status != NodeStatus.Done && node.CompletedAt is not null)
        {
            return $"「{name}」は完了していませんが完了日時が残っています。";
        }

        return null;
    }

    /// <summary>プロジェクト全体の検証。最初に見つかった問題を返す。</summary>
    public static string? Validate(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var node in project.Nodes)
        {
            if (node is null) continue;
            if (Validate(node) is { } error) return error;
        }

        return null;
    }

    /// <summary>
    /// 形式番号との整合。旧形式を名乗るファイルに回数が入っていたら、
    /// 解釈せず不整合として扱う（別アプリが書いた可能性を握り潰さない）。
    /// </summary>
    public static string? ValidateSchema(TodoProject project, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (schemaVersion < FirstSchemaVersion && project.Nodes.Any(n => n?.Repeat is not null))
        {
            return $"schemaVersion={schemaVersion} のファイルに繰り返しの回数が入っています。";
        }

        return Validate(project);
    }

    /// <summary>繰り返しを保存できるようになった形式番号。</summary>
    public const int FirstSchemaVersion = 6;
}
