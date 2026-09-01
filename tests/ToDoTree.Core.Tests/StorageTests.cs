using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public class StorageTests
{
    [Test]
    [DisplayName("保存して読み直すと中身が一致する")]
    public async Task SaveThenLoad_RoundTripsContent()
    {
        var project = SampleProject.Create();
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(path, project);
            var loaded = store.Load(path);

            await Assert.That(loaded.Name).IsEqualTo(project.Name).Because("プロジェクト名");
            await Assert.That(loaded.Nodes.Count).IsEqualTo(project.Nodes.Count).Because("ノード数");
            await Assert.That(loaded.Edges.Count).IsEqualTo(project.Edges.Count).Because("辺の数");

            var originalGoal = project.Nodes.First(n => n.Kind == NodeKind.Goal);
            var loadedGoal = loaded.Nodes.First(n => n.Id == originalGoal.Id);
            await Assert.That(loadedGoal.Title).IsEqualTo(originalGoal.Title).Because("ゴールのタイトル");
            await Assert.That(loadedGoal.X).IsEqualTo(originalGoal.X).Because("座標 X");
            await Assert.That(loadedGoal.EstimateMinutes).IsEqualTo(originalGoal.EstimateMinutes).Because("見積もり");

            var graph = new TodoGraph(loaded);
            await Assert.That(graph.HasCycle()).IsFalse().Because("読み込んでも DAG のまま");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("日本語がそのまま書き出される")]
    public async Task Save_WritesJapaneseUnescaped()
    {
        var store = new JsonProjectStore();
        var project = new TodoProject { Name = "日本語のプロジェクト" };
        var path = Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(path, project);
            var text = File.ReadAllText(path);
            await Assert.That(text.Contains("日本語のプロジェクト")).IsTrue().Because("エスケープされずに読める");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("上書き保存でバックアップが残る")]
    public async Task Save_KeepsBackupOnOverwrite()
    {
        var store = new JsonProjectStore();
        var path = Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(path, new TodoProject { Name = "一回目" });
            store.Save(path, new TodoProject { Name = "二回目" });

            await Assert.That(store.Load(path).Name).IsEqualTo("二回目").Because("最新の内容");
            await Assert.That(File.Exists(path + ".bak")).IsTrue().Because(".bak が作られる");
            await Assert.That(File.Exists(path + ".tmp")).IsFalse().Because(".tmp は残らない");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("複製は元に影響しない")]
    public async Task DeepClone_DoesNotAffectOriginal()
    {
        var project = SampleProject.Create();
        var clone = project.DeepClone();
        clone.Nodes[0].Title = "書き換えた";
        await Assert.That(project.Nodes[0].Title == "書き換えた").IsFalse().Because("元のノードは変わらない");
        await Assert.That(clone.Edges.Count).IsEqualTo(project.Edges.Count).Because("辺の数は同じ");
    }

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
