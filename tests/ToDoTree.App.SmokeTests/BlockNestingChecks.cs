using System.IO;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyBlockNesting()
    {
        TodoNode[] nodes =
        [
            new() { Title = "親の直接項目", X = 80, Y = 140 },
            new() { Title = "子の直接項目", X = 390, Y = 210 },
            new() { Title = "孫の項目", X = 700, Y = 280 },
            new() { Title = "外部", X = 1050, Y = 280 },
        ];
        var root = new TodoBlock { Title = "リリース準備", NodeIds = [nodes[0].Id] };
        var child = new TodoBlock { Title = "実装", ParentBlockId = root.Id, NodeIds = [nodes[1].Id] };
        var leaf = new TodoBlock { Title = "確認", ParentBlockId = child.Id, NodeIds = [nodes[2].Id] };
        var project = new TodoProject { Name = "入れ子表示", Nodes = [.. nodes], Blocks = [root, child, leaf] };
        project.Edges.Add(new TodoEdge { FromId = nodes[3].Id, ToId = leaf.Id });
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null,
            Path.Combine(Path.GetTempPath(), $"todotree-nesting-smoke-{Guid.NewGuid():N}"));

        BlockViewModel B(Guid id) => vm.Blocks.Single(b => b.Id == id);
        Check(B(root.Id).TotalCount == 3 && B(child.Id).TotalCount == 2 && B(leaf.Id).TotalCount == 1,
            "Nested block summaries count every descendant step exactly once.");
        Check(B(root.Id).Bounds.Contains(B(child.Id).X, B(child.Id).Y)
              && B(root.Id).Bounds.Contains(B(child.Id).Bounds.Right, B(child.Id).Bounds.Bottom),
            "A parent block contains the complete child block including its header.");
        Check(B(root.Id).ZIndex < B(child.Id).ZIndex && B(child.Id).ZIndex < B(leaf.Id).ZIndex,
            "Nested blocks render from ancestors to descendants.");

        var positions = nodes.Take(3).ToDictionary(n => n.Id, n => (n.X, n.Y));
        vm.SelectBlock(B(root.Id));
        Check(vm.BeginBlockDrag(B(root.Id)), "A fully visible parent block can be dragged.");
        vm.UpdateBlockDrag(25, 35);
        vm.CommitBlockDrag();
        Check(nodes.Take(3).All(n => n.X == positions[n.Id].X + 25 && n.Y == positions[n.Id].Y + 35),
            "Dragging a parent moves every descendant step once.");
        vm.Undo();

        B(child.Id).Model.IsCollapsed = true;
        vm.RefreshAll();
        vm.ToggleBlockCollapse(B(root.Id));
        Check(!B(child.Id).IsVisible && vm.DisplayEndpoint(leaf.Id).Id == root.Id,
            "A collapsed parent hides descendants and receives projected child connections.");
        vm.ToggleBlockCollapse(B(root.Id));
        Check(B(child.Id).IsCollapsed && vm.DisplayEndpoint(leaf.Id).Id == child.Id,
            "Opening a parent restores a child's own collapsed state and projection.");

        vm.ToggleBlockCollapse(B(child.Id));
        vm.SelectBlock(B(leaf.Id));
        vm.MoveSelectedBlockTo(B(root.Id));
        var movedParent = vm.Graph.Project.Blocks.Single(b => b.Id == leaf.Id).ParentBlockId;
        Check(movedParent == root.Id,
            "Moving a block changes only its direct parent.");
        vm.Undo();
        Check(vm.Graph.Project.Blocks.Single(b => b.Id == leaf.Id).ParentBlockId == child.Id,
            "Undo restores a parent-only hierarchy change.");
    }
}
