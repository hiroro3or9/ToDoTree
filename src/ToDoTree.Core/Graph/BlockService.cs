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
/// ブロックへの接続がある場合、所属変更は依存関係の条件も変える。
/// 解除時はブロック端点を所属ステップへ展開し、着手条件を維持する。
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
        return new BlockHierarchy(project).DirectOwnerOf(nodeId);
    }

    public static bool IsGrouped(TodoProject project, Guid nodeId) => BlockOf(project, nodeId) is not null;

    /// <summary>ブロックに入っているすべてのノード。</summary>
    public static IReadOnlySet<Guid> GroupedNodes(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Blocks.SelectMany(b => b.NodeIds).ToHashSet();
    }

    // ---- 作成 ----

    /// <summary>同じ直接所属先のステップを、その場に作る子ブロックへまとめる。</summary>
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

        var hierarchy = new BlockHierarchy(project);
        var owners = distinct.Select(id => hierarchy.DirectOwnerOf(id)?.Id).Distinct().ToList();
        if (owners.Count != 1)
        {
            return BlockResult.Fail("同じブロック内のステップを選んでください。");
        }

        var parentId = owners[0];
        var candidate = project.DeepClone();
        if (parentId is { } parent)
            Find(candidate, parent)!.NodeIds.RemoveAll(distinct.Contains);
        var block = new TodoBlock { Title = Normalize(title), ParentBlockId = parentId, NodeIds = [.. distinct] };
        candidate.Blocks.Add(block.Clone());
        if (BlockConnections.Validate(candidate) is { } error) return BlockResult.Fail(error);

        if (parentId is { } owner) Find(project, owner)!.NodeIds.RemoveAll(distinct.Contains);
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

        var candidate = project.DeepClone();
        Find(candidate, blockId)!.NodeIds.AddRange(distinct.Where(id => !block.NodeIds.Contains(id)));
        if (BlockConnections.Validate(candidate) is { } error) return BlockResult.Fail(error);

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

        RemoveEmpty(project);
        return removed;
    }

    /// <summary>囲みだけを取り除き、直接の中身を一段上へ戻す。</summary>
    public static bool Dissolve(TodoProject project, Guid blockId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Find(project, blockId) is not { } block) return false;
        var hierarchy = new BlockHierarchy(project);
        BlockConnections.Dissolve(project, block, hierarchy.DescendantNodeIds(blockId));

        foreach (var child in hierarchy.ChildrenOf(blockId)) child.ParentBlockId = block.ParentBlockId;
        if (block.ParentBlockId is { } parentId && Find(project, parentId) is { } parent)
        {
            foreach (var nodeId in block.NodeIds.Where(id => !parent.NodeIds.Contains(id))) parent.NodeIds.Add(nodeId);
        }
        return project.Blocks.Remove(block);
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

        changed |= RemoveEmpty(project) > 0;
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

            if (known.Contains(block.Id) || !blockIds.Add(block.Id))
            {
                return $"ブロックの ID が重複しています（{block.Id}）。";
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

        // 保存順は親子順とは限らないので、IDを集め終えてから親参照を検査する。
        foreach (var block in project.Blocks)
        {
            if (block.ParentBlockId == block.Id)
                return $"ブロック「{block.Title}」が自分自身を親にしています。";
            if (block.ParentBlockId is { } parent && !blockIds.Contains(parent))
                return $"ブロック「{block.Title}」が、存在しない親ブロック（{parent}）を指しています。";
        }

        var byId = project.Blocks.ToDictionary(b => b.Id);
        var done = new HashSet<Guid>();
        foreach (var start in project.Blocks)
        {
            if (done.Contains(start.Id)) continue;
            var path = new HashSet<Guid>();
            var current = start;
            while (true)
            {
                if (!path.Add(current.Id))
                    return $"ブロック「{current.Title}」の親子関係が循環しています。";
                if (current.ParentBlockId is not { } parent || done.Contains(parent)) break;
                current = byId[parent];
            }
            done.UnionWith(path);
        }

        var hierarchy = new BlockHierarchy(project);
        foreach (var block in project.Blocks)
        {
            if (hierarchy.DescendantNodeIds(block.Id).Count == 0)
                return $"子孫にもステップが 1 つも入っていないブロックがあります（{block.Title}）。";
        }

        return null;
    }

    private static int RemoveEmpty(TodoProject project)
    {
        var removed = 0;
        while (true)
        {
            var parents = project.Blocks.Where(b => b.ParentBlockId is not null)
                .Select(b => b.ParentBlockId!.Value).ToHashSet();
            var emptyLeaves = project.Blocks.Where(b => b.NodeIds.Count == 0 && !parents.Contains(b.Id))
                .Select(b => b.Id).ToHashSet();
            if (emptyLeaves.Count == 0) return removed;
            project.Edges.RemoveAll(e => emptyLeaves.Contains(e.FromId) || emptyLeaves.Contains(e.ToId));
            removed += project.Blocks.RemoveAll(b => emptyLeaves.Contains(b.Id));
        }
    }

    /// <summary>ブロック全体を別の親へ移す。null は最上位。</summary>
    public static string? MoveBlock(TodoProject project, Guid blockId, Guid? targetParentId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Find(project, blockId) is not { } block) return "移動するブロックがありません。";
        if (targetParentId is { } target && Find(project, target) is null) return "追加先のブロックがありません。";
        if (blockId == targetParentId) return "ブロックを自分自身の中へ移すことはできません。";
        if (block.ParentBlockId == targetParentId) return null;

        var candidate = project.DeepClone();
        Find(candidate, blockId)!.ParentBlockId = targetParentId;
        if (BlockConnections.Validate(candidate) is { } error) return error;
        block.ParentBlockId = targetParentId;
        return null;
    }

    /// <summary>親から一段だけ外す。最上位は変更しない。</summary>
    public static string? DetachBlock(TodoProject project, Guid blockId)
    {
        if (Find(project, blockId) is not { ParentBlockId: { } parentId } block)
            return Find(project, blockId) is null ? "移動するブロックがありません。" : null;
        return MoveBlock(project, block.Id, Find(project, parentId)?.ParentBlockId);
    }

    /// <summary>選んだブロックだけを包む新しい親を同じ階層に作る。</summary>
    public static BlockResult Wrap(TodoProject project, Guid blockId, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Find(project, blockId) is not { } block) return BlockResult.Fail("包むブロックがありません。");
        var wrapper = new TodoBlock { Title = Normalize(title), ParentBlockId = block.ParentBlockId };
        var candidate = project.DeepClone();
        Find(candidate, blockId)!.ParentBlockId = wrapper.Id;
        candidate.Blocks.Add(wrapper.Clone());
        if (BlockConnections.Validate(candidate) is { } error) return BlockResult.Fail(error);
        block.ParentBlockId = wrapper.Id;
        project.Blocks.Add(wrapper);
        return BlockResult.Ok(wrapper);
    }

    /// <summary>位置や状態を変えず所属を移す。検証に失敗したときは一切変更しない。</summary>
    public static string? Transfer(TodoProject project, IReadOnlyList<Guid> nodeIds, Guid? targetId)
    {
        if (targetId is { } target && Find(project, target) is null) return "追加先のブロックがありません。";
        if (nodeIds.Any(id => !project.Nodes.Any(n => n.Id == id))) return "ステップが見つかりません。";
        var candidate = project.DeepClone();
        foreach (var block in candidate.Blocks) block.NodeIds.RemoveAll(nodeIds.Contains);
        if (targetId is { } destination) Find(candidate, destination)!.NodeIds.AddRange(nodeIds.Distinct());
        RemoveEmpty(candidate);
        if (BlockConnections.Validate(candidate) is { } error) return error;
        foreach (var block in project.Blocks) block.NodeIds.RemoveAll(nodeIds.Contains);
        if (targetId is { } dest) Find(project, dest)!.NodeIds.AddRange(nodeIds.Distinct());
        RemoveEmpty(project);
        return null;
    }

    private static string Normalize(string? title) =>
        string.IsNullOrWhiteSpace(title) ? TodoBlock.DefaultTitle : title.Trim();
}
