using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class BranchTemplateTests
{
    [Test]
    public async Task Capture_PreservesInternalGraphAndResetsWorkState()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        a.X = 100; a.Y = 50; b.X = 400; b.Y = 150;
        a.Status = NodeStatus.Done; a.CompletedAt = DateTimeOffset.Now; a.Due = DateTimeOffset.Now;
        a.IsPinned = true; a.Notes = "再利用するメモ"; a.EstimateMinutes = 90; a.Tags.Add("設計");
        graph.Project.Blocks.Add(new TodoBlock { Title = "工程", NodeIds = [a.Id, b.Id, c.Id] });
        graph.Edges[0].Waypoints.Add(new JunctionPoint(250, 80, true));
        var template = BranchTemplate.Capture(graph.Project, [a.Id, b.Id], " 設計部品 ");
        await Assert.That(template.Name).IsEqualTo("設計部品");
        await Assert.That(template.Nodes.Count).IsEqualTo(2);
        await Assert.That(template.Edges.Count).IsEqualTo(1);
        await Assert.That(template.Blocks[0].NodeIds.Count).IsEqualTo(2);
        await Assert.That(template.Nodes[0].Status).IsEqualTo(NodeStatus.NotStarted);
        await Assert.That(template.Nodes[0].Due is null && template.Nodes[0].CompletedAt is null && !template.Nodes[0].IsPinned).IsTrue();
        await Assert.That(template.Nodes[0].Notes).IsEqualTo(a.Notes);
        await Assert.That(template.Nodes[0].EstimateMinutes).IsEqualTo(90);
        await Assert.That(template.Nodes[0].Tags.SequenceEqual(a.Tags)).IsTrue();
        await Assert.That(template.Edges[0].Waypoints[0]).IsEqualTo(new JunctionPoint(150, 30, true));
        await Assert.That(a.Status).IsEqualTo(NodeStatus.Done);
        await Assert.That(graph.Edges.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Instances_HaveIndependentIdsAndPreserveJoinAndLayout()
    {
        var (graph, a, b, c) = GraphTests.Chain();
        graph.Connect(a.Id, c.Id);
        a.X = 30; a.Y = 10; b.X = 330; b.Y = 60;
        graph.Project.Blocks.Add(new TodoBlock { NodeIds = [a.Id, b.Id] });
        var template = BranchTemplate.Capture(graph.Project, [a.Id, b.Id, c.Id], "合流");
        var first = BranchTemplate.Instantiate(template, 800, 900);
        var second = BranchTemplate.Instantiate(template, 800, 900);
        await Assert.That(first.Nodes.Select(n => n.Id).Intersect(second.Nodes.Select(n => n.Id)).Any()).IsFalse();
        await Assert.That(first.Edges.Select(n => n.Id).Intersect(second.Edges.Select(n => n.Id)).Any()).IsFalse();
        await Assert.That(first.Blocks[0].Id == second.Blocks[0].Id).IsFalse();
        await Assert.That(first.Nodes.Min(n => n.X)).IsEqualTo(800d);
        await Assert.That(first.Nodes.Min(n => n.Y)).IsEqualTo(900d);
        await Assert.That(first.Nodes[1].X - first.Nodes[0].X).IsEqualTo(300d);
        await Assert.That(new TodoGraph(first).ParentsOf(first.Nodes[2].Id).Count()).IsEqualTo(2);
        first.Nodes[0].Tags.Add("変更");
        await Assert.That(second.Nodes[0].Tags.Contains("変更")).IsFalse();
        await Assert.That(template.Nodes[0].Tags.Contains("変更")).IsFalse();
    }

    [Test]
    public async Task InvalidTemplate_IsRejectedBeforeInstantiation()
    {
        var (graph, a, _, c) = GraphTests.Chain();
        graph.Project.Edges.Add(new TodoEdge { FromId = c.Id, ToId = a.Id });
        await Assert.That(() => BranchTemplate.Instantiate(graph.Project, 0, 0)).Throws<InvalidDataException>();
        graph.Project.Edges.RemoveAt(graph.Project.Edges.Count - 1);
        graph.Project.Edges[0].ToId = Guid.NewGuid();
        await Assert.That(() => BranchTemplate.Instantiate(graph.Project, 0, 0)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Store_RoundTripsAndKeepsSameNameTemplatesSeparate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"todotree-templates-{Guid.NewGuid():N}");
        try
        {
            var (graph, _, _, _) = GraphTests.Chain();
            var store = new BranchTemplateStore(directory);
            var a = store.Save(graph.Project, "いつもの流れ");
            var b = store.Save(graph.Project, "いつもの流れ");
            var (loaded, errors) = new BranchTemplateStore(directory).LoadAll();
            await Assert.That(loaded.Count).IsEqualTo(2);
            await Assert.That(errors.Count).IsEqualTo(0);
            await Assert.That(a.Id == b.Id).IsFalse();
            await Assert.That(loaded.All(t => t.Edges.Count == 2)).IsTrue();
            File.WriteAllText(Path.Combine(directory, "broken.template.json"), "{ broken");
            File.WriteAllText(Path.Combine(directory, "null.template.json"), "{\"name\":\"bad\",\"nodes\":[null]}");
            var (Templates, Errors) = store.LoadAll();
            await Assert.That(Templates.Count).IsEqualTo(2);
            await Assert.That(Errors.Count).IsEqualTo(2);
        }
        finally
        {
            // 自分で生成した一意のテストディレクトリ内だけを片付ける。
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}
