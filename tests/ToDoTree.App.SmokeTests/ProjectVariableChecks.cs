using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToDoTree.App;
using ToDoTree.App.Services;
using ToDoTree.App.ViewModels;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Models;
using ToDoTree.Core.Storage;

internal static partial class Program
{
    private static void VerifyProjectVariables()
    {
        var project = new TodoProject
        {
            Name = "変数の確認",
            Nodes =
            [
                new() { Title = "{製品名} の手順書を作成", Notes = "{製品名} の設定を確認", X = 60, Y = 90 },
                new() { Title = "{未定義} を調べる", X = 400, Y = 90 },
                new() { Title = "あとかたづけ", X = 740, Y = 90 },
            ],
            Variables = [new ProjectVariable { Name = "製品名", Value = "みかん箱" }],
        };
        var vm = new MainViewModel(new JsonProjectStore(), new AppSettings(), project, null, AppContext.BaseDirectory);
        var withVariable = vm.Nodes[0];
        var withUndefined = vm.Nodes[1];

        Check(withVariable.Title == "{製品名} の手順書を作成", "Editing keeps the source text.");
        Check(withVariable.DisplayTitle == "みかん箱 の手順書を作成", "Display expands the reference.");
        Check(withVariable.DisplayNotes == "みかん箱 の設定を確認", "Notes expand too.");
        Check(!withVariable.HasUndefinedVariables && withUndefined.HasUndefinedVariables,
            "Only the card with a missing definition is flagged.");
        Check(withUndefined.UndefinedVariableText == "未定義: 未定義", "The missing name is named in text, not colour alone.");
        Check(withVariable.CardTooltip.Contains("原文：{製品名}") && withVariable.CardTooltip.Contains("変数: 製品名"),
            "Tooltip shows the source text and the variables in use.");

        // ---- 検索は原文でも表示値でも当たる ----
        vm.SearchText = "製品名";
        Check(vm.SidebarNodes.Contains(withVariable), "Searching the variable name finds the step.");
        vm.SearchText = "{製品名}";
        Check(vm.SidebarNodes.Contains(withVariable), "Searching the reference finds the step.");
        vm.SearchText = "みかん箱";
        Check(vm.SidebarNodes.Contains(withVariable), "Searching the value finds the step.");
        Check(!vm.SidebarNodes.Contains(vm.Nodes[2]), "A step that does not reference it is not matched by the value.");
        vm.SearchText = string.Empty;

        // ---- 値を変えると、完了済みも含めて表示だけが変わる ----
        vm.Nodes[0].Status = NodeStatus.Done;
        var dirtyBefore = vm.IsDirty;
        var undoBefore = History(vm, "_undo");
        Check(vm.ApplyProjectVariables(vm.CopyVariables(),
            new Dictionary<string, string>(StringComparer.Ordinal)) is null, "Saving the same content is accepted.");
        Check(History(vm, "_undo") == undoBefore && vm.IsDirty == dirtyBefore,
            "A no-change save adds neither history nor an unsaved mark.");

        var updatedAt = withVariable.Model.UpdatedAt;
        var completedAt = withVariable.Model.CompletedAt;
        Check(vm.ApplyProjectVariables([new ProjectVariable { Name = "製品名", Value = "りんご箱" }],
            new Dictionary<string, string>(StringComparer.Ordinal)) is null, "Changing a value is accepted.");
        Check(vm.Nodes[0].DisplayTitle == "りんご箱 の手順書を作成", "Completed steps show the new value too.");
        Check(vm.Nodes[0].Title == "{製品名} の手順書を作成", "The source text is untouched.");
        Check(vm.Nodes[0].Model.UpdatedAt == updatedAt && vm.Nodes[0].Model.CompletedAt == completedAt,
            "A value change does not touch UpdatedAt or the completion time.");

        // ---- 改名は有効な参照だけを書き換え、Undo で定義と原文が一緒に戻る ----
        Check(vm.ApplyProjectVariables([new ProjectVariable { Name = "商品名", Value = "りんご箱" }],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["製品名"] = "商品名" }) is null, "Renaming is accepted.");
        Check(vm.Nodes[0].Title == "{商品名} の手順書を作成" && vm.Nodes[0].Notes == "{商品名} の設定を確認",
            "Renaming rewrites the source of every valid reference.");
        vm.Undo();
        Check(vm.Nodes[0].Title == "{製品名} の手順書を作成"
            && vm.Graph.Project.Variables.Single().Name == "製品名",
            "One undo restores the definition and the source together.");
        vm.Redo();
        Check(vm.Nodes[0].Title == "{商品名} の手順書を作成" && vm.Nodes[0].DisplayTitle == "りんご箱 の手順書を作成",
            "Redo reapplies both.");

        // ---- 削除しても参照の原文は残り、未定義として示す ----
        Check(vm.ApplyProjectVariables([], new Dictionary<string, string>(StringComparer.Ordinal)) is null,
            "Deleting a definition in use is accepted.");
        Check(vm.Nodes[0].Title == "{商品名} の手順書を作成" && vm.Nodes[0].DisplayTitle == "{商品名} の手順書を作成"
            && vm.Nodes[0].HasUndefinedVariables, "The reference stays in the source and becomes undefined.");
        Check(vm.ApplyProjectVariables([new ProjectVariable { Name = "商品名", Value = "りんご箱" }],
            new Dictionary<string, string>(StringComparer.Ordinal)) is null, "Re-adding is accepted.");
        Check(vm.Nodes[0].DisplayTitle == "りんご箱 の手順書を作成", "Re-adding resolves the reference again.");

        // ---- 不正な定義は拒否し、モデルを動かさない ----
        var before = State(vm);
        Check(vm.ApplyProjectVariables([new ProjectVariable { Name = "商 品名", Value = "値" }],
            new Dictionary<string, string>(StringComparer.Ordinal)) is not null, "An invalid name is rejected.");
        Check(State(vm) == before, "A rejected save leaves the project untouched.");

        // ---- 受信箱は原文を保ったまま配置する ----
        vm.InboxTitle = "{商品名} の在庫を数える";
        vm.AddInboxCommand.Execute(null);
        var inbox = vm.InboxItems.Single();
        Check(inbox.Title == "{商品名} の在庫を数える" && inbox.DisplayTitle == "りんご箱 の在庫を数える",
            "The inbox keeps the source and shows the value.");
        Check(vm.PlaceInboxItem(inbox.Id, 60, 320), "Inbox placement works.");
        Check(vm.Nodes.Last().Title == "{商品名} の在庫を数える", "Placing an inbox item carries the source text over.");

        // ---- 部品は使っている定義だけを連れていき、挿入先の値を優先する ----
        var template = BranchTemplate.Capture(vm.Graph.Project, [vm.Nodes[0].Id], "手順書の部品");
        Check(template.Variables.Single().Name == "商品名", "The template carries only the definition it uses.");
        var destination = new MainViewModel(new JsonProjectStore(), new AppSettings(),
            new TodoProject { Name = "別のプロジェクト", Nodes = [new() { Title = "起点", X = 40, Y = 40 }] },
            null, AppContext.BaseDirectory);
        var undoCount = History(destination, "_undo");
        destination.InsertTemplate(template, 400, 40);
        Check(History(destination, "_undo") == undoCount + 1, "Definitions and nodes arrive in one undo unit.");
        Check(destination.Graph.Project.Variables.Single().Value == "りんご箱"
            && destination.Nodes.Last().DisplayTitle == "りんご箱 の手順書を作成",
            "The missing definition comes along with the part.");
        destination.Undo();
        Check(destination.Graph.Project.Variables.Count == 0 && destination.Nodes.Count == 1,
            "One undo removes both the definition and the nodes.");

        var kept = new MainViewModel(new JsonProjectStore(), new AppSettings(),
            new TodoProject
            {
                Name = "同名の定義がある",
                Nodes = [new() { Title = "起点", X = 40, Y = 40 }],
                Variables = [new ProjectVariable { Name = "商品名", Value = "ぶどう箱" }],
            }, null, AppContext.BaseDirectory);
        kept.InsertTemplate(template, 400, 40);
        Check(kept.Graph.Project.Variables.Single().Value == "ぶどう箱",
            "An existing definition wins over the one in the part.");

        // ---- 画面 ----
        var window = new MainWindow(new TaskDetailsWorkspace(vm))
        {
            DataContext = new TaskDetailsWorkspace(vm),
            Width = 1500, Height = 1100, ShowInTaskbar = false, ShowActivated = false, Opacity = 0,
        };
        window.Show();
        vm.SelectedNode = vm.Nodes[0];
        window.UpdateLayout();

        var titleBox = (TextBox)window.FindName("TitleBox")!;
        Check(titleBox.Text == "{商品名} の手順書を作成", "The detail panel edits the source text.");
        Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text == "りんご箱 の手順書を作成"),
            "The detail panel previews the expanded title.");

        RenderTaskDetails(window, "variables-light");
        ThemeManager.Apply(AppTheme.Dark);
        vm.SelectedNode = vm.Nodes[1];
        vm.RefreshAll();
        window.UpdateLayout();
        RenderTaskDetails(window, "variables-dark");
        ThemeManager.Apply(AppTheme.Light);
        vm.RefreshAll();

        VerifyProjectVariablesWindow(vm, window);
        window.Close();
    }

    /// <summary>設定画面そのもの。下書きの検証・改名・削除と、明暗両テーマの描画を見る。</summary>
    private static void VerifyProjectVariablesWindow(MainViewModel vm, Window owner)
    {
        var untouched = State(vm);
        var dialog = new ProjectVariablesWindow(vm.Graph.Project, vm.CopyVariables())
        {
            Owner = owner, ShowInTaskbar = false, ShowActivated = false, Opacity = 0,
        };
        dialog.Show();
        dialog.UpdateLayout();

        var draft = (ProjectVariablesViewModel)dialog.DataContext;
        var row = draft.Rows.Single();
        Check(row.Name == "商品名" && row.UsageCount == 3, "The row shows the saved name and its usage count.");
        Check(draft.Usages.Count == 3 && draft.Usages.Any(u => u.Occurrences >= 1),
            "Usages are listed per field, with the occurrence count in the detail.");
        Check(draft.Undefined.Single().Name == "未定義", "Undefined references are listed separately.");

        row.Name = "商 品名";
        Check(!draft.CanSave && draft.ValidationMessage.Length > 0, "An invalid name blocks saving.");
        row.Name = "型番";
        Check(draft.CanSave && row.IsRenamed && draft.Renames["商品名"] == "型番", "A rename is tracked against the saved name.");
        Check(draft.Usages.All(u => u.IsChanged == false || u.After.Contains("りんご箱")),
            "The preview keeps resolving the renamed reference.");

        draft.Remove(row);
        Check(row.IsDeleted && row.StateText.Contains("未定義になります"),
            "Deleting a definition in use shows the effect before saving.");
        // 削除した行の改名は起きないので、原文は {商品名} のまま残って未定義になる。
        Check(draft.Renames.Count == 0 && draft.Undefined.Any(u => u.Name == "商品名"),
            "Deleting a renamed row drops the rename and leaves the original reference undefined.");
        draft.Remove(row);
        Check(!row.IsDeleted, "Deletion can be taken back inside the draft.");

        draft.AddCommand.Execute(null);
        Check(draft.Rows.Count == 2 && draft.CanSave, "A new row starts valid.");
        draft.Rows[1].Name = "型番";
        Check(!draft.CanSave, "A duplicate name blocks saving.");
        draft.Remove(draft.Rows[1]);
        Check(draft.Rows.Count == 1, "A row added in the draft is dropped outright.");

        Check(State(vm) == untouched, "Editing the draft never touches the project.");

        draft.SelectedRow = draft.Rows[0];
        Check(draft.UsageHeader.Contains("型番") && draft.Usages.Count == 3,
            "Selecting a row lists its usages under the new name.");
        dialog.UpdateLayout();
        RenderTaskDetails(dialog, "variables-window-light");
        ThemeManager.Apply(AppTheme.Dark);
        dialog.UpdateLayout();
        RenderTaskDetails(dialog, "variables-window-dark");
        ThemeManager.Apply(AppTheme.Light);
        dialog.Close();
    }
}
