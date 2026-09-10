using System.Text.Json;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;
using ToDoTree.Core.Text;

namespace ToDoTree.Core.Tests;

public class ProjectVariableTests
{
    private static ProjectVariableResolver Resolver(params (string Name, string Value)[] pairs) =>
        ProjectVariableResolver.From(pairs.Select(p => new ProjectVariable { Name = p.Name, Value = p.Value }));

    private static TodoProject ProjectWith(params (string Title, string Notes)[] steps)
    {
        var project = new TodoProject { Name = "変数のテスト" };
        foreach (var (title, notes) in steps)
        {
            project.Nodes.Add(new TodoNode { Title = title, Notes = notes });
        }

        return project;
    }

    // ---- 記法 ----

    [Test]
    [DisplayName("同じ変数を複数回参照しても、すべて展開する")]
    public async Task Expand_ReplacesEveryReference()
    {
        var resolver = Resolver(("Hoge", "なにかしらの固有名称"));
        await Assert.That(resolver.Expand("{Hoge} の手順書を {Hoge} 用に作成"))
            .IsEqualTo("なにかしらの固有名称 の手順書を なにかしらの固有名称 用に作成");
    }

    [Test]
    [DisplayName("日本語の変数名も使える")]
    public async Task Expand_AcceptsJapaneseName()
    {
        var resolver = Resolver(("製品名", "みかん箱"));
        await Assert.That(resolver.Expand("{製品名} の梱包")).IsEqualTo("みかん箱 の梱包");
    }

    [Test]
    [DisplayName("変数名は大文字と小文字を区別する")]
    public async Task Expand_IsCaseSensitive()
    {
        var resolver = Resolver(("Hoge", "値"));
        await Assert.That(resolver.Expand("{hoge}")).IsEqualTo("{hoge}").Because("別の名前として扱う");
        await Assert.That(resolver.Scan("{hoge}").HasUndefined).IsTrue();
    }

    [Test]
    [DisplayName("未定義の参照は原文のまま残り、未定義として報告される")]
    public async Task Scan_ReportsUndefined()
    {
        var scan = Resolver(("Hoge", "値")).Scan("{Hoge} と {Fuga}");

        await Assert.That(scan.Display).IsEqualTo("値 と {Fuga}");
        await Assert.That(scan.ReferencedNames).IsEquivalentTo(new[] { "Hoge" });
        await Assert.That(scan.UndefinedNames).IsEquivalentTo(new[] { "Fuga" });
    }

    [Test]
    [DisplayName("不完全・不正な記法は入力どおりに残す")]
    public async Task Expand_LeavesMalformedNotation()
    {
        var resolver = Resolver(("Hoge", "値"), ("a", "A"));

        await Assert.That(resolver.Expand("{Hoge")).IsEqualTo("{Hoge").Because("閉じていない");
        await Assert.That(resolver.Expand("{ Hoge }")).IsEqualTo("{ Hoge }").Because("空白を含む");
        await Assert.That(resolver.Expand("{a b}")).IsEqualTo("{a b}").Because("名前として不正");
        await Assert.That(resolver.Expand("Hoge}")).IsEqualTo("Hoge}");
        await Assert.That(resolver.Expand("{1Hoge}")).IsEqualTo("{1Hoge}").Because("数字では始められない");
    }

    [Test]
    [DisplayName("二重の波括弧はリテラルとして1つに戻り、参照として扱わない")]
    public async Task Expand_UnescapesDoubledBraces()
    {
        var resolver = Resolver(("Hoge", "値"));

        await Assert.That(resolver.Expand("{{Hoge}}")).IsEqualTo("{Hoge}");
        await Assert.That(resolver.Scan("{{Hoge}}").HasReference).IsFalse();
        await Assert.That(resolver.Expand("{{}} と {Hoge}")).IsEqualTo("{} と 値");
    }

    [Test]
    [DisplayName("展開した値は再走査しない")]
    public async Task Expand_DoesNotRescanValues()
    {
        var resolver = Resolver(("Hoge", "{Other}"), ("Other", "深い値"));
        await Assert.That(resolver.Expand("{Hoge}")).IsEqualTo("{Other}");
    }

    [Test]
    [DisplayName("区間は原文の位置と表示の位置を別に持つ")]
    public async Task Scan_TracksSourceAndDisplayPositions()
    {
        var scan = Resolver(("Hoge", "12345")).Scan("A{Hoge}B");
        var reference = scan.Segments.Single(s => s.Kind == VariableSegmentKind.Reference);

        await Assert.That(reference.SourceStart).IsEqualTo(1);
        await Assert.That(reference.SourceLength).IsEqualTo(6).Because("{Hoge} の6文字");
        await Assert.That(reference.DisplayStart).IsEqualTo(1);
        await Assert.That(reference.DisplayLength).IsEqualTo(5).Because("展開後の値の長さ");
        await Assert.That(scan.Display).IsEqualTo("A12345B");
    }

    [Test]
    [DisplayName("波括弧の無い原文はそのまま返す")]
    public async Task Expand_PassesThroughPlainText()
    {
        await Assert.That(Resolver(("Hoge", "値")).Expand("ふつうの文章")).IsEqualTo("ふつうの文章");
    }

    // ---- 定義の検証 ----

    [Test]
    [DisplayName("不正な名前・値・重複は検証で弾く")]
    public async Task Validate_RejectsBadDefinitions()
    {
        static string? Check(string name, string value) =>
            ProjectVariableService.Validate([new ProjectVariable { Name = name, Value = value }]);

        await Assert.That(Check("Hoge", "値")).IsNull();
        await Assert.That(Check("Ho ge", "値")).IsNotNull().Because("空白を含む名前");
        await Assert.That(Check("1Hoge", "値")).IsNotNull().Because("数字で始まる名前");
        await Assert.That(Check(string.Empty, "値")).IsNotNull().Because("空の名前");
        await Assert.That(Check("Hoge", string.Empty)).IsNotNull().Because("空の値");
        await Assert.That(Check("Hoge", "   ")).IsNotNull().Because("空白だけの値");
        await Assert.That(Check("Hoge", "1行目\n2行目")).IsNotNull().Because("改行を含む値");
        await Assert.That(Check("Hoge", new string('あ', 4097))).IsNotNull().Because("長すぎる値");

        await Assert.That(ProjectVariableService.Validate(
            [new ProjectVariable { Name = "Hoge", Value = "A" }, new ProjectVariable { Name = "Hoge", Value = "B" }]))
            .IsNotNull().Because("重複した名前");
    }

    [Test]
    [DisplayName("前後の空白を含む値は保持する")]
    public async Task Validate_KeepsSurroundingSpaces()
    {
        await Assert.That(ProjectVariableService.Validate(
            [new ProjectVariable { Name = "Hoge", Value = " 値 " }])).IsNull();
        await Assert.That(Resolver(("Hoge", " 値 ")).Expand("[{Hoge}]")).IsEqualTo("[ 値 ]");
    }

    // ---- 使用箇所と改名 ----

    [Test]
    [DisplayName("使用箇所はフィールド単位で数え、同一フィールド内の2回は1箇所")]
    public async Task FindUsages_CountsFieldsNotOccurrences()
    {
        var project = ProjectWith(("{Hoge} と {Hoge}", "メモ"), ("関係ない", "{Hoge} の設定"));
        var resolver = Resolver(("Hoge", "値"));

        var usages = ProjectVariableService.FindUsages(project, resolver, "Hoge");

        await Assert.That(usages.Count).IsEqualTo(2).Because("タイトル1件とメモ1件");
        await Assert.That(usages[0].Occurrences).IsEqualTo(2).Because("出現回数は詳細で示す");
        await Assert.That(usages[1].Kind).IsEqualTo(VariableFieldKind.NodeNotes);
        await Assert.That(ProjectVariableService.CountUsages(project, resolver)["Hoge"]).IsEqualTo(2);
    }

    [Test]
    [DisplayName("受信箱の項目名も対象フィールドに含む")]
    public async Task TargetFields_IncludeInbox()
    {
        var project = ProjectWith(("ステップ", string.Empty));
        project.Inbox.Add(new InboxItem { Title = "{Hoge} を確認" });

        var usages = ProjectVariableService.FindUsages(project, Resolver(("Hoge", "値")), "Hoge");

        await Assert.That(usages.Count).IsEqualTo(1);
        await Assert.That(usages[0].Kind).IsEqualTo(VariableFieldKind.InboxTitle);
        await Assert.That(usages[0].Display).IsEqualTo("値 を確認");
    }

    [Test]
    [DisplayName("改名は有効な参照だけを書き換え、エスケープと非対象フィールドは変えない")]
    public async Task PlanRename_TouchesOnlyValidReferences()
    {
        var project = ProjectWith(("{Hoge} と {{Hoge}} と {Hoge", "{Hoge} のメモ"));
        project.Nodes[0].Tags.Add("{Hoge}");
        project.Nodes[0].BlockReason = "{Hoge}";
        var resolver = Resolver(("Hoge", "値"));

        var rewrites = ProjectVariableService.PlanRename(project, resolver,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Hoge"] = "Fuga" });
        ProjectVariableService.ApplyRename(rewrites);

        await Assert.That(project.Nodes[0].Title).IsEqualTo("{Fuga} と {{Hoge}} と {Hoge");
        await Assert.That(project.Nodes[0].Notes).IsEqualTo("{Fuga} のメモ");
        await Assert.That(project.Nodes[0].Tags[0]).IsEqualTo("{Hoge}").Because("タグは対象外");
        await Assert.That(project.Nodes[0].BlockReason).IsEqualTo("{Hoge}").Because("ブロック理由は対象外");
    }

    [Test]
    [DisplayName("名前の交換でも連鎖置換しない")]
    public async Task PlanRename_SwapsWithoutChaining()
    {
        var project = ProjectWith(("{A} と {B}", string.Empty));
        var resolver = Resolver(("A", "あ"), ("B", "い"));

        ProjectVariableService.ApplyRename(ProjectVariableService.PlanRename(project, resolver,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = "B", ["B"] = "A" }));

        await Assert.That(project.Nodes[0].Title).IsEqualTo("{B} と {A}");
    }

    [Test]
    [DisplayName("改名したステップだけ UpdatedAt が進む")]
    public async Task ApplyRename_TouchesOnlyRewrittenNodes()
    {
        var project = ProjectWith(("{Hoge}", string.Empty), ("関係ない", string.Empty));
        var stale = DateTimeOffset.Now.AddDays(-3);
        foreach (var node in project.Nodes) node.UpdatedAt = stale;

        ProjectVariableService.ApplyRename(ProjectVariableService.PlanRename(project, Resolver(("Hoge", "値")),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Hoge"] = "Fuga" }));

        await Assert.That(project.Nodes[0].UpdatedAt).IsGreaterThan(stale);
        await Assert.That(project.Nodes[1].UpdatedAt).IsEqualTo(stale);
    }

    [Test]
    [DisplayName("未定義の名前を一覧できる")]
    public async Task FindUndefinedNames_ListsMissingDefinitions()
    {
        var project = ProjectWith(("{Fuga} と {Hoge}", "{Fuga}"));

        var undefined = ProjectVariableService.FindUndefinedNames(project, Resolver(("Hoge", "値")));

        await Assert.That(undefined.Count).IsEqualTo(1);
        await Assert.That(undefined[0].Name).IsEqualTo("Fuga");
        await Assert.That(undefined[0].Fields).IsEqualTo(2);
    }

    // ---- 保存・互換性 ----

    [Test]
    [DisplayName("変数を保存して読み直すと原文と定義がそのまま戻る")]
    public async Task SaveThenLoad_KeepsSourceAndDefinitions()
    {
        var project = ProjectWith(("{Hoge} の手順書を作成", "{Hoge} の設定を確認"));
        project.Variables.Add(new ProjectVariable { Name = "Hoge", Value = "なにかしらの固有名称" });
        var path = TempPath();

        try
        {
            new JsonProjectStore().Save(path, project);
            var loaded = new JsonProjectStore().Load(path);

            await Assert.That(loaded.SchemaVersion).IsEqualTo(TodoProject.CurrentSchemaVersion);
            await Assert.That(loaded.Variables.Count).IsEqualTo(1);
            await Assert.That(loaded.Variables[0].Value).IsEqualTo("なにかしらの固有名称");
            await Assert.That(loaded.Nodes[0].Title).IsEqualTo("{Hoge} の手順書を作成").Because("原文のまま");
            await Assert.That(ProjectVariableResolver.From(loaded).Expand(loaded.Nodes[0].Title))
                .IsEqualTo("なにかしらの固有名称 の手順書を作成");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("旧形式の波括弧は移行しても表示が変わらず、二重に移行しない")]
    public async Task Load_MigratesLegacyBracesOnce()
    {
        var path = TempPath();
        var legacy = """
            {
              "schemaVersion": 9,
              "name": "旧形式",
              "nodes": [ { "id": "11111111-1111-4111-8111-111111111111",
                           "title": "{Hoge} と {{Fuga}}", "notes": "} だけ" } ]
            }
            """;

        try
        {
            File.WriteAllText(path, legacy);
            var store = new JsonProjectStore();
            var loaded = store.Load(path);

            await Assert.That(loaded.Nodes[0].Title).IsEqualTo("{{Hoge}} と {{{{Fuga}}}}");
            await Assert.That(ProjectVariableResolver.From(loaded).Expand(loaded.Nodes[0].Title))
                .IsEqualTo("{Hoge} と {{Fuga}}").Because("移行しても見た目は同じ");

            store.Save(path, loaded);
            var again = store.Load(path);
            await Assert.That(again.Nodes[0].Title).IsEqualTo("{{Hoge}} と {{{{Fuga}}}}")
                .Because("形式10として保存したので二重に移行しない");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("形式9以下を名乗るのに変数を持つファイルは拒否し、元ファイルを残す")]
    public async Task Load_RejectsVersionMismatch()
    {
        var path = TempPath();
        var forged = """
            {
              "schemaVersion": 9,
              "name": "形式詐称",
              "variables": [ { "name": "Hoge", "value": "値" } ],
              "nodes": []
            }
            """;

        try
        {
            File.WriteAllText(path, forged);
            await Assert.That(() => new JsonProjectStore().Load(path)).Throws<InvalidDataException>();
            await Assert.That(File.ReadAllText(path)).IsEqualTo(forged).Because("元ファイルは書き換えない");
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("不正な定義を持つファイルは読み込みも保存も拒否する")]
    public async Task Store_RejectsInvalidDefinitions()
    {
        var path = TempPath();
        var broken = """
            {
              "schemaVersion": 10,
              "name": "壊れた定義",
              "variables": [ { "name": "Ho ge", "value": "値" } ],
              "nodes": []
            }
            """;

        try
        {
            File.WriteAllText(path, broken);
            await Assert.That(() => new JsonProjectStore().Load(path)).Throws<InvalidDataException>();

            var project = new TodoProject();
            project.Variables.Add(new ProjectVariable { Name = "Hoge", Value = "  " });
            await Assert.That(() => new JsonProjectStore().Save(TempPath(), project)).Throws<InvalidDataException>();
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Test]
    [DisplayName("部品の経路でも同じ検証と移行を通す")]
    public async Task TemplateStore_UsesSameValidationAndMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"todotree-tpl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(Path.Combine(directory, "legacy.template.json"), """
                {
                  "schemaVersion": 9,
                  "name": "旧形式の部品",
                  "nodes": [ { "id": "11111111-1111-4111-8111-111111111111", "title": "{Hoge}" } ]
                }
                """);
            File.WriteAllText(Path.Combine(directory, "forged.template.json"), """
                {
                  "schemaVersion": 9,
                  "name": "形式詐称の部品",
                  "variables": [ { "name": "Hoge", "value": "値" } ],
                  "nodes": [ { "id": "22222222-2222-4222-8222-222222222222", "title": "A" } ]
                }
                """);

            var (templates, errors) = new BranchTemplateStore(directory).LoadAll();

            await Assert.That(templates.Count).IsEqualTo(1);
            await Assert.That(templates[0].Nodes[0].Title).IsEqualTo("{{Hoge}}").Because("旧形式は逃がす");
            await Assert.That(errors.Count).IsEqualTo(1).Because("形式詐称は読み込まない");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [DisplayName("DeepClone した変数は元と共有しない")]
    public async Task DeepClone_CopiesVariables()
    {
        var project = new TodoProject();
        project.Variables.Add(new ProjectVariable { Name = "Hoge", Value = "元の値" });

        var clone = project.DeepClone();
        clone.Variables[0].Value = "書き換えた値";
        clone.Variables.Add(new ProjectVariable { Name = "Fuga", Value = "追加" });

        await Assert.That(project.Variables.Count).IsEqualTo(1);
        await Assert.That(project.Variables[0].Value).IsEqualTo("元の値");
    }

    [Test]
    [DisplayName("別プロジェクトの定義は参照しない")]
    public async Task Resolver_DoesNotLeakAcrossProjects()
    {
        var a = ProjectWith(("{Hoge}", string.Empty));
        a.Variables.Add(new ProjectVariable { Name = "Hoge", Value = "Aの値" });
        var b = ProjectWith(("{Hoge}", string.Empty));

        await Assert.That(ProjectVariableResolver.From(a).Expand(a.Nodes[0].Title)).IsEqualTo("Aの値");
        await Assert.That(ProjectVariableResolver.From(b).Expand(b.Nodes[0].Title)).IsEqualTo("{Hoge}");
    }

    // ---- 書き出し ----

    [Test]
    [DisplayName("書き出しは展開してから出力形式のエスケープを行う")]
    public async Task Export_ExpandsBeforeEscaping()
    {
        var project = ProjectWith(("{Hoge} の作業", string.Empty));
        project.Variables.Add(new ProjectVariable { Name = "Hoge", Value = "\"引用\" #[角括弧]" });

        var mermaid = GraphExporter.ToMermaid(project);

        await Assert.That(mermaid.Contains("#quot;引用#quot;")).IsTrue().Because("引用符を逃がす");
        await Assert.That(mermaid.Contains("#35;[角括弧]")).IsTrue().Because("シャープを逃がす");
        await Assert.That(mermaid.Contains("{Hoge}")).IsFalse().Because("原文は残さない");
        await Assert.That(GraphExporter.ToMarkdown(project).Contains("**\"引用\" #[角括弧] の作業**")).IsTrue();
    }

    // ---- 部品テンプレート ----

    [Test]
    [DisplayName("部品は参照している定義だけを持ち出す")]
    public async Task Capture_CopiesOnlyUsedDefinitions()
    {
        var project = ProjectWith(("{製品名} の梱包", "{未定義} を確認"));
        project.Variables.Add(new ProjectVariable { Name = "製品名", Value = "みかん箱" });
        project.Variables.Add(new ProjectVariable { Name = "未使用", Value = "使っていない" });

        var template = BranchTemplate.Capture(project, project.Nodes.Select(n => n.Id), "梱包の部品");

        await Assert.That(template.Variables.Select(v => v.Name)).IsEquivalentTo(new[] { "製品名" });
        await Assert.That(template.Nodes[0].Title).IsEqualTo("{製品名} の梱包").Because("原文のまま");
        await Assert.That(template.Nodes[0].Notes).IsEqualTo("{未定義} を確認").Because("未定義の参照も残す");
    }

    [Test]
    [DisplayName("部品の定義は挿入先に無いものだけ足し、値の食い違いは知らせる")]
    public async Task Insert_PrefersDestinationDefinitions()
    {
        var destination = new TodoProject();
        destination.Variables.Add(new ProjectVariable { Name = "製品名", Value = "りんご箱" });
        IReadOnlyList<ProjectVariable> incoming =
        [
            new ProjectVariable { Name = "製品名", Value = "みかん箱" },
            new ProjectVariable { Name = "担当", Value = "山田" },
        ];

        var missing = ProjectVariableService.MissingDefinitions(destination, incoming);
        var conflicts = ProjectVariableService.ConflictingDefinitions(destination, incoming);

        await Assert.That(missing.Select(v => v.Name)).IsEquivalentTo(new[] { "担当" });
        await Assert.That(conflicts.Count).IsEqualTo(1);
        await Assert.That(conflicts[0].DestinationValue).IsEqualTo("りんご箱").Because("挿入先を優先する");
        await Assert.That(conflicts[0].TemplateValue).IsEqualTo("みかん箱");
    }

    [Test]
    [DisplayName("変数を含む部品は JSON へ往復しても定義を保つ")]
    public async Task Template_RoundTripsThroughJson()
    {
        var project = ProjectWith(("{製品名} の梱包", string.Empty));
        project.Variables.Add(new ProjectVariable { Name = "製品名", Value = "みかん箱" });
        var directory = Path.Combine(Path.GetTempPath(), $"todotree-tpl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var store = new BranchTemplateStore(directory);
            store.Save(project, "梱包の部品");
            var (templates, errors) = store.LoadAll();

            await Assert.That(errors.Count).IsEqualTo(0);
            await Assert.That(templates[0].Variables[0].Value).IsEqualTo("みかん箱");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [DisplayName("形式10の JSON は variables を一覧として書き出す")]
    public async Task Save_WritesVariablesAsList()
    {
        var project = ProjectWith(("{Hoge}", string.Empty));
        project.Variables.Add(new ProjectVariable { Name = "Hoge", Value = "値" });
        var path = TempPath();

        try
        {
            new JsonProjectStore().Save(path, project);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var variables = document.RootElement.GetProperty("variables");

            await Assert.That(variables.ValueKind).IsEqualTo(JsonValueKind.Array);
            await Assert.That(variables[0].GetProperty("name").GetString()).IsEqualTo("Hoge");
            await Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32()).IsEqualTo(TodoProject.CurrentSchemaVersion);
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"todotree-{Guid.NewGuid():N}.json");

    private static void Cleanup(string path)
    {
        foreach (var candidate in new[] { path, path + ".bak", path + ".tmp" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
