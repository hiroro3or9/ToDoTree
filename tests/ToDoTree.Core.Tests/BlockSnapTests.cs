using ToDoTree.Core.Layout;

namespace ToDoTree.Core.Tests;

public class BlockSnapTests
{
    private static readonly BlockBounds Start = new(0, 0, 100, 100);
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Test]
    public async Task EveryAnchor_AlignsOnBothAxes()
    {
        foreach (var anchor in Enum.GetValues<SnapAnchor>())
        {
            var offset = (int)anchor * 50d;
            var x = BlockSnapService.Compute(Start, new(offset - 5, 0),
                [new(First, new(0, 150, 200, 100))], new(), 1);
            await Assert.That(x.Delta.X).IsEqualTo(offset);
            await Assert.That(x.State.X!.Anchor).IsEqualTo(anchor);
            var y = BlockSnapService.Compute(Start, new(0, offset - 5),
                [new(First, new(150, 0, 100, 200))], new(), 1);
            await Assert.That(y.Delta.Y).IsEqualTo(offset);
            await Assert.That(y.State.Y!.Anchor).IsEqualTo(anchor);
        }
    }

    [Test]
    public async Task Zoom_PreservesScreenDistancesAndReleaseHysteresis()
    {
        foreach (var zoom in new[] { 0.25, 1, 2.5 })
        {
            SnapTarget[] targets = [new(First, new(0, 150, 100, 100))];
            var acquired = BlockSnapService.Compute(Start, new(6 / zoom, 0), targets, new(), zoom);
            await Assert.That(acquired.State.X is not null).IsTrue();
            var outside = BlockSnapService.Compute(Start, new(6.1 / zoom, 0), targets, new(), zoom);
            await Assert.That(outside.State.X is null).IsTrue();
            var held = BlockSnapService.Compute(Start, new(10 / zoom, 0), targets, acquired.State, zoom);
            await Assert.That(held.Delta.X).IsEqualTo(0);
            var released = BlockSnapService.Compute(Start, new(10.1 / zoom, 0), targets, held.State, zoom);
            await Assert.That(released.State.X is null).IsTrue();
            await Assert.That(released.Delta.X).IsEqualTo(10.1 / zoom);
        }
    }

    [Test]
    public async Task HeldTarget_DoesNotSwitchToNearerCandidate()
    {
        SnapTarget[] targets = [new(First, new(0, 150, 100, 100)), new(Second, new(9, 150, 100, 100))];
        var initial = BlockSnapService.Compute(Start, new(0, 0), targets, new(), 1);
        var held = BlockSnapService.Compute(Start, new(9, 0), targets, initial.State, 1);
        await Assert.That(held.State.X!.TargetId).IsEqualTo(First);
        var released = BlockSnapService.Compute(Start, new(11, 0), targets, held.State, 1);
        await Assert.That(released.State.X is null).IsTrue();
        var next = BlockSnapService.Compute(Start, new(11, 0), targets, released.State, 1);
        await Assert.That(next.State.X!.TargetId).IsEqualTo(Second);
    }

    [Test]
    public async Task CrossAxisRange_HasSeparateAcquireAndReleaseLimits()
    {
        SnapTarget[] targets = [new(First, new(0, 260, 100, 100))];
        var acquired = BlockSnapService.Compute(Start, new(0, 0), targets, new(), 1);
        await Assert.That(acquired.State.X is not null).IsTrue();
        var outside = BlockSnapService.Compute(Start, new(0, -0.1), targets, new(), 1);
        await Assert.That(outside.State.X is null).IsTrue();
        var held = BlockSnapService.Compute(Start, new(0, -24), targets, acquired.State, 1);
        await Assert.That(held.State.X is not null).IsTrue();
        var released = BlockSnapService.Compute(Start, new(0, -24.1), targets, held.State, 1);
        await Assert.That(released.State.X is null).IsTrue();
    }

    [Test]
    public async Task CandidateOrder_DoesNotAffectTies()
    {
        SnapTarget[] targets = [new(Second, new(5, 150, 100, 100)), new(First, new(-5, 150, 100, 100))];
        var a = BlockSnapService.Compute(Start, new(0, 0), targets, new(), 1);
        var b = BlockSnapService.Compute(Start, new(0, 0), [.. targets.Reverse()], new(), 1);
        await Assert.That(a.State).IsEqualTo(b.State);
        await Assert.That(a.State.X!.TargetId).IsEqualTo(First);
    }

    [Test]
    public async Task Axes_CanUseDifferentTargets_AndProduceBoundedGuides()
    {
        var result = BlockSnapService.Compute(Start, new(4, 5),
            [new(First, new(0, 150, 100, 100)), new(Second, new(150, 0, 100, 100))], new(), 1);
        await Assert.That(result.State.X!.TargetId).IsEqualTo(First);
        await Assert.That(result.State.Y!.TargetId).IsEqualTo(Second);
        await Assert.That(result.Delta).IsEqualTo(new Vec2(0, 0));
        await Assert.That(result.Guides.Count).IsEqualTo(2);
        await Assert.That(result.Guides[0]).IsEqualTo(new SnapGuide(true, 0, 0, 250));
    }

    [Test]
    public async Task Bypass_AndMissingTargets_ClearStateWithoutChangingRawMovement()
    {
        var previous = new SnapState(new(First, SnapAnchor.Start), new(Second, SnapAnchor.End));
        var bypass = BlockSnapService.Compute(Start, new(4, 7),
            [new(First, new(0, 150, 100, 100))], previous, 1, true);
        var missing = BlockSnapService.Compute(Start, new(4, 7), [], previous, 1);
        foreach (var result in new[] { bypass, missing })
        {
            await Assert.That(result.Delta).IsEqualTo(new Vec2(4, 7));
            await Assert.That(result.State).IsEqualTo(new SnapState());
            await Assert.That(result.Guides.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task NegativeCoordinates_AndRepeatedUpdates_DoNotAccumulateCorrection()
    {
        var start = new BlockBounds(-100, -200, 100, 100);
        SnapTarget[] targets = [new(First, new(-80, -50, 100, 100))];
        var state = new SnapState();
        for (var i = 0; i < 100; i++)
        {
            var result = BlockSnapService.Compute(start, new(16, 0), targets, state, 1);
            state = result.State;
            await Assert.That(result.Delta.X).IsEqualTo(20);
        }
        var free = BlockSnapService.Compute(start, new(35, 0), targets, state, 1);
        await Assert.That(free.Delta.X).IsEqualTo(35);
    }

    [Test]
    public async Task InvalidInput_IsRejected()
    {
        foreach (var zoom in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            var rejected = false;
            try { BlockSnapService.Compute(Start, new(0, 0), [], new(), zoom); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            await Assert.That(rejected).IsTrue();
        }
        var invalid = false;
        try { BlockSnapService.Compute(Start, new(double.NaN, 0), [], new(), 1); }
        catch (ArgumentOutOfRangeException) { invalid = true; }
        await Assert.That(invalid).IsTrue();
    }
}
