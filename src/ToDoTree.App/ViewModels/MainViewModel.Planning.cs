using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ToDoTree.App.Views;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;
using ToDoTree.Core.Text;

namespace ToDoTree.App.ViewModels;

/// <summary>「次に何をやるか」を助ける部分：優先順位、最長経路、期限の逆算、自動保存。</summary>
public sealed partial class MainViewModel
{
    private bool _showCriticalPath;

    /// <summary>いま手を付けるべき上位のステップ。</summary>
    public ObservableCollection<NextActionViewModel> NextActions { get; } = [];

    public ICommand ImportOutlineCommand { get; private set; } = null!;

    public ICommand ToggleCriticalPathCommand { get; private set; } = null!;

    /// <summary>最長経路（ここが遅れると全体が遅れる鎖）を強調する。</summary>
    public bool ShowCriticalPath
    {
        get => _showCriticalPath;
        set
        {
            if (!SetProperty(ref _showCriticalPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CriticalPathLabel));
            RefreshPlanning();
            NotifyVisualsChanged();
            StatusMessage = value
                ? "最長経路を強調表示しています。ここが遅れると全体が遅れます。"
                : "最長経路の強調を解除しました。";
        }
    }

    public string CriticalPathLabel => ShowCriticalPath ? "経路を隠す" : "最長経路";

    public bool HasNextActions => NextActions.Count > 0;

    private void InitializePlanning()
    {
        ImportOutlineCommand = new RelayCommand(ImportOutline);
        ToggleCriticalPathCommand = new RelayCommand(() => ShowCriticalPath = !ShowCriticalPath);
        ExportCommand = new RelayCommand(Export);
        CopyMermaidCommand = new RelayCommand(CopyMermaid);
        SplitCommand = new RelayCommand(SplitStep, () => SelectedNode is not null);
    }

    /// <summary>最長経路・期限の逆算・次にやることをまとめて計算し直す。</summary>
    private void RefreshPlanning()
    {
        var criticalOrder = ShowCriticalPath
            ? Graph.CriticalPath().Select(n => n.Id).ToList()
            : [];

        var criticalNodes = criticalOrder.ToHashSet();
        var criticalEdges = new HashSet<Guid>();

        for (var i = 0; i + 1 < criticalOrder.Count; i++)
        {
            var edge = Graph.OutgoingOf(criticalOrder[i]).FirstOrDefault(e => e.ToId == criticalOrder[i + 1]);
            if (edge is not null)
            {
                criticalEdges.Add(edge.Id);
            }
        }

        var schedule = ScheduleAnalysis.Compute(Graph);

        foreach (var node in Nodes)
        {
            node.IsOnCriticalPath = criticalNodes.Contains(node.Id);
            node.Schedule = schedule.TryGetValue(node.Id, out var info) ? info : null;
        }

        foreach (var edge in Edges)
        {
            edge.IsOnCriticalPath = criticalEdges.Contains(edge.Model.Id);
        }

        NextActions.Clear();
        var rank = 1;
        foreach (var action in NextActionPlanner.Suggest(Graph, count: 3))
        {
            if (_byId.TryGetValue(action.Node.Id, out var vm))
            {
                NextActions.Add(new NextActionViewModel(rank++, vm, action.Reason));
            }
        }

        UpdateForecast();
        OnPropertyChanged(nameof(HasNextActions));
    }

    /// <summary>箇条書きをまとめて取り込む。</summary>
    private void ImportOutline()
    {
        var dialog = new OutlineInputWindow();
        if (Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var items = OutlineParser.Parse(dialog.OutlineText);
        if (items.Count == 0)
        {
            StatusMessage = "取り込めるステップが見つかりませんでした。";
            return;
        }

        var anchorId = SelectedNode?.Id;

        PushUndo();

        var created = OutlineImporter.Import(_graph, items, anchorId);
        AbsorbCreated(created, selectFirst: true, anchorId);
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
        StatusMessage = $"{created.Count} 個のステップを取り込みました。Ctrl+Z で元に戻せます。";
    }

    /// <summary>
    /// まとめて作られたステップを画面に取り込み、並べ直す。
    /// <paramref name="anchorId"/> がブロックの中にあるときは、作られたステップもその所属を引き継ぐ。
    /// </summary>
    internal void AbsorbCreated(IReadOnlyList<TodoNode> created, bool selectFirst, Guid? anchorId = null)
    {
        foreach (var model in created)
        {
            var vm = new NodeViewModel(model, this);
            Nodes.Add(vm);
            _byId[model.Id] = vm;
        }

        // これから中に入るステップを、その囲みの外へ弾かないようにする。
        // 所属を先に付けてしまうと今度は固定対象になって並ばないので、整列 → 所属の順にする。
        var adopting = anchorId is { } anchor ? BlockOf(anchor)?.Id : null;
        LayeredLayoutEngine.Apply(_graph, BuildLayoutOptions(adopting));

        foreach (var node in Nodes)
        {
            node.NotifyPositionChanged();
        }

        RebuildEdges();
        InheritBlock(anchorId, created.Select(n => n.Id));

        if (selectFirst && created.Count > 0 && _byId.TryGetValue(created[0].Id, out var first))
        {
            SelectedNode = first;
        }

        MarkDirty();
        RefreshAll();
    }

    /// <summary>矢印キーで、その向きにある一番近いステップへ移る。</summary>
    public void MoveSelection(MoveDirection direction)
    {
        if (SelectedNode is not { } current)
        {
            SelectedNode = Nodes.FirstOrDefault();
            return;
        }

        if (Navigation.FindNeighbor(_graph, current.Id, direction) is not { } neighbor)
        {
            return;
        }

        if (_byId.TryGetValue(neighbor.Id, out var vm))
        {
            SelectedNode = vm;
            EnsureVisibleRequested?.Invoke(this, vm);
        }
    }

    /// <summary>完了した瞬間に、新しく動き出したステップを教える。</summary>
    public void AnnounceUnlocked(NodeViewModel node)
    {
        if (node.Model.Status != NodeStatus.Done)
        {
            return;
        }

        var unlocked = _graph.ChildrenOf(node.Id)
            .Where(child => _graph.ReadinessOf(child) == Readiness.Ready)
            .ToList();

        if (unlocked.Count == 0)
        {
            return;
        }

        StatusMessage = unlocked.Count == 1
            ? $"「{unlocked[0].Title}」が着手できるようになりました。"
            : $"「{unlocked[0].Title}」ほか {unlocked.Count - 1} 件が着手できるようになりました。";
    }

    // ---- 自動保存 ----

    public string RecoveryFilePath => Path.Combine(_recoveryDirectory, $"{DocumentId:N}.todotree.json");

    /// <summary>ワークスペースの共通タイマーから呼び出される。</summary>
    public void AutoSave()
    {
        // 見出しのドラッグや命名の途中では書き出さない（確定前の座標や名前を残さない）。
        if (IsBlockEditing || !IsDirty)
        {
            return;
        }

        try
        {
            if (Procedure?.SavePendingDetails() == false) return;
            if (!string.IsNullOrEmpty(_filePath))
            {
                var stored = PrepareStorageProject();
                _store.Save(_filePath, stored);
                AcceptStorageProject(stored);
                IsDirty = false;
                StatusMessage = $"自動保存しました（{DateTime.Now:HH:mm}）。";
            }
            else
            {
                // 未保存タブ同士が上書きし合わないよう、タブごとに退避する。
                var stored = PrepareStorageProject();
                _store.Save(RecoveryFilePath, stored);
                AcceptStorageProject(stored);
            }
        }
        catch (Exception ex)
        {
            // 自動保存に失敗しても作業は止めない。
            if (IsProcedure) StatusMessage = $"自動保存失敗・変更は未保存です: {ex.Message}";
        }
    }

    public void DeleteRecoveryFile()
    {
        try
        {
            if (File.Exists(RecoveryFilePath))
            {
                File.Delete(RecoveryFilePath);
            }
        }
        catch
        {
            // 復旧用ファイルを消せなくても、編集中の作業は止めない。
        }
    }
}
