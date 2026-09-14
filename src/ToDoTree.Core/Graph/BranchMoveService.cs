using ToDoTree.Core.Models;

namespace ToDoTree.Core.Graph;

public sealed record BranchMoveResult(TodoProject Source, TodoProject Destination, Guid EntranceId, int MovedCount);

/// <summary>枝を独立させるための候補を作る。保存が済むまでは元の文書を変更しない。</summary>
public static class BranchMoveService
{
    /// <summary>下流と所属ブロックを丸ごと含める。ブロック全体への依存を分割しない。</summary>
    public static IReadOnlySet<Guid> Collect(TodoProject source, Guid rootId)
    {
        if (source.DocumentKind != DocumentKind.Todo || source.Procedure is not null)
            throw new InvalidOperationException("枝の引っ越しは通常プロジェクトで使えます。");
        if (!source.Nodes.Any(n => n.Id == rootId))
            throw new InvalidOperationException("移動する枝の起点がありません。");
        if (BlockConnections.Validate(source) is { } error) throw new InvalidDataException(error);
        var graph = new TodoGraph(source);
        var hierarchy = new BlockHierarchy(source);
        var ids = new HashSet<Guid>();
        var expandedBlocks = new HashSet<Guid>();
        var pending = new Queue<Guid>();
        pending.Enqueue(rootId);
        while (pending.TryDequeue(out var id))
        {
            if (!ids.Add(id)) continue;
            foreach (var child in graph.ChildrenOf(id)) pending.Enqueue(child.Id);
            foreach (var child in TaskHierarchy.Children(source, id)) pending.Enqueue(child.Id);
            if (hierarchy.DirectOwnerOf(id) is { } owner)
            {
                var top = hierarchy.AncestorsOf(owner.Id).LastOrDefault() ?? owner;
                if (!expandedBlocks.Add(top.Id)) continue;
                foreach (var member in hierarchy.DescendantNodeIds(top.Id))
                    if (!ids.Contains(member)) pending.Enqueue(member);
            }
        }
        return ids;
    }

    public static BranchMoveResult Create(TodoProject source, Guid rootId, string destinationPath, string name)
    {
        if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("移動先の保存場所とプロジェクト名が必要です。");
        var ids = Collect(source, rootId);
        var blockIds = source.Blocks.Where(b => BlockConnections.Members(source, b.Id).Any(ids.Contains))
            .Select(b => b.Id).ToHashSet();
        var endpoints = ids.Concat(blockIds).ToHashSet();
        var destination = new TodoProject
        {
            Name = name.Trim(),
            Variables = [.. source.Variables.Select(v => v.Clone())],
            Nodes = [.. source.Nodes.Where(n => ids.Contains(n.Id)).Select(n => n.Clone())],
            Blocks = [.. source.Blocks.Where(b => blockIds.Contains(b.Id)).Select(b => b.Clone())],
            Edges = [.. source.Edges.Where(e => endpoints.Contains(e.FromId) && endpoints.Contains(e.ToId)).Select(e => e.Clone())],
            Bookmark = source.Bookmark is { } mark && ids.Contains(mark.NodeId) ? mark.Clone() : null,
        };
        foreach (var node in destination.Nodes)
            if (node.ParentTaskId is { } parent && !ids.Contains(parent)) node.ParentTaskId = null;
        var remaining = source.DeepClone();
        remaining.Nodes.RemoveAll(n => ids.Contains(n.Id));
        remaining.Edges.RemoveAll(e => endpoints.Contains(e.FromId) && endpoints.Contains(e.ToId));
        // 下流の閉包なので外へ出る線はない。残る線のID・端点・接続点・理由をそのまま保つ。
        if (remaining.Edges.Any(e => endpoints.Contains(e.FromId)))
            throw new InvalidOperationException("枝の外への接続を解決できませんでした。");
        var originalNodes = source.Nodes.ToDictionary(n => n.Id);
        var hierarchy = new BlockHierarchy(source);
        var entrances = new HashSet<Guid>();
        TodoNode AddEntrance(Guid target, Guid? blockId = null)
        {
            var original = originalNodes[target];
            var entrance = new TodoNode
            {
                Id = blockId is null ? target : Guid.NewGuid(),
                Title = original.Title, X = original.X, Y = original.Y,
                ParentTaskId = original.ParentTaskId,
                IsPinned = original.IsPinned,
                ProjectLink = new ProjectLink
                {
                    ProjectId = destination.Id, NodeId = target,
                    FilePath = destinationPath, ProjectName = destination.Name,
                },
            };
            remaining.Nodes.Add(entrance);
            entrances.Add(entrance.Id);
            if (blockId is { } block) remaining.Blocks.Single(b => b.Id == block).NodeIds.Add(entrance.Id);
            return entrance;
        }
        // 元の起点を入口に置き換える。複数の外部接続は元の端点ごとに入口を残す。
        AddEntrance(rootId);
        foreach (var target in remaining.Edges.Select(e => e.ToId).Where(ids.Contains).Distinct())
            if (!entrances.Contains(target)) AddEntrance(target);
        foreach (var block in remaining.Blocks.Where(b => blockIds.Contains(b.Id)))
        {
            block.NodeIds.RemoveAll(id => !entrances.Contains(id));
            block.IsCollapsed = false;
        }
        foreach (var blockId in remaining.Edges.Select(e => e.ToId).Where(blockIds.Contains).Distinct())
        {
            if (hierarchy.DescendantNodeIds(blockId).Any(entrances.Contains)) continue;
            var target = hierarchy.DescendantNodeIds(blockId).First();
            AddEntrance(target, blockId);
        }
        // 入口がない囲みだけを落とす。参照されている囲みはポートも含めて残る。
        var remainingHierarchy = new BlockHierarchy(remaining);
        remaining.Blocks.RemoveAll(b => remainingHierarchy.DescendantNodeIds(b.Id).Count == 0);
        if (source.Bookmark is { } bookmark && ids.Contains(bookmark.NodeId))
        {
            remaining.Bookmark = bookmark.Clone();
            remaining.Bookmark.NodeId = rootId;
        }
        if (BlockConnections.Validate(remaining) is { } sourceError) throw new InvalidDataException(sourceError);
        if (BlockConnections.Validate(destination) is { } targetError) throw new InvalidDataException(targetError);
        if (ChoiceService.Validate(remaining, TodoProject.CurrentSchemaVersion) is { } choiceError)
            throw new InvalidDataException(choiceError);
        return new BranchMoveResult(remaining, destination, rootId, ids.Count);
    }

    public static string? ValidateLinks(TodoProject project, int version)
    {
        var links = project.Nodes.Where(n => n.ProjectLink is not null).Select(n => n.ProjectLink!).ToArray();
        if (links.Length > 0 && version < 13) return "枝の引っ越しには形式13以降が必要です。";
        if (links.Any(l => l.ProjectId == Guid.Empty || l.NodeId == Guid.Empty
            || l.ProjectId == project.Id || string.IsNullOrWhiteSpace(l.FilePath)
            || string.IsNullOrWhiteSpace(l.ProjectName)))
            return "枝の入口の移動先が不正です。";
        return null;
    }
}
