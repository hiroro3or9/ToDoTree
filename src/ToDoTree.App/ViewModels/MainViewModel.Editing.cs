using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>編集操作・履歴・再描画まわり。</summary>
public sealed partial class MainViewModel
{
    /// <summary>ステップを追加する。<paramref name="sibling"/> が true なら選択中と同じ先行にぶら下げる。</summary>
    public NodeViewModel AddNode(NodeViewModel? anchor, bool sibling)
    {
        PushUndo();

        var model = new TodoNode { Title = "新しいステップ" };
        _graph.AddNode(model);
        var vm = new NodeViewModel(model, this);
        Nodes.Add(vm);
        _byId[model.Id] = vm;

        if (anchor is not null)
        {
            if (sibling)
            {
                foreach (var parent in _graph.ParentsOf(anchor.Id).ToList())
                {
                    _graph.Connect(parent.Id, model.Id);
                }
            }
            else
            {
                _graph.Connect(anchor.Id, model.Id);
            }
        }

        PlaceNear(model, anchor, sibling);
        vm.NotifyPositionChanged();
        RebuildEdges();
        SelectedNode = vm;
        MarkDirty();
        RefreshAll();

        EnsureVisibleRequested?.Invoke(this, vm);

        // 追加した直後は、そのカードの上でそのまま名前を打てる。
        BeginEdit(vm);
        StatusMessage = anchor is null
            ? "ステップを追加しました。"
            : sibling ? "同じ先行にぶら下げて追加しました。" : "続きのステップを追加しました。";
        return vm;
    }

    /// <summary>重ならない場所を探して置く。</summary>
    private void PlaceNear(TodoNode model, NodeViewModel? anchor, bool sibling)
    {
        var horizontal = Direction == LayoutDirection.LeftToRight;
        double x, y;

        if (anchor is null)
        {
            x = 80;
            y = 80;
        }
        else if (sibling)
        {
            x = horizontal ? anchor.X : anchor.X + NodeViewModel.CardWidth + 40;
            y = horizontal ? anchor.Y + NodeViewModel.CardHeight + 36 : anchor.Y;
        }
        else
        {
            x = horizontal ? anchor.X + NodeViewModel.CardWidth + 60 : anchor.X;
            y = horizontal ? anchor.Y : anchor.Y + NodeViewModel.CardHeight + 70;
        }

        for (var guard = 0; guard < 200 && Overlaps(x, y); guard++)
        {
            if (horizontal)
            {
                y += NodeViewModel.CardHeight + 26;
            }
            else
            {
                x += NodeViewModel.CardWidth + 26;
            }
        }

        model.X = x;
        model.Y = y;
    }

    private bool Overlaps(double x, double y) => Nodes.Any(n =>
        Math.Abs(n.X - x) < NodeViewModel.CardWidth * 0.75 &&
        Math.Abs(n.Y - y) < NodeViewModel.CardHeight * 0.95);

    public void DeleteSelected()
    {
        // 線が選ばれているときは、線だけを外す。
        if (HasSelectedEdge)
        {
            DeleteSelectedEdge();
            return;
        }

        var targets = SelectedNodes;
        if (targets.Count == 0)
        {
            return;
        }

        PushUndo();

        var removing = targets.Select(t => t.Id).ToHashSet();
        var fallback = targets
            .SelectMany(t => t.Parents.Concat(t.Children))
            .FirstOrDefault(n => !removing.Contains(n.Id));

        foreach (var node in targets)
        {
            _graph.RemoveNodeAndBridge(node.Id);
            Nodes.Remove(node);
            _byId.Remove(node.Id);
        }

        RebuildEdges();
        SelectOnly(fallback);
        MarkDirty();
        RefreshAll();

        StatusMessage = targets.Count == 1
            ? "削除しました（前後は繋ぎ直しました）。Ctrl+Z で戻せます。"
            : $"{targets.Count} 件を削除しました（前後は繋ぎ直しました）。Ctrl+Z で戻せます。";
    }

    public void ToggleDone()
    {
        var targets = SelectedNodes;
        if (targets.Count == 0)
        {
            return;
        }

        // 1 つでも未完了があれば「まとめて完了」、全部完了なら「まとめて戻す」。
        var toDone = targets.Any(n => n.Status != NodeStatus.Done);

        // 状態を変えると「次に動き出したステップ」が案内されるので、先に既定の文言を置いておく。
        StatusMessage = (toDone, targets.Count) switch
        {
            (true, 1) => "完了にしました。",
            (true, var count) => $"{count} 件を完了にしました。",
            (false, 1) => "未着手に戻しました。",
            (false, var count) => $"{count} 件を未着手に戻しました。",
        };

        SetStatusOfSelection(toDone ? NodeStatus.Done : NodeStatus.NotStarted);
    }

    public void AutoLayout()
    {
        PushUndo();
        LayeredLayoutEngine.Apply(_graph, new LayoutOptions { Direction = Direction });

        foreach (var node in Nodes)
        {
            node.NotifyPositionChanged();
        }

        MarkDirty();
        NotifyVisualsChanged();
        ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
        StatusMessage = "自動整列しました（位置を固定したステップはそのままです）。";
    }

    public void ToggleDirection()
    {
        Direction = Direction == LayoutDirection.LeftToRight
            ? LayoutDirection.TopToBottom
            : LayoutDirection.LeftToRight;

        _settings.Direction = Direction;
        _settings.Save();

        OnPropertyChanged(nameof(Direction));
        OnPropertyChanged(nameof(DirectionLabel));
        AutoLayout();
    }

    /// <summary>キャンバス上でドラッグして繋いだときに呼ばれる。</summary>
    public bool TryConnect(Guid fromId, Guid toId)
    {
        var check = _graph.CanConnect(fromId, toId);
        if (!check.IsOk())
        {
            StatusMessage = check.ToMessage();
            return false;
        }

        PushUndo();
        _graph.Connect(fromId, toId);
        RebuildEdges();
        MarkDirty();
        RefreshAll();
        StatusMessage = "繋ぎました。";
        return true;
    }

    private void RemoveLink(NodeViewModel? other, bool isParent)
    {
        if (other is null || SelectedNode is not { } node)
        {
            return;
        }

        PushUndo();
        if (isParent)
        {
            _graph.Disconnect(other.Id, node.Id);
        }
        else
        {
            _graph.Disconnect(node.Id, other.Id);
        }

        RebuildEdges();
        MarkDirty();
        RefreshAll();
        StatusMessage = "繋がりを外しました。";
    }

    /// <summary>ドラッグ開始時に 1 回だけ履歴を取る。</summary>
    public void BeginNodeDrag() => PushUndo();

    public void RequestRenameFocus() => BeginEdit(SelectedNode);

    // ---- 履歴 ----

    /// <summary>
    /// 変更前の状態を控えておく。<paramref name="key"/> が同じ操作が短時間に続くときは 1 つにまとめる
    /// （文字入力のたびに履歴が積み上がらないように）。
    /// </summary>
    public void PushUndo(string? key = null)
    {
        var now = DateTime.UtcNow;
        if (key is not null && key == _lastUndoKey && (now - _lastUndoAt).TotalSeconds < 2)
        {
            _lastUndoAt = now;
            return;
        }

        _lastUndoKey = key ?? Guid.NewGuid().ToString("N");
        _lastUndoAt = now;

        _undo.Add(_project.DeepClone());
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(_project.DeepClone());

        var selectedId = SelectedNode?.Id;
        LoadProject(snapshot, _filePath, selectedId);
        IsDirty = true;
        _lastUndoKey = string.Empty;
        StatusMessage = "元に戻しました。";
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(_project.DeepClone());

        var selectedId = SelectedNode?.Id;
        LoadProject(snapshot, _filePath, selectedId);
        IsDirty = true;
        _lastUndoKey = string.Empty;
        StatusMessage = "やり直しました。";
    }

    // ---- 再描画 ----

    public void MarkDirty() => IsDirty = true;

    public void NotifyVisualsChanged() => VisualsChanged?.Invoke(this, EventArgs.Empty);

    public void RefreshAll()
    {
        foreach (var node in Nodes)
        {
            node.RefreshDerived();
        }

        RefreshTags();
        RefreshVisibility();
        UpdateHighlights();
        RefreshSidebar();
        RefreshPlanning();
        Progress = _graph.Progress();
        NotifyVisualsChanged();
    }

    private void RebuildEdges()
    {
        Edges.Clear();
        foreach (var edge in _graph.Edges)
        {
            if (_byId.TryGetValue(edge.FromId, out var from) && _byId.TryGetValue(edge.ToId, out var to))
            {
                Edges.Add(new EdgeViewModel(edge, from, to));
            }
        }

        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        var related = new HashSet<Guid>();
        if (SelectedNode is { } selected)
        {
            related.UnionWith(_graph.Ancestors(selected.Id));
            related.UnionWith(_graph.Descendants(selected.Id));
        }

        var searching = !string.IsNullOrWhiteSpace(SearchText) || !string.IsNullOrEmpty(SelectedTag);

        foreach (var node in Nodes)
        {
            node.IsRelated = related.Contains(node.Id);
            node.IsDimmed = searching && !Matches(node);
        }

        foreach (var edge in Edges)
        {
            edge.IsHighlighted = SelectedNode is { } current &&
                                 (edge.From.Id == current.Id || edge.To.Id == current.Id);
        }
    }

    public void RefreshSidebar()
    {
        var desired = Nodes
            .Where(n => Matches(n) && (!HideCompleted || !n.Model.IsSettled))
            .OrderBy(n => n.GroupOrder)
            .ThenBy(n => n.Title, StringComparer.CurrentCulture)
            .ToList();

        _rebuildingSidebar = true;
        try
        {
            for (var i = SidebarNodes.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(SidebarNodes[i]))
                {
                    SidebarNodes.RemoveAt(i);
                }
            }

            for (var i = 0; i < desired.Count; i++)
            {
                var item = desired[i];
                var current = SidebarNodes.IndexOf(item);
                if (current < 0)
                {
                    SidebarNodes.Insert(i, item);
                }
                else if (current != i)
                {
                    SidebarNodes.Move(current, i);
                }
            }
        }
        finally
        {
            _rebuildingSidebar = false;
        }
    }

    private bool Matches(NodeViewModel node)
    {
        // タグで絞り込んでいるときは、そのタグを持つものだけ。
        if (!string.IsNullOrEmpty(SelectedTag) && !node.Model.Tags.Contains(SelectedTag))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var needle = SearchText.Trim();
        return node.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || node.Model.Notes.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
               || node.Model.Tags.Any(t => t.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
    }

    public IReadOnlyList<NodeViewModel> ParentsOf(NodeViewModel node) => [.. _graph
        .ParentsOf(node.Id)
        .Select(m => _byId.TryGetValue(m.Id, out var vm) ? vm : null)
        .Where(vm => vm is not null)
        .Select(vm => vm!)];

    public IReadOnlyList<NodeViewModel> ChildrenOf(NodeViewModel node) => [.. _graph
        .ChildrenOf(node.Id)
        .Select(m => _byId.TryGetValue(m.Id, out var vm) ? vm : null)
        .Where(vm => vm is not null)
        .Select(vm => vm!)];

    /// <summary>追加したステップが画面の外なら見える位置まで動かしてほしい。</summary>
    public event EventHandler<NodeViewModel>? EnsureVisibleRequested;
}
