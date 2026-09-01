using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

public static class StorageTests
{
    public static void Register()
    {
        MiniTest.Case("保存して読み直すと中身が一致する", () =>
        {
            var project = SampleProject.Create();
            var store = new JsonProjectStore();
            var path = Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

            try
            {
                store.Save(path, project);
                var loaded = store.Load(path);

                MiniTest.Equal(project.Name, loaded.Name, "プロジェクト名");
                MiniTest.Equal(project.Nodes.Count, loaded.Nodes.Count, "ノード数");
                MiniTest.Equal(project.Edges.Count, loaded.Edges.Count, "辺の数");

                var originalGoal = project.Nodes.First(n => n.Kind == NodeKind.Goal);
                var loadedGoal = loaded.Nodes.First(n => n.Id == originalGoal.Id);
                MiniTest.Equal(originalGoal.Title, loadedGoal.Title, "ゴールのタイトル");
                MiniTest.Equal(originalGoal.X, loadedGoal.X, "座標 X");
                MiniTest.Equal(originalGoal.EstimateMinutes, loadedGoal.EstimateMinutes, "見積もり");

                var graph = new TodoGraph(loaded);
                MiniTest.False(graph.HasCycle(), "読み込んでも DAG のまま");
            }
            finally
            {
                Cleanup(path);
            }
        });

        MiniTest.Case("日本語がそのまま書き出される", () =>
        {
            var store = new JsonProjectStore();
            var project = new TodoProject { Name = "日本語のプロジェクト" };
            var path = Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

            try
            {
                store.Save(path, project);
                var text = File.ReadAllText(path);
                MiniTest.True(text.Contains("日本語のプロジェクト"), "エスケープされずに読める");
            }
            finally
            {
                Cleanup(path);
            }
        });

        MiniTest.Case("上書き保存でバックアップが残る", () =>
        {
            var store = new JsonProjectStore();
            var path = Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

            try
            {
                store.Save(path, new TodoProject { Name = "一回目" });
                store.Save(path, new TodoProject { Name = "二回目" });

                MiniTest.Equal("二回目", store.Load(path).Name, "最新の内容");
                MiniTest.True(File.Exists(path + ".bak"), ".bak が作られる");
                MiniTest.False(File.Exists(path + ".tmp"), ".tmp は残らない");
            }
            finally
            {
                Cleanup(path);
            }
        });

        MiniTest.Case("複製は元に影響しない", () =>
        {
            var project = SampleProject.Create();
            var clone = project.DeepClone();
            clone.Nodes[0].Title = "書き換えた";
            MiniTest.False(project.Nodes[0].Title == "書き換えた", "元のノードは変わらない");
            MiniTest.Equal(project.Edges.Count, clone.Edges.Count, "辺の数は同じ");
        });
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
