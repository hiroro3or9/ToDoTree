using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class BookmarkTests
{
    [Test]
    public async Task DeleteAndSnapshotRestore_PreserveBookmarkIndependently()
    {
        var node = new TodoNode { Title = "再開" };
        var other = new TodoNode();
        var project = new TodoProject
        {
            Nodes = [node, other],
            Bookmark = new WorkBookmark { NodeId = node.Id, Note = "次は確認\n結果を記録" },
        };
        var snapshot = project.DeepClone();
        project.Bookmark.Note = "編集後";
        var graph = new TodoGraph(project);
        graph.RemoveNode(other.Id);
        await Assert.That(project.Bookmark is not null).IsTrue();
        graph.RemoveNodeAndBridge(node.Id);
        await Assert.That(project.Bookmark is null).IsTrue();
        await Assert.That(snapshot.Bookmark!.Note).IsEqualTo("次は確認\n結果を記録");
        await Assert.That(new TodoGraph(snapshot).Contains(snapshot.Bookmark.NodeId)).IsTrue();
    }

    [Test]
    public async Task Storage_RoundTripsBookmarkAndMigratesVersionTwoBlocks()
    {
        var node = new TodoNode();
        var other = new TodoNode();
        var project = new TodoProject
        {
            Nodes = [node, other],
            Bookmark = new WorkBookmark { NodeId = node.Id, Note = "ここで中断\n次は動作確認" },
        };
        BlockService.Create(project, [node.Id, other.Id], "作業");
        var path = Path.Combine(Path.GetTempPath(), $"bookmark-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore();
            store.Save(path, project);
            var loaded = store.Load(path);
            await Assert.That(loaded.Bookmark!.NodeId).IsEqualTo(node.Id);
            await Assert.That(loaded.Bookmark.Note).IsEqualTo(project.Bookmark.Note);
            project.Bookmark = null;
            project.SchemaVersion = 2;
            File.WriteAllText(path, JsonSerializer.Serialize(project, JsonProjectStore.SerializerOptions));
            loaded = store.Load(path);
            await Assert.That(loaded.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);
            await Assert.That(loaded.Blocks.Count).IsEqualTo(1);
            await Assert.That(loaded.Bookmark is null).IsTrue();
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Test]
    public async Task Storage_RejectsMissingBookmarkTarget()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bookmark-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonProjectStore();
            store.Save(path, new TodoProject { Bookmark = new WorkBookmark { NodeId = Guid.NewGuid() } });
            var rejected = false;
            try { store.Load(path); }
            catch (InvalidDataException) { rejected = true; }
            await Assert.That(rejected).IsTrue();
        }
        finally { File.Delete(path); }
    }
}
