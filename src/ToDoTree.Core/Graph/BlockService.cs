using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>操作の結果。失敗したときはグラフもブロックも呼ぶ前のままにする。</summary>
public readonly record struct BlockResult(TodoBlock? Block, string? Error)
{
    public bool IsOk => Error is null;

    public static BlockResult Ok(TodoBlock block) => new(block, null);

    public static BlockResult Fail(string message) => new(null, message);
}

/// <summary>
/// ブロック（囲み）の作成・所属更新・解除・検証。
///
/// ここは依存関係に一切触れない。辺もノードも作らず消さないので、
/// 着手可能判定・進捗・最長経路・完了予測はブロック操作の前後で必ず同じになる。
/// </summary>
public static class BlockService
{
    /// <summary>まとめるのに必要な最小件数。</summary>
    public const int MinimumSize = 2;

    public static TodoBlock? Find(TodoProject project, Guid blockId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Blocks.FirstOrDefault(b => b.Id == blockId);
    }

    public static TodoBlock? BlockOf(TodoProject project, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Blocks.FirstOrDefault(b => b.NodeIds.Contains(nodeId));
    }

    public static bool IsGrouped(TodoProject project, Guid nodeId) => BlockOf(project, nodeId) is not null;

    /// <summary>ブロックに入っているすべてのノード。</summary>
    public static IReadOnlySet<Guid> GroupedNodes(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Blocks.SelectMany(b => b.NodeIds).ToHashSet();
    }

    // ---- 作成 ----

    /// <summary>選んだステップを 1 つの囲みにまとめる。所属済みが混ざっていたら何もしない。</summary>
    public static BlockResult Create(TodoProject project, IReadOnlyList<Guid> nodeIds, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(nodeIds);

        var distinct = nodeIds.Distinct().ToList();
        if (distinct.Count < MinimumSize)
        {
            return BlockResult.Fail($"{MinimumSize} 件以上のステップを選んでからまとめてください。");
        }

        var known = project.Nodes.Select(n => n.Id).ToHashSet();
        if (distinct.Any(id => !known.Contains(id)))
        {
            return BlockResult.Fail("見つからないステップが含まれています。");
        }

        if (distinct.Any(id => IsGrouped(project, id)))
        {
            return BlockResult.Fail("すでにブロックに入っているステップが含まれています。所属を外してからまとめてください。");
        }

        var block = new TodoBlock { Title = Normalize(title), NodeIds = [.. distinct] };
        project.Blocks.Add(block);
        return BlockResult.Ok(block);
    }

    // ---- 所属の追加・取り外し ----

    /// <summary>未所属のステップを既存の囲みに入れる。他のブロックに入っているものが混ざったら何もしない。</summary>
    public static BlockResult Add(TodoProject project, Guid blockId, IReadOnlyList<Guid> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(nodeIds);

        if (Find(project, blockId) is not { } block)
        {
            return BlockResult.Fail("そのブロックはもうありません。");
        }

        var distinct = nodeIds.Distinct().ToList();
        var known = project.Nodes.Select(n => n.Id).ToHashSet();
        if (distinct.Any(id => !known.Contains(id)))
        {
            return BlockResult.Fail("見つからないステップが含まれています。");
        }

        // 同じノードが 2 つのブロックに入る道は作らない。すでにこの囲みの中にあるものは素通り。
        if (distinct.Any(id => BlockOf(project, id) is { } other && other.Id != blockId))
        {
            return BlockResult.Fail("別のブロックに入っているステップが含まれています。先に所属を外してください。");
        }

        foreach (var id in distinct.Where(id => !block.NodeIds.Contains(id)))
        {
            block.NodeIds.Add(id);
        }

        return BlockResult.Ok(block);
    }

    /// <summary>
    /// 選んだステップの所属だけを取り除く。位置も接続も変えない。
    /// 0 件になった囲みは、この操作の中で一緒に消す（1 件なら残す）。
    /// </summary>
    public static int Remove(TodoProject project, IReadOnlyList<Guid> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(nodeIds);

        var removed = 0;
        foreach (var id in nodeIds.Distinct())
        {
            foreach (var block in project.Blocks)
            {
                if (block.NodeIds.Remove(id))
                {
                    removed++;
                }
            }
        }

        project.Blocks.RemoveAll(b => b.NodeIds.Count == 0);
        return removed;
    }

    /// <summary>囲みだけを取り除く。中のステップと接続・通過点はそのまま残る。</summary>
    public static bool Dissolve(TodoProject project, Guid blockId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Blocks.RemoveAll(b => b.Id == blockId) > 0;
    }

    /// <summary>
    /// 画面操作の副作用で生じた壊れた所属を整える。
    /// 実在しない ID、同じリスト内の重複、二重所属、0 件の囲みを落とす。変えたら true。
    /// </summary>
    public static bool Prune(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var known = project.Nodes.Select(n => n.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        var changed = false;

        foreach (var block in project.Blocks)
        {
            var kept = new List<Guid>(block.NodeIds.Count);
            var dropped = false;

            foreach (var id in block.NodeIds)
            {
                if (known.Contains(id) && seen.Add(id))
                {
                    kept.Add(id);
                }
                else
                {
                    dropped = true;
                }
            }

            if (dropped)
            {
                block.NodeIds = kept;
                changed = true;
            }

            var normalized = Normalize(block.Title);
            if (block.Title != normalized)
            {
                block.Title = normalized;
                changed = true;
            }
        }

        changed |= project.Blocks.RemoveAll(b => b.NodeIds.Count == 0) > 0;
        return changed;
    }

    // ---- 検証 ----

    /// <summary>問題がなければ null、あれば理由を返す。読み込み時と変更確定前に通す。</summary>
    public static string? Validate(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var known = project.Nodes.Select(n => n.Id).ToHashSet();
        var blockIds = new HashSet<Guid>();
        var owners = new Dictionary<Guid, Guid>();

        foreach (var block in project.Blocks)
        {
            if (block is null)
            {
                return "中身のないブロックが含まれています。";
            }

            if (!blockIds.Add(block.Id))
            {
                return $"ブロックの ID が重複しています（{block.Id}）。";
            }

            if (block.NodeIds.Count == 0)
            {
                return $"ステップが 1 つも入っていないブロックがあります（{block.Title}）。";
            }

            var withinBlock = new HashSet<Guid>();
            foreach (var id in block.NodeIds)
            {
                if (!known.Contains(id))
                {
                    return $"ブロック「{block.Title}」が、存在しないステップ（{id}）を指しています。";
                }

                if (!withinBlock.Add(id))
                {
                    return $"ブロック「{block.Title}」に同じステップが 2 回入っています。";
                }

                if (!owners.TryAdd(id, block.Id))
                {
                    return $"同じステップが複数のブロックに入っています（{id}）。";
                }
            }
        }

        return null;
    }

    private static string Normalize(string? title) =>
        string.IsNullOrWhiteSpace(title) ? TodoBlock.DefaultTitle : title.Trim();
}
