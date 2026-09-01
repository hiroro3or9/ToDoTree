using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.Core.Storage;

/// <summary>初回起動時に見せる、分岐と合流のあるサンプル。</summary>
public static class SampleProject
{
    public static TodoProject Create()
    {
        var project = new TodoProject
        {
            Name = "アプリをリリースする",
            Description = "スタートからゴールまでを細かいステップに割って、分岐と合流で管理する例です。",
        };

        var graph = new TodoGraph(project);

        TodoNode Add(string title, NodeKind kind = NodeKind.Step, NodeStatus status = NodeStatus.NotStarted, int? estimate = null)
        {
            var node = new TodoNode { Title = title, Kind = kind, Status = status, EstimateMinutes = estimate };
            if (status == NodeStatus.Done)
            {
                node.CompletedAt = DateTimeOffset.Now;
            }

            return graph.AddNode(node);
        }

        var idea = Add("アイデアを決める", NodeKind.Start, NodeStatus.Done, 60);
        var requirements = Add("やることを書き出す", NodeKind.Step, NodeStatus.Done, 90);
        var screens = Add("画面のラフを描く", NodeKind.Step, NodeStatus.InProgress, 120);
        var data = Add("データ構造を決める", NodeKind.Step, NodeStatus.NotStarted, 90);
        var environment = Add("開発環境を用意する", NodeKind.Step, NodeStatus.Done, 45);
        var ui = Add("画面を実装する", NodeKind.Step, NodeStatus.NotStarted, 480);
        var storage = Add("保存機能を実装する", NodeKind.Step, NodeStatus.NotStarted, 240);
        var integrate = Add("つないで動作確認", NodeKind.Milestone, NodeStatus.NotStarted, 180);
        var icon = Add("アイコンを作る", NodeKind.Step, NodeStatus.NotStarted, 120);
        var shots = Add("紹介用のスクショを撮る", NodeKind.Step, NodeStatus.NotStarted, 60);
        var release = Add("公開する", NodeKind.Goal, NodeStatus.NotStarted, 60);

        graph.Connect(idea.Id, requirements.Id);
        graph.Connect(requirements.Id, screens.Id);
        graph.Connect(requirements.Id, data.Id);
        graph.Connect(requirements.Id, icon.Id);
        graph.Connect(idea.Id, environment.Id);

        // 分岐した作業がここで合流する。
        graph.Connect(screens.Id, ui.Id);
        graph.Connect(environment.Id, ui.Id);
        graph.Connect(data.Id, storage.Id);
        graph.Connect(environment.Id, storage.Id);

        graph.Connect(ui.Id, integrate.Id);
        graph.Connect(storage.Id, integrate.Id);

        graph.Connect(integrate.Id, shots.Id);
        graph.Connect(icon.Id, shots.Id);
        graph.Connect(shots.Id, release.Id);
        graph.Connect(integrate.Id, release.Id);

        LayeredLayoutEngine.Apply(graph);
        return project;
    }
}
