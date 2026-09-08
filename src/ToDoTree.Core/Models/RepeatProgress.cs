namespace ToDoTree.Core.Models;

/// <summary>
/// 「指定回数を達成したら完了する」項目の回数。
///
/// ノード種別（出発点・ステップ等）とは独立した属性で、
/// <see cref="TodoNode.Repeat"/> が null なら従来どおりの通常項目。
/// 各回の日時は持たない（<see cref="TodoNode.CompletedAt"/> は項目全体の
/// 最新の完了遷移日時であって、最終回の記録ではない）。
///
/// 不変条件の検証と状態遷移は <see cref="Graph.RepeatService"/> にまとめてある。
/// ここは値の入れ物に徹し、自分では正規化しない
/// （黙って丸めると、不正なファイルを読んだときに気づけなくなる）。
/// </summary>
public sealed class RepeatProgress
{
    /// <summary>下限。1回で終わるなら繰り返しにする意味がない。</summary>
    public const int MinTarget = 2;

    /// <summary>上限。カードのバッジが4桁で収まる範囲。</summary>
    public const int MaxTarget = 9999;

    /// <summary>既定の目標回数。設定画面の初期値にも使う。</summary>
    public const int DefaultTarget = 3;

    /// <summary>目標回数。</summary>
    public int TargetCount { get; set; } = DefaultTarget;

    /// <summary>達成回数。</summary>
    public int CompletedCount { get; set; }

    /// <summary>最後の1回を達成した（＝項目としては完了しているべき）。</summary>
    public bool IsFull => CompletedCount >= TargetCount;

    /// <summary>次の1回で完了する。</summary>
    public bool IsFinalNext => CompletedCount + 1 >= TargetCount;

    public RepeatProgress Clone() => new() { TargetCount = TargetCount, CompletedCount = CompletedCount };

    public override string ToString() => $"{CompletedCount} / {TargetCount} 回";
}
