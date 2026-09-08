using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Tests;

public class CardSnapTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Block1 = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid Block2 = Guid.Parse("00000000-0000-0000-0000-0000000000f2");

    /// <summary>広めに取って、表示範囲の判定が他のテストの邪魔をしないようにする。</summary>
    private static readonly BlockBounds Wide = new(-2000, -2000, 8000, 8000);

    private static NodeRect Card(Guid id, double x, double y, double width = 224, double height = 88) =>
        new(id, x, y, width, height);

    private static HashSet<Guid> Moving(params Guid[] ids) => [.. ids];

    [Test]
    public async Task MovingBounds_WrapsEveryCard()
    {
        var single = CardSnapService.MovingBounds([Card(A, 10, 20)]);
        await Assert.That(single!.Value.X).IsEqualTo(10).Because("1 枚ならそのカードの矩形");
        await Assert.That(single!.Value.Y).IsEqualTo(20);
        await Assert.That(single!.Value.Width).IsEqualTo(224);
        await Assert.That(single!.Value.Height).IsEqualTo(88);

        var pair = CardSnapService.MovingBounds([Card(A, 10, 20), Card(B, -30, 200)]);
        await Assert.That(pair!.Value.X).IsEqualTo(-30).Because("負の座標も含めて囲む");
        await Assert.That(pair!.Value.Y).IsEqualTo(20);
        await Assert.That(pair!.Value.Width).IsEqualTo(264);
        await Assert.That(pair!.Value.Height).IsEqualTo(268);

        await Assert.That(CardSnapService.MovingBounds([]) is null).IsTrue().Because("動かすカードが無い");
        await Assert.That(CardSnapService.MovingBounds([Card(A, double.NaN, 0)]) is null).IsTrue()
            .Because("使えない矩形しか無ければ吸着させない");
    }

    [Test]
    public async Task Targets_DropTheMovingCardsAndTheBlocksThatHoldThem()
    {
        // Block1 は動かす A を抱えているので A と一緒に動く。揃え先にすると自分を追いかけることになる。
        var targets = CardSnapService.Targets(
            [Card(A, 0, 0), Card(C, 300, 0), Card(B, 600, 0)],
            [
                new(Block1, new(-24, -32, 572, 144), [A, C]),
                new(Block2, new(576, -32, 272, 144), [B]),
            ],
            Moving(A),
            Wide);

        var ids = targets.Select(t => t.Id).ToList();
        await Assert.That(ids.Contains(A)).IsFalse().Because("動かすカード自身は揃え先にしない");
        await Assert.That(ids.Contains(Block1)).IsFalse().Because("動かすカードを含む囲みも動く");
        await Assert.That(ids.Contains(C)).IsTrue().Because("同じ囲みの中でも、動かさないカードには揃えられる");
        await Assert.That(ids.Contains(B)).IsTrue().Because("外のカード");
        await Assert.That(ids.Contains(Block2)).IsTrue().Because("動かすカードを含まない囲み");
        await Assert.That(targets.Count).IsEqualTo(3).Because("重複して積まない");
    }

    [Test]
    public async Task Targets_DropWhatIsOutsideTheViewport()
    {
        BlockSnapCandidate[] blocks = [new(Block2, new(5000, 5000, 200, 100), [B])];
        var viewport = new BlockBounds(0, 0, 800, 600);

        var inside = CardSnapService.Targets([Card(B, 100, 100)], blocks, Moving(A), viewport);
        await Assert.That(inside.Count).IsEqualTo(1).Because("画面内のカードは残る");

        var straddling = CardSnapService.Targets([Card(B, 700, 100)], blocks, Moving(A), viewport);
        await Assert.That(straddling.Count).IsEqualTo(1).Because("端にかかっていれば残る");

        var outside = CardSnapService.Targets([Card(B, 900, 100)], blocks, Moving(A), viewport);
        await Assert.That(outside.Count).IsEqualTo(0).Because("画面の外のカードと囲みは外す");

        var unknown = CardSnapService.Targets([Card(B, 100, 100)], blocks, Moving(A), new(0, 0, 0, 0));
        await Assert.That(unknown.Count).IsEqualTo(0).Because("表示範囲が取れていなければ吸着させない");
    }

    [Test]
    public async Task SameSizeCards_AlignOnEveryAnchorAtOnce()
    {
        // 同じ寸法どうしでは、左端・中心・右端がまとめて一致する。どれが選ばれても位置は同じで、
        // 種別の固定順から Start になる。カード表示でもミニマル表示でも変わらない。
        foreach (var (width, height) in new[] { (224d, 88d), (24d, 24d) })
        {
            var start = new BlockBounds(0, 0, width, height);
            SnapTarget[] targets = [new(B, new(0, height + 60, width, height))];

            var result = BlockSnapService.Compute(start, new(5, 0), targets, new(), 1);

            await Assert.That(result.Delta.X).IsEqualTo(0).Because($"{width}×{height} で左右が揃う");
            await Assert.That(result.State.X!.Anchor).IsEqualTo(SnapAnchor.Start);
            await Assert.That(result.State.Y is null).IsTrue().Because("縦は離れている");
            await Assert.That(result.Guides.Count).IsEqualTo(1).Because("揃った軸だけ線を引く");
            await Assert.That(result.Guides[0].IsVertical).IsTrue();
        }
    }

    [Test]
    public async Task MinimalCard_SnapsToTheCenterOfABlock()
    {
        // ミニマル表示の丸（24×24）を、囲みの横方向の中心へ揃える。
        // 寸法が違う相手では、端と中心が別々の候補になる。
        var start = new BlockBounds(100, 0, 24, 24);
        SnapTarget[] targets = [new(Block1, new(0, 100, 300, 120))];

        var result = BlockSnapService.Compute(start, new(35, 0), targets, new(), 1);

        await Assert.That(result.State.X!.Anchor).IsEqualTo(SnapAnchor.Center).Because("中心が最寄り");
        await Assert.That(result.Delta.X).IsEqualTo(38).Because("112 から 150 へ寄せる");
        await Assert.That(result.State.Y is null).IsTrue().Because("縦は揃っていない");
    }
}
