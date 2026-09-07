using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

namespace ToDoTree.Core.Tests;

/// <summary>ブロックと線の個別色。色は飾りなので、壊れていても読み込みを止めない。</summary>
public class ColorTests
{
    [Test]
    [DisplayName("ブロックと線の色は保存して読み直しても残る")]
    public async Task Color_RoundTrips()
    {
        var (project, block, edge) = Sample("blue", "red");
        var store = new JsonProjectStore();
        var path = TempPath();

        try
        {
            store.Save(path, project);
            var loaded = store.Load(path);

            await Assert.That(loaded.Blocks.Single(b => b.Id == block.Id).ColorId)
                .IsEqualTo("blue").Because("ブロックの色");
            await Assert.That(loaded.Edges.Single(e => e.Id == edge.Id).ColorId)
                .IsEqualTo("red").Because("線の色");
            await Assert.That(loaded.SchemaVersion)
                .IsEqualTo(TodoProject.CurrentSchemaVersion).Because("形式の版");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("既定色のときは colorId を書き出さない")]
    public async Task DefaultColor_IsNotWritten()
    {
        var (project, _, _) = Sample(null, null);
        var store = new JsonProjectStore();
        var path = TempPath();

        try
        {
            store.Save(path, project);
            await Assert.That(File.ReadAllText(path).Contains("colorId"))
                .IsFalse().Because("既存ファイルの見た目を変えない");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("色を知らない形式のファイルも読める")]
    public async Task OlderSchema_LoadsWithoutColor()
    {
        var (project, block, _) = Sample(null, null);
        project.SchemaVersion = 4;
        var store = new JsonProjectStore();
        var path = TempPath();

        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
                project, JsonProjectStore.SerializerOptions));
            var loaded = store.Load(path);

            await Assert.That(loaded.Blocks.Single(b => b.Id == block.Id).ColorId)
                .IsNull().Because("色を持たない形式は既定色になる");
            await Assert.That(loaded.SchemaVersion)
                .IsEqualTo(TodoProject.CurrentSchemaVersion).Because("読み込み時に版を上げる");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("知らない色が入っていても読み込みを止めず、値も捨てない")]
    public async Task UnknownColor_SurvivesRoundTrip()
    {
        var (project, block, edge) = Sample("mystery", "mystery");
        var store = new JsonProjectStore();
        var path = TempPath();

        try
        {
            store.Save(path, project);
            var loaded = store.Load(path);

            await Assert.That(loaded.Blocks.Single(b => b.Id == block.Id).ColorId)
                .IsEqualTo("mystery").Because("知らない色でも書き戻せるように保持する");
            await Assert.That(loaded.Edges.Single(e => e.Id == edge.Id).ColorId)
                .IsEqualTo("mystery").Because("線も同じ");
            await Assert.That(ColorPresets.IsKnown("mystery"))
                .IsFalse().Because("描くときは既定色へ落ちる");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("複写しても色が引き継がれる")]
    public async Task Clone_KeepsColor()
    {
        var (project, block, edge) = Sample("green", "violet");
        var copy = project.DeepClone();

        await Assert.That(copy.Blocks.Single(b => b.Id == block.Id).ColorId)
            .IsEqualTo("green").Because("ブロック");
        await Assert.That(copy.Edges.Single(e => e.Id == edge.Id).ColorId)
            .IsEqualTo("violet").Because("線");
    }

    [Test]
    [DisplayName("部品として保存するとブロックの色も一緒に残る")]
    public async Task Template_KeepsBlockColor()
    {
        var (project, block, _) = Sample("teal", null);
        var template = BranchTemplate.Capture(project, block.NodeIds, "色つきの部品");

        await Assert.That(template.Blocks.Single().ColorId)
            .IsEqualTo("teal").Because("配置し直したときも同じ色で出したい");
    }

    [Test]
    [DisplayName("プリセットの id は重複せず、すべて既知として扱われる")]
    public async Task Presets_AreUniqueAndKnown()
    {
        var ids = ColorPresets.All.Select(p => p.Id).ToList();

        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Count).Because("id の重複なし");
        await Assert.That(ids.All(ColorPresets.IsKnown)).IsTrue().Because("すべて既知");
        await Assert.That(ids.All(id => id.All(c => c is >= 'a' and <= 'z'))).IsTrue()
            .Because("リソースキーの末尾に足すので英小文字だけにする");
        await Assert.That(ColorPresets.IsKnown(null)).IsFalse().Because("null は既定色であって色ではない");
    }

    private static (TodoProject Project, TodoBlock Block, TodoEdge Edge) Sample(
        string? blockColor, string? edgeColor)
    {
        var first = new TodoNode { Title = "要件整理" };
        var second = new TodoNode { Title = "画面設計", X = 300 };
        var edge = new TodoEdge { FromId = first.Id, ToId = second.Id, ColorId = edgeColor };
        var block = new TodoBlock
        {
            Title = "設計",
            ColorId = blockColor,
            NodeIds = [first.Id, second.Id],
        };

        return (new TodoProject
        {
            Nodes = [first, second],
            Edges = [edge],
            Blocks = [block],
        }, block, edge);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"todotree-color-{Guid.NewGuid():N}.json");

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
