using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

/// <summary>
/// 平坦に保存されたブロック一覧へ、親・子・直接所属の索引を被せる。
/// 構造を保存せず、プロジェクトの構造変更後に作り直して使う。
/// </summary>
public sealed class BlockHierarchy
{
    private readonly Dictionary<Guid, TodoBlock> _blocks;
    private readonly Dictionary<Guid, List<TodoBlock>> _children = [];
    private readonly Dictionary<Guid, TodoBlock> _owners = [];

    public BlockHierarchy(TodoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _blocks = project.Blocks.Where(b => b is not null).GroupBy(b => b.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var block in _blocks.Values)
        {
            _children.TryAdd(block.Id, []);
            if (block.ParentBlockId is { } parent && _blocks.ContainsKey(parent))
                _children[parent].Add(block);
            foreach (var nodeId in block.NodeIds)
                _owners.TryAdd(nodeId, block);
        }
    }

    public TodoBlock? Find(Guid blockId) => _blocks.GetValueOrDefault(blockId);

    public TodoBlock? DirectOwnerOf(Guid nodeId) => _owners.GetValueOrDefault(nodeId);

    public IReadOnlyList<TodoBlock> ChildrenOf(Guid blockId) =>
        _children.TryGetValue(blockId, out var children) ? children : [];

    /// <summary>直接の親から根へ向かう順で祖先を返す。</summary>
    public IReadOnlyList<TodoBlock> AncestorsOf(Guid blockId)
    {
        var result = new List<TodoBlock>();
        var seen = new HashSet<Guid> { blockId };
        var current = Find(blockId);
        while (current?.ParentBlockId is { } parentId && _blocks.TryGetValue(parentId, out current))
        {
            if (!seen.Add(parentId)) break;
            result.Add(current);
        }
        return result;
    }

    /// <summary>子から孫へ、プロジェクト内の順序を保って返す。</summary>
    public IReadOnlyList<TodoBlock> DescendantsOf(Guid blockId)
    {
        var result = new List<TodoBlock>();
        var seen = new HashSet<Guid> { blockId };
        var stack = new Stack<IEnumerator<TodoBlock>>();
        stack.Push(ChildrenOf(blockId).GetEnumerator());
        while (stack.Count > 0)
        {
            var iterator = stack.Peek();
            if (!iterator.MoveNext())
            {
                iterator.Dispose();
                stack.Pop();
                continue;
            }

            var child = iterator.Current;
            if (!seen.Add(child.Id)) continue;
            result.Add(child);
            stack.Push(ChildrenOf(child.Id).GetEnumerator());
        }
        return result;
    }

    public IReadOnlySet<Guid> DescendantNodeIds(Guid blockId)
    {
        if (Find(blockId) is not { } root) return new HashSet<Guid>();
        var result = new HashSet<Guid>(root.NodeIds);
        foreach (var child in DescendantsOf(blockId)) result.UnionWith(child.NodeIds);
        return result;
    }

    public bool IsAncestorOf(Guid ancestorId, Guid blockId) =>
        AncestorsOf(blockId).Any(b => b.Id == ancestorId);

    public int DepthOf(Guid blockId) => AncestorsOf(blockId).Count;

    public string PathOf(Guid blockId)
    {
        if (Find(blockId) is not { } block) return string.Empty;
        var parts = AncestorsOf(blockId).Reverse().Select(b => b.Title).Append(block.Title);
        return string.Join(" / ", parts);
    }
}
