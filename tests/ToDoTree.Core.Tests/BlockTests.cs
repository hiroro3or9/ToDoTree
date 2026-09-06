using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

/// <summary>
/// ブロック（囲み）は整理と移動の単位であって、タスクではない。
/// ここでは「まとめても外しても、グラフとしての意味は 1 ミリも変わらない」ことを主に確かめる。
/// </summary>
public class BlockTests
{
    // ---- 依存関係に触れない ----

    [Test]
    [DisplayName("まとめても外しても解除してもグラフは変わらない")]
    public async Task BlockOperations_DoNotTouchGraph()
    {
        var (project, graph, nodes) = Chain(4);
        var before = Snapshot(graph);

        var created = BlockService.Create(project, [nodes[0].Id, nodes[1].Id, nodes[2].Id]);
        await Assert.That(created.IsOk).IsTrue().Because("まとめられる");
        await Assert.That(Snapshot(graph)).IsEqualTo(before).Because("作成でグラフは変わらない");

        BlockService.Add(project, created.Block!.Id, [nodes[3].Id]);
        await Assert.That(Snapshot(graph)).IsEqualTo(before).Because("追加でグラフは変わらない");

        BlockService.Remove(project, [nodes[3].Id]);
        await Assert.That(Snapshot(graph)).IsEqualTo(before).Because("取り外しでグラフは変わらない");

        BlockService.Dissolve(project, created.Block.Id);
        await Assert.That(Snapshot(graph)).IsEqualTo(before).Because("解除でグラフは変わらない");
        await Assert.That(project.Blocks.Count).IsEqualTo(0).Because("囲みだけが消える");
    }

    [Test]
    [DisplayName("解除しても中のステップと通過点は残る")]
    public async Task Dissolve_KeepsNodesAndWaypoints()
    {
        var (project, graph, nodes) = Chain(3);
        project.Edges[0].Waypoints.Add(new JunctionPoint(10, 20));

        var block = BlockService.Create(project, [nodes[0].Id, nodes[1].Id]).Block!;
        BlockService.Dissolve(project, block.Id);

        await Assert.That(graph.NodeCount).IsEqualTo(3).Because("ステップの数");
        await Assert.That(project.Edges.Count).IsEqualTo(2).Because("辺の数");
        await Assert.That(project.Edges[0].Waypoints.Count).IsEqualTo(1).Because("通過点も残る");
    }

    // ---- 作成の条件 ----

    [Test]
    [DisplayName("1 件だけではまとめられない")]
    public async Task Create_NeedsTwoOrMore()
    {
        var (project, _, nodes) = Chain(2);
        var result = BlockService.Create(project, [nodes[0].Id]);

        await Assert.That(result.IsOk).IsFalse().Because("失敗する");
        await Assert.That(project.Blocks.Count).IsEqualTo(0).Because("囲みは作られない");
    }

    [Test]
    [DisplayName("繋がっていないステップ同士でもまとめられる")]
    public async Task Create_AllowsUnconnectedNodes()
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });

        await Assert.That(BlockService.Create(project, [a.Id, b.Id]).IsOk).IsTrue().Because("接続は条件ではない");
    }

    [Test]
    [DisplayName("所属済みが混ざった選択は、まとめも追加も断る")]
    public async Task Create_RejectsAlreadyGrouped_AndLeavesNoPartialChange()
    {
        var (project, _, nodes) = Chain(4);
        var first = BlockService.Create(project, [nodes[0].Id, nodes[1].Id]).Block!;

        var result = BlockService.Create(project, [nodes[1].Id, nodes[2].Id]);
        await Assert.That(result.IsOk).IsFalse().Because("二重所属は作れない");
        await Assert.That(result.Error!.Length > 0).IsTrue().Because("理由が付く");
        await Assert.That(project.Blocks.Count).IsEqualTo(1).Because("失敗しても囲みは増えない");

        var second = BlockService.Create(project, [nodes[2].Id, nodes[3].Id]).Block!;
        var added = BlockService.Add(project, second.Id, [nodes[0].Id]);

        await Assert.That(added.IsOk).IsFalse().Because("他所属のステップは足せない");
        await Assert.That(second.NodeIds.Count).IsEqualTo(2).Because("失敗しても所属は増えない");
        await Assert.That(first.NodeIds.Contains(nodes[0].Id)).IsTrue().Because("元の所属も動かない");
    }

    [Test]
    [DisplayName("同じステップを 2 回足しても 1 件のまま")]
    public async Task Add_IsIdempotent()
    {
        var (project, _, nodes) = Chain(3);
        var block = BlockService.Create(project, [nodes[0].Id, nodes[1].Id]).Block!;

        BlockService.Add(project, block.Id, [nodes[2].Id]);
        BlockService.Add(project, block.Id, [nodes[2].Id]);

        await Assert.That(block.NodeIds.Count).IsEqualTo(3).Because("重複しない");
    }

    // ---- 0 件・1 件 ----

    [Test]
    [DisplayName("残り 1 件でも囲みは残り、0 件になったら消える")]
    public async Task Remove_KeepsSingleton_DropsEmpty()
    {
        var (project, _, nodes) = Chain(3);
        var block = BlockService.Create(project, [nodes[0].Id, nodes[1].Id]).Block!;

        BlockService.Remove(project, [nodes[0].Id]);
        await Assert.That(project.Blocks.Count).IsEqualTo(1).Because("1 件でも囲みは残る");
        await Assert.That(block.NodeIds.Count).IsEqualTo(1).Because("残りの件数");

        BlockService.Remove(project, [nodes[1].Id]);
        await Assert.That(project.Blocks.Count).IsEqualTo(0).Because("0 件の囲みは同じ操作で消える");
    }

    [Test]
    [DisplayName("最後の所属ノードを消すと囲みも消える")]
    public async Task RemovingNodes_DropsEmptyBlock()
    {
        var (project, graph, nodes) = Chain(3);
        BlockService.Create(project, [nodes[0].Id, nodes[1].Id]);

        graph.RemoveNodeAndBridge(nodes[0].Id);
        BlockService.Remove(project, [nodes[0].Id]);
        await Assert.That(project.Blocks.Count).IsEqualTo(1).Because("まだ 1 件残っている");

        graph.RemoveNodeAndBridge(nodes[1].Id);
        BlockService.Remove(project, [nodes[1].Id]);
        await Assert.That(project.Blocks.Count).IsEqualTo(0).Because("空になった囲みは消える");
    }

    [Test]
    [DisplayName("壊れた所属は整えられる")]
    public async Task Prune_CleansBrokenMembership()
    {
        var (project, _, nodes) = Chain(3);
        project.Blocks.Add(new TodoBlock
        {
            Title = "  ",
            NodeIds = [nodes[0].Id, nodes[0].Id, Guid.NewGuid()],
        });
        project.Blocks.Add(new TodoBlock { Title = "空", NodeIds = [] });

        await Assert.That(BlockService.Prune(project)).IsTrue().Because("直すところがあった");
        await Assert.That(project.Blocks.Count).IsEqualTo(1).Because("空の囲みは落とす");
        await Assert.That(project.Blocks[0].NodeIds.Count).IsEqualTo(1).Because("重複と迷子の ID を落とす");
        await Assert.That(project.Blocks[0].Title).IsEqualTo(TodoBlock.DefaultTitle).Because("空白の名前は既定名に戻す");
        await Assert.That(BlockService.Validate(project)).IsNull().Because("整えたあとは正しい");
    }

    // ---- 検証 ----

    [Test]
    [DisplayName("二重所属・迷子の ID・空の囲みは検証で弾かれる")]
    public async Task Validate_RejectsBrokenShapes()
    {
        var (project, _, nodes) = Chain(3);
        BlockService.Create(project, [nodes[0].Id, nodes[1].Id]);
        await Assert.That(BlockService.Validate(project)).IsNull().Because("正しい形");

        project.Blocks.Add(new TodoBlock { Title = "重なり", NodeIds = [nodes[1].Id, nodes[2].Id] });
        await Assert.That(BlockService.Validate(project)).IsNotNull().Because("同じノードが 2 つの囲みに入っている");

        project.Blocks.RemoveAt(1);
        project.Blocks.Add(new TodoBlock { Title = "迷子", NodeIds = [Guid.NewGuid()] });
        await Assert.That(BlockService.Validate(project)).IsNotNull().Because("存在しないステップを指している");

        project.Blocks.RemoveAt(1);
        project.Blocks.Add(new TodoBlock { Title = "空", NodeIds = [] });
        await Assert.That(BlockService.Validate(project)).IsNotNull().Because("0 件の囲み");
    }

    // ---- 境界 ----

    [Test]
    [DisplayName("境界は所属ノードの外接矩形に余白と見出しを足したもの")]
    public async Task Compute_AddsPaddingAndHeader()
    {
        var bounds = BlockGeometry.Compute(
        [
            new NodeRect(Guid.NewGuid(), 100, 200, 224, 88),
            new NodeRect(Guid.NewGuid(), 400, 300, 224, 88),
        ]);

        await Assert.That(bounds.HasValue).IsTrue().Because("囲みが出る");
        var box = bounds!.Value;

        await Assert.That(box.X).IsEqualTo(100 - BlockGeometry.SidePadding).Because("左");
        await Assert.That(box.Y)
            .IsEqualTo(200 - BlockGeometry.HeaderGap - BlockGeometry.HeaderHeight).Because("上（見出しのぶん高い）");
        await Assert.That(box.Right).IsEqualTo(400 + 224 + BlockGeometry.SidePadding).Because("右");
        await Assert.That(box.Bottom).IsEqualTo(300 + 88 + BlockGeometry.BottomPadding).Because("下");
        await Assert.That(box.Header.Height).IsEqualTo(BlockGeometry.HeaderHeight).Because("見出しの高さ");
        await Assert.That(box.Header.Contains(box.X + 10, box.Y + 4)).IsTrue().Because("見出しは上端にある");
    }

    [Test]
    [DisplayName("1 件も見えていなければ囲みは出ない")]
    public async Task Compute_ReturnsNullWhenEmpty()
    {
        await Assert.That(BlockGeometry.Compute([]).HasValue).IsFalse().Because("描くものがない");
    }

    [Test]
    [DisplayName("外へ迂回した通過点で囲みが巨大化しない")]
    public async Task Compute_IgnoresWaypoints()
    {
        // 境界はノードの矩形だけから作る。通過点は Compute に渡す口すら無い。
        var box = BlockGeometry.Compute([new NodeRect(Guid.NewGuid(), 0, 0, 224, 88)])!.Value;
        await Assert.That(box.Width).IsEqualTo(224 + (BlockGeometry.SidePadding * 2)).Because("幅は箱ぶんだけ");
    }

    [Test]
    [DisplayName("通過点を一緒に動かすのは両端とも移動する辺だけ")]
    public async Task InternalEdges_OnlyWhenBothEndsMove()
    {
        var (project, _, nodes) = Chain(3);
        foreach (var edge in project.Edges)
        {
            edge.Waypoints.Add(new JunctionPoint(1, 1));
        }

        var moving = new HashSet<Guid> { nodes[0].Id, nodes[1].Id };
        var internals = BlockGeometry.InternalEdges(project, moving);

        await Assert.That(internals.Count).IsEqualTo(1).Because("0→1 の辺だけ");
        await Assert.That(internals[0].FromId).IsEqualTo(nodes[0].Id).Because("動く側の辺");
    }

    // ---- 保存 ----

    [Test]
    [DisplayName("ブロックごと保存して読み直せる")]
    public async Task SaveThenLoad_RoundTripsBlocks()
    {
        var (project, _, nodes) = Chain(3);
        BlockService.Create(project, [nodes[0].Id, nodes[1].Id], "設計");

        var store = new JsonProjectStore();
        var path = TempPath();

        try
        {
            store.Save(path, project);
            var loaded = store.Load(path);

            await Assert.That(loaded.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion).Because("形式の版");
            await Assert.That(loaded.Blocks.Count).IsEqualTo(1).Because("囲みの数");
            await Assert.That(loaded.Blocks[0].Title).IsEqualTo("設計").Because("名前");
            await Assert.That(loaded.Blocks[0].NodeIds.Count).IsEqualTo(2).Because("所属の件数");
            await Assert.That(loaded.Blocks[0].NodeIds.Contains(nodes[0].Id)).IsTrue().Because("所属の中身");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("旧形式は空のブロックとして読み込み、次の保存で新形式になる")]
    public async Task Load_MigratesOldSchema()
    {
        var path = TempPath();

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "旧形式",
                  "schemaVersion": 1,
                  "nodes": [{ "id": "22222222-2222-2222-2222-222222222222", "title": "A" }],
                  "edges": []
                }
                """);

            var store = new JsonProjectStore();
            var loaded = store.Load(path);

            await Assert.That(loaded.Blocks.Count).IsEqualTo(0).Because("旧形式に囲みは無い");
            await Assert.That(loaded.SchemaVersion)
                .IsEqualTo(TodoProject.CurrentSchemaVersion).Because("読み込み時に版を上げる");

            store.Save(path, loaded);
            await Assert.That(File.ReadAllText(path).Contains("\"schemaVersion\": 2")).IsTrue().Because("新形式で書き出す");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("壊れた所属情報のファイルは読み込みを中止する")]
    public async Task Load_RejectsBrokenBlocks()
    {
        var path = TempPath();

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "壊れている",
                  "schemaVersion": 2,
                  "nodes": [{ "id": "22222222-2222-2222-2222-222222222222", "title": "A" }],
                  "edges": [],
                  "blocks": [
                    { "id": "33333333-3333-3333-3333-333333333333", "title": "迷子",
                      "nodeIds": ["44444444-4444-4444-4444-444444444444"] }
                  ]
                }
                """);

            var store = new JsonProjectStore();
            var failed = false;

            try
            {
                store.Load(path);
            }
            catch (InvalidDataException)
            {
                failed = true;
            }

            await Assert.That(failed).IsTrue().Because("黙って落とさず読み込みを止める");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("複製したブロックは元と切り離されている")]
    public async Task DeepClone_SeparatesBlocks()
    {
        var (project, _, nodes) = Chain(3);
        BlockService.Create(project, [nodes[0].Id, nodes[1].Id], "設計");

        var clone = project.DeepClone();
        clone.Blocks[0].Title = "書き換えた";
        clone.Blocks[0].NodeIds.Add(nodes[2].Id);

        await Assert.That(project.Blocks[0].Title).IsEqualTo("設計").Because("元の名前は変わらない");
        await Assert.That(project.Blocks[0].NodeIds.Count).IsEqualTo(2).Because("元の所属も変わらない");
    }

    // ---- 自動整列 ----

    [Test]
    [DisplayName("自動整列は所属ノードを動かさず、未所属を囲みに重ねない")]
    public async Task Apply_KeepsFixedNodesAndAvoidsBlocks()
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);

        // 2 番目のレイヤの既定の置き場所に、わざと囲みを重ねておく。
        var inside1 = graph.AddNode(new TodoNode { Title = "囲みの中 1", X = 370, Y = 80 });
        var inside2 = graph.AddNode(new TodoNode { Title = "囲みの中 2", X = 370, Y = 200 });
        var free1 = graph.AddNode(new TodoNode { Title = "外 1" });
        var free2 = graph.AddNode(new TodoNode { Title = "外 2" });
        graph.Connect(free1.Id, free2.Id);

        BlockService.Create(project, [inside1.Id, inside2.Id]);

        var rects = new List<NodeRect>
        {
            new(inside1.Id, inside1.X, inside1.Y, 224, 88),
            new(inside2.Id, inside2.X, inside2.Y, 224, 88),
        };

        var area = BlockGeometry.Compute(rects)!.Value;
        var options = NodeStyleMetrics.LayoutFor(NodeStyle.Card, LayoutDirection.LeftToRight);
        options.FixedIds = new HashSet<Guid> { inside1.Id, inside2.Id };
        options.Obstacles = [area];

        LayeredLayoutEngine.Apply(graph, options);

        await Assert.That(inside1.X).IsEqualTo(370).Because("所属ノードの X");
        await Assert.That(inside1.Y).IsEqualTo(80).Because("所属ノードの Y");
        await Assert.That(inside2.Y).IsEqualTo(200).Because("もう 1 件も動かない");
        await Assert.That(inside1.IsPinned).IsFalse().Because("IsPinned は書き換えない");

        foreach (var node in new[] { free1, free2 })
        {
            var box = new BlockBounds(node.X, node.Y, 224, 88);
            await Assert.That(box.IntersectsWith(area)).IsFalse().Because($"{node.Title} が囲みに重ならない");
        }

        await Assert.That(free2.Y >= area.Bottom).IsTrue().Because("重なる位置から下へ押し出されている");
    }

    [Test]
    [DisplayName("囲みが無いときの自動整列は今までどおり")]
    public async Task Apply_UnchangedWithoutBlocks()
    {
        var graph = new TodoGraph(new TodoProject());
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        graph.Connect(a.Id, b.Id);

        var options = NodeStyleMetrics.LayoutFor(NodeStyle.Card, LayoutDirection.LeftToRight);
        LayeredLayoutEngine.Apply(graph, options);

        await Assert.That(a.X).IsEqualTo(options.OriginX).Because("先頭のレイヤ");
        await Assert.That(b.X).IsEqualTo(options.OriginX + options.LayerSpacing).Because("次のレイヤ");
        await Assert.That(a.Y).IsEqualTo(b.Y).Because("1 件ずつのレイヤは同じ高さに並ぶ");
    }

    // ---- 道具 ----

    private static (TodoProject Project, TodoGraph Graph, List<TodoNode> Nodes) Chain(int count)
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);
        var nodes = new List<TodoNode>();

        for (var i = 0; i < count; i++)
        {
            var node = graph.AddNode(new TodoNode { Title = $"S{i}", X = i * 300, Y = 100 });
            nodes.Add(node);

            if (i > 0)
            {
                graph.Connect(nodes[i - 1].Id, node.Id);
            }
        }

        return (project, graph, nodes);
    }

    /// <summary>ノード・辺・着手可能判定をまとめて 1 本の文字列にする（変化していないことの確認用）。</summary>
    private static string Snapshot(TodoGraph graph)
    {
        var nodes = graph.Nodes
            .OrderBy(n => n.Id)
            .Select(n => $"{n.Id}:{graph.ReadinessOf(n)}");

        var edges = graph.Edges
            .Select(e => $"{e.FromId}>{e.ToId}")
            .OrderBy(t => t, StringComparer.Ordinal);

        return string.Join("|", nodes) + "//" + string.Join("|", edges);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"todotree-block-{Guid.NewGuid():N}.json");

    private static void Cleanup(string path)
    {
        foreach (var candidate in new[] { path, path + ".bak", path + ".tmp" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
