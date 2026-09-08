using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

/// <summary>
/// ブロックの中だけを並べ直す機能。
/// ここで確かめたいのは「中は整うが、外と実データは 1 ミリも動かない」こと。
/// </summary>
public class BlockLayoutTests
{
    // ---- 実データに触れない ----

    [Test]
    [DisplayName("候補を計算しただけでは、プロジェクトは何も変わらない")]
    public async Task Compute_DoesNotTouchTheProject()
    {
        var (project, _, nodes, block) = Grouped(4);
        Scatter(nodes);

        var snapshot = Fingerprint(project);
        var result = BlockLayoutService.Compute(project, block.Id, Options());

        await Assert.That(result.IsReady).IsTrue().Because("整列できる");
        await Assert.That(Fingerprint(project)).IsEqualTo(snapshot).Because("実データは呼ぶ前のまま");
        await Assert.That(result.Positions.Count).IsEqualTo(4).Because("全所属ぶんの座標が返る");
    }

    [Test]
    [DisplayName("外のカード・他の囲み・所属・辺・固定フラグは変わらない")]
    public async Task Compute_LeavesEverythingElseAlone()
    {
        var (project, graph, nodes, block) = Grouped(3);
        var outside = graph.AddNode(new TodoNode { Title = "外", X = 2000, Y = 2000, IsPinned = true });
        graph.Connect(nodes[2].Id, outside.Id);
        Scatter(nodes);

        var before = Fingerprint(project);
        BlockLayoutService.Compute(project, block.Id, Options());

        await Assert.That(Fingerprint(project)).IsEqualTo(before).Because("すべて据え置き");
        await Assert.That(outside.IsPinned).IsTrue().Because("固定フラグも触らない");
        await Assert.That(block.NodeIds.Count).IsEqualTo(3).Because("所属も変わらない");
    }

    // ---- 配置で守ること ----

    [Test]
    [DisplayName("囲みの左上（見出しの始点）を保つ")]
    public async Task Compute_KeepsHeaderOrigin()
    {
        var (project, _, nodes, block) = Grouped(4);
        Scatter(nodes);

        var options = Options();
        var beforeBounds = BoundsOf(nodes, options)!.Value;
        var result = BlockLayoutService.Compute(project, block.Id, options);

        await Assert.That(result.IsReady).IsTrue().Because("整列できる");
        await Assert.That(result.Bounds!.Value.X).IsEqualTo(beforeBounds.X).Because("左端");
        await Assert.That(result.Bounds!.Value.Y).IsEqualTo(beforeBounds.Y).Because("上端");
    }

    [Test]
    [DisplayName("負の座標でも左上をそのまま保つ")]
    public async Task Compute_KeepsNegativeOrigin()
    {
        var (project, _, nodes, block) = Grouped(3);
        nodes[0].X = -900; nodes[0].Y = -700;
        nodes[1].X = -400; nodes[1].Y = -300;
        nodes[2].X = -650; nodes[2].Y = -900;

        var options = Options();
        var beforeBounds = BoundsOf(nodes, options)!.Value;
        var result = BlockLayoutService.Compute(project, block.Id, options);

        await Assert.That(result.IsReady).IsTrue().Because("整列できる");
        await Assert.That(result.Bounds!.Value.X).IsEqualTo(beforeBounds.X).Because("左端をゼロへ丸めない");
        await Assert.That(result.Bounds!.Value.Y).IsEqualTo(beforeBounds.Y).Because("上端をゼロへ丸めない");
        await Assert.That(result.Bounds!.Value.X < 0).IsTrue().Because("負のまま");
    }

    [Test]
    [DisplayName("直列・分岐・合流・非連結のどれでも、中のカードは重ならない")]
    public async Task Compute_NeverOverlapsInside()
    {
        foreach (var direction in new[] { LayoutDirection.LeftToRight, LayoutDirection.TopToBottom })
        {
            foreach (var style in new[] { NodeStyle.Card, NodeStyle.Minimal })
            {
                var (project, graph, nodes, block) = Grouped(6, connectChain: false);

                // 分岐と合流と、どこにも繋がらない 1 件。
                graph.Connect(nodes[0].Id, nodes[1].Id);
                graph.Connect(nodes[0].Id, nodes[2].Id);
                graph.Connect(nodes[1].Id, nodes[3].Id);
                graph.Connect(nodes[2].Id, nodes[3].Id);
                graph.Connect(nodes[3].Id, nodes[4].Id);
                Scatter(nodes);

                var options = NodeStyleMetrics.LayoutFor(style, direction);
                var result = BlockLayoutService.Compute(project, block.Id, options);

                await Assert.That(result.IsReady).IsTrue().Because($"{direction} / {style} で整列できる");

                var rects = result.Positions.Values
                    .Select(p => new BlockBounds(p.X, p.Y, options.NodeWidth, options.NodeHeight))
                    .ToList();

                for (var i = 0; i < rects.Count; i++)
                {
                    for (var j = i + 1; j < rects.Count; j++)
                    {
                        await Assert.That(rects[i].IntersectsWith(rects[j])).IsFalse()
                            .Because($"{direction} / {style} で {i} と {j} が重ならない");
                    }
                }
            }
        }
    }

    [Test]
    [DisplayName("外を経由して戻る筋は、中の並びに持ち込まない")]
    public async Task Compute_IgnoresPathsThroughOutside()
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);

        var a = graph.AddNode(new TodoNode { Title = "内 A" });
        var b = graph.AddNode(new TodoNode { Title = "内 B" });
        var x = graph.AddNode(new TodoNode { Title = "外 X", X = 3000, Y = 3000 });
        graph.Connect(a.Id, x.Id);
        graph.Connect(x.Id, b.Id);

        var block = BlockService.Create(project, [a.Id, b.Id]).Block!;
        a.X = 100; a.Y = 100;
        b.X = 500; b.Y = 400;

        var options = Options();
        var result = BlockLayoutService.Compute(project, block.Id, options);

        await Assert.That(result.IsReady).IsTrue().Because("整列できる");

        // 内部に直接の辺は無いので、2 件は同じレイヤ（同じ X）に並ぶ。
        var positions = result.Positions.Values.ToList();
        await Assert.That(positions[0].X).IsEqualTo(positions[1].X).Because("外を通る筋はレイヤを分けない");
    }

    // ---- 安定して同じ結果になる ----

    [Test]
    [DisplayName("配列の並びが変わっても同じ候補になる")]
    public async Task Compute_DoesNotDependOnArrayOrder()
    {
        var (first, _, firstNodes, firstBlock) = Grouped(5);
        Scatter(firstNodes);

        var shuffled = first.DeepClone();
        shuffled.Nodes.Reverse();
        shuffled.Edges.Reverse();
        shuffled.Blocks[0].NodeIds.Reverse();

        var a = BlockLayoutService.Compute(first, firstBlock.Id, Options());
        var b = BlockLayoutService.Compute(shuffled, shuffled.Blocks[0].Id, Options());

        await Assert.That(a.IsReady).IsTrue().Because("整列できる");
        await Assert.That(b.IsReady).IsTrue().Because("並べ替えても整列できる");

        foreach (var (id, position) in a.Positions)
        {
            await Assert.That(b.Positions[id]).IsEqualTo(position).Because($"{id} の位置が一致する");
        }
    }

    [Test]
    [DisplayName("一度整列したあと、もう一度計算すると変化なしになる")]
    public async Task Compute_IsIdempotent()
    {
        var (project, _, nodes, block) = Grouped(5);
        Scatter(nodes);

        var first = BlockLayoutService.Compute(project, block.Id, Options());
        await Assert.That(first.IsReady).IsTrue().Because("1 回目は整列できる");
        Apply(project, first);

        var second = BlockLayoutService.Compute(project, block.Id, Options());
        await Assert.That(second.Status).IsEqualTo(BlockLayoutStatus.Unchanged).Because("2 回目は変化なし");
        await Assert.That(second.Positions.Count).IsEqualTo(0).Because("座標変更を要求しない");
    }

    // ---- 実行できない条件 ----

    [Test]
    [DisplayName("1 件以下では整列しない")]
    public async Task Compute_RejectsTooFewNodes()
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);
        var a = graph.AddNode(new TodoNode { Title = "A" });
        var b = graph.AddNode(new TodoNode { Title = "B" });
        var block = BlockService.Create(project, [a.Id, b.Id]).Block!;

        BlockService.Remove(project, [b.Id]);
        var result = BlockLayoutService.Compute(project, block.Id, Options());

        await Assert.That(result.Status).IsEqualTo(BlockLayoutStatus.TooFewNodes).Because("1 件では並べるものがない");
    }

    [Test]
    [DisplayName("位置を固定したステップがあると整列しない")]
    public async Task Compute_RejectsPinnedNodes()
    {
        var (project, _, nodes, block) = Grouped(3);
        Scatter(nodes);
        nodes[1].IsPinned = true;

        var result = BlockLayoutService.Compute(project, block.Id, Options());

        await Assert.That(result.Status)
            .IsEqualTo(BlockLayoutStatus.ContainsPinnedNodes).Because("固定は黙って外さない");
        await Assert.That(nodes[1].IsPinned).IsTrue().Because("固定フラグはそのまま");
    }

    [Test]
    [DisplayName("知らないブロック ID や壊れた所属は安全に断る")]
    public async Task Compute_RejectsBrokenInput()
    {
        var (project, _, nodes, block) = Grouped(3);
        Scatter(nodes);

        await Assert.That(BlockLayoutService.Compute(project, Guid.NewGuid(), Options()).Status)
            .IsEqualTo(BlockLayoutStatus.InvalidInput).Because("知らないブロック");

        block.NodeIds.Add(Guid.NewGuid());
        await Assert.That(BlockLayoutService.Compute(project, block.Id, Options()).Status)
            .IsEqualTo(BlockLayoutStatus.InvalidInput).Because("存在しないステップを指している");

        block.NodeIds.RemoveAt(block.NodeIds.Count - 1);
        nodes[0].X = double.NaN;
        await Assert.That(BlockLayoutService.Compute(project, block.Id, Options()).Status)
            .IsEqualTo(BlockLayoutStatus.InvalidInput).Because("座標が数値でない");
    }

    [Test]
    [DisplayName("寸法や間隔が 0 以下なら断る")]
    public async Task Compute_RejectsBadOptions()
    {
        var (project, _, nodes, block) = Grouped(3);
        Scatter(nodes);

        var options = Options();
        options.NodeWidth = 0;

        await Assert.That(BlockLayoutService.Compute(project, block.Id, options).Status)
            .IsEqualTo(BlockLayoutStatus.InvalidInput).Because("幅が 0");
    }

    [Test]
    [DisplayName("渡した設定オブジェクトを書き換えない")]
    public async Task Compute_DoesNotMutateOptions()
    {
        var (project, _, nodes, block) = Grouped(3);
        Scatter(nodes);

        var options = Options();
        options.OriginX = 555;
        options.OriginY = 777;

        BlockLayoutService.Compute(project, block.Id, options);

        await Assert.That(options.OriginX).IsEqualTo(555).Because("原点 X");
        await Assert.That(options.OriginY).IsEqualTo(777).Because("原点 Y");
    }

    // ---- 外側との重なり ----

    [Test]
    [DisplayName("並べ直すと外のカードに重なる場合は、何も適用しない")]
    public async Task Compute_RejectsWhenItWouldHitOutsideNode()
    {
        var (project, graph, nodes, block) = Grouped(4);

        // 縦に積んでおくと、整列で横長になる。その行き先を外のカードで塞ぐ。
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].X = 100;
            nodes[i].Y = 100 + i * 140;
        }

        var options = Options();
        var free = BlockLayoutService.Compute(project, block.Id, options);
        await Assert.That(free.IsReady).IsTrue().Because("塞ぐ前は整列できる");

        var target = free.Bounds!.Value;
        graph.AddNode(new TodoNode
        {
            Title = "邪魔",
            X = target.Right - options.NodeWidth - 10,
            Y = target.Y + 10,
        });

        var blocked = BlockLayoutService.Compute(project, block.Id, options);
        await Assert.That(blocked.Status)
            .IsEqualTo(BlockLayoutStatus.OverlapsOutside).Because("外に重なるので実行しない");
        await Assert.That(blocked.Positions.Count).IsEqualTo(0).Because("座標変更を要求しない");
    }

    [Test]
    [DisplayName("いまが重なっていても、新しい配置が重ならなければ整列できる")]
    public async Task Compute_AllowsFixingAnExistingOverlap()
    {
        var (project, graph, nodes, block) = Grouped(3);

        // 中を横一列に散らかしておく。整列後の囲みは、この範囲より右へは広がらない。
        nodes[0].X = 100; nodes[0].Y = 100;
        nodes[1].X = 900; nodes[1].Y = 100;
        nodes[2].X = 1700; nodes[2].Y = 100;

        // いまの囲みの中に外のカードを置く（すでに重なっている状態）。
        // 整列すると囲みは左へ縮むので、この位置は新しい囲みの外に出る。
        graph.AddNode(new TodoNode { Title = "いま重なっている", X = 1400, Y = 100 });

        var result = BlockLayoutService.Compute(project, block.Id, Options());
        await Assert.That(result.IsReady).IsTrue().Because("新しい配置が重ならなければ実行できる");
    }

    [Test]
    [DisplayName("別の囲みは、隠れている所属も含めた領域として避ける")]
    public async Task Compute_AvoidsOtherBlocks()
    {
        var (project, graph, nodes, block) = Grouped(3);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].X = 100;
            nodes[i].Y = 100 + i * 140;
        }

        var options = Options();
        var free = BlockLayoutService.Compute(project, block.Id, options);
        await Assert.That(free.IsReady).IsTrue().Because("隣の囲みを作る前は整列できる");

        var target = free.Bounds!.Value;
        var other1 = graph.AddNode(new TodoNode { Title = "隣 1", X = target.Right - options.NodeWidth - 10, Y = target.Y + 10 });
        var other2 = graph.AddNode(new TodoNode { Title = "隣 2", X = target.Right + 600, Y = target.Y + 10 });
        BlockService.Create(project, [other1.Id, other2.Id], "隣のブロック");

        var blocked = BlockLayoutService.Compute(project, block.Id, options);
        await Assert.That(blocked.Status)
            .IsEqualTo(BlockLayoutStatus.OverlapsOutside).Because("別の囲みにぶつかる");
    }

    // ---- 通過点 ----

    [Test]
    [DisplayName("通過点の座標・順序・滑らかさは触らない")]
    public async Task Compute_KeepsWaypoints()
    {
        var (project, _, nodes, block) = Grouped(3);
        Scatter(nodes);

        var edge = project.Edges[0];
        edge.Waypoints.Add(new JunctionPoint(11, 22, IsSmooth: true));
        edge.Waypoints.Add(new JunctionPoint(33, 44));

        var result = BlockLayoutService.Compute(project, block.Id, Options());
        Apply(project, result);

        await Assert.That(edge.Waypoints.Count).IsEqualTo(2).Because("数");
        await Assert.That(edge.Waypoints[0]).IsEqualTo(new JunctionPoint(11, 22, IsSmooth: true)).Because("1 つ目");
        await Assert.That(edge.Waypoints[1]).IsEqualTo(new JunctionPoint(33, 44)).Because("2 つ目");
    }

    // ---- 保存 ----

    [Test]
    [DisplayName("整列した座標は保存して読み直しても一致する")]
    public async Task Apply_SurvivesSaveAndLoad()
    {
        var (project, _, nodes, block) = Grouped(4);
        Scatter(nodes);

        var result = BlockLayoutService.Compute(project, block.Id, Options());
        Apply(project, result);

        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"todotree-blocklayout-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(path, project);
            var loaded = store.Load(path);

            foreach (var node in project.Nodes)
            {
                var same = loaded.Nodes.First(n => n.Id == node.Id);
                await Assert.That(same.X).IsEqualTo(node.X).Because($"{node.Title} の X");
                await Assert.That(same.Y).IsEqualTo(node.Y).Because($"{node.Title} の Y");
            }
        }
        finally
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

    // ---- 道具 ----

    private static LayoutOptions Options() =>
        NodeStyleMetrics.LayoutFor(NodeStyle.Card, LayoutDirection.LeftToRight);

    private static (TodoProject Project, TodoGraph Graph, List<TodoNode> Nodes, TodoBlock Block) Grouped(
        int count,
        bool connectChain = true)
    {
        var project = new TodoProject();
        var graph = new TodoGraph(project);
        var nodes = new List<TodoNode>();
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < count; i++)
        {
            // 並びが実行のたびに揺れないよう、作成時刻をはっきり分けておく。
            var node = graph.AddNode(new TodoNode { Title = $"S{i}", CreatedAt = created.AddMinutes(i) });
            nodes.Add(node);

            if (connectChain && i > 0)
            {
                graph.Connect(nodes[i - 1].Id, node.Id);
            }
        }

        var block = BlockService.Create(project, [.. nodes.Select(n => n.Id)]).Block!;
        return (project, graph, nodes, block);
    }

    /// <summary>手で動かしたあとのように、規則性のない座標へ散らす。</summary>
    private static void Scatter(List<TodoNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].X = 120 + ((i * 397) % 900);
            nodes[i].Y = 80 + ((i * 251) % 700);
        }
    }

    private static BlockBounds? BoundsOf(IReadOnlyList<TodoNode> nodes, LayoutOptions options) =>
        BlockGeometry.Compute(
            [.. nodes.Select(n => new NodeRect(n.Id, n.X, n.Y, options.NodeWidth, options.NodeHeight))]);

    private static void Apply(TodoProject project, BlockLayoutResult result)
    {
        foreach (var node in project.Nodes)
        {
            if (result.Positions.TryGetValue(node.Id, out var position))
            {
                node.X = position.X;
                node.Y = position.Y;
            }
        }
    }

    /// <summary>プロジェクトの中身を 1 本の文字列にする（何も動いていないことの確認用）。</summary>
    private static string Fingerprint(TodoProject project)
    {
        var nodes = project.Nodes
            .OrderBy(n => n.Id)
            .Select(n => $"{n.Id}:{n.X:R}:{n.Y:R}:{n.IsPinned}");

        var edges = project.Edges
            .OrderBy(e => e.Id)
            .Select(e => $"{e.Id}:{e.FromId}>{e.ToId}:" + string.Join(",", e.Waypoints.Select(w => $"{w.X:R}/{w.Y:R}/{w.IsSmooth}")));

        var blocks = project.Blocks
            .OrderBy(b => b.Id)
            .Select(b => $"{b.Id}:{b.Title}:" + string.Join(",", b.NodeIds));

        return string.Join("|", nodes) + "//" + string.Join("|", edges) + "//" + string.Join("|", blocks);
    }
}
