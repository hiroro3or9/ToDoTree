using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class CompletedTaskTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");
    private static readonly DateOnly Date = new(2026, 9, 11);

    [Test]
    public async Task LocalDate_UsesMidnightBoundariesAndSortsNewestFirst()
    {
        var start = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(9));
        TodoNode Done(DateTimeOffset time) => new() { Status = NodeStatus.Done, CompletedAt = time.ToUniversalTime() };
        var yesterday = Done(start.AddTicks(-1));
        var first = Done(start);
        var last = Done(start.AddDays(1).AddTicks(-1));
        var tomorrow = Done(start.AddDays(1));
        var result = CompletedTaskQuery.ForDate([yesterday, first, tomorrow, last], Date, Zone);
        await Assert.That(result.Select(n => n.Id).SequenceEqual(new[] { last.Id, first.Id })).IsTrue();
    }

    [Test]
    public async Task OnlyDoneWithCompletionTime_IsIncluded()
    {
        var now = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.FromHours(9));
        var nodes = Enum.GetValues<NodeStatus>().Select(status => new TodoNode { Status = status, CompletedAt = now }).ToList();
        nodes.Add(new TodoNode { Status = NodeStatus.Done });
        var result = CompletedTaskQuery.ForDate(nodes, Date, Zone);
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Status).IsEqualTo(NodeStatus.Done);
    }

    [Test]
    public async Task SavedCompletion_RemainsAvailableAfterReopening()
    {
        var node = new TodoNode { Title = "保存して閉じた作業", Status = NodeStatus.Done,
            CompletedAt = new DateTimeOffset(2026, 9, 11, 10, 15, 0, TimeSpan.FromHours(9)) };
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"todotree-completed-{Guid.NewGuid():N}.json");
        try
        {
            store.Save(path, new TodoProject { Nodes = [node] });
            var result = CompletedTaskQuery.ForDate(store.Load(path).Nodes, Date, Zone);
            await Assert.That(result.Single().Id).IsEqualTo(node.Id);
            await Assert.That(result.Single().CompletedAt).IsEqualTo(node.CompletedAt);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
