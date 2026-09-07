using System.Collections.ObjectModel;
using System.Windows.Input;
using ToDoTree.App.Services;
using ToDoTree.Core.Graph;
using ToDoTree.Core.Layout;
using ToDoTree.Core.Models;

namespace ToDoTree.App.ViewModels;

/// <summary>履歴に残す「そのとき何を選んでいたか」。プロジェクト JSON には含めない。</summary>
internal readonly record struct SelectionState(bool IsBlock, Guid[] Ids)
{
    public static SelectionState Empty => new(false, []);
}

/// <summary>
/// Undo / Redo の 1 手。
/// 以前は選択中のステップ 1 件しか復元できず、ブロック解除を戻したときに
/// 何が選ばれているべきかを表せなかったので、選択の種別と ID をここに持たせている。
/// </summary>
internal sealed record HistoryEntry(TodoProject Project, SelectionState Selection);

/// <summary>
/// ブロック（囲み）まわり：作成・命名・選択・移動・所属編集・解除。
///
/// ここでの操作はどれも依存関係に触れない。ノードも辺も作らず消さないので、
/// 着手可能判定・進捗・最長経路・完了予測は前後で必ず一致する。
///
/// 履歴は <see cref="PushUndo"/> ではなく「変更前スナップショットを一時保持して、
/// 確定時に差分があるときだけ積む」方式にしている。ドラッグ開始で履歴を積んでしまうと、
/// 取り消したときに Redo が失われてしまうため。
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<Guid, BlockViewModel> _blockById = [];
    private readonly Dictionary<Guid, BlockViewModel> _blockOfNode = [];

    private BlockViewModel? _selectedBlock;

    // ---- 編集中の操作（作成＋命名 / 名前変更 / 移動）----
    private TodoProject? _pendingSnapshot;
    private SelectionState _pendingSelection = SelectionState.Empty;
    private bool _pendingDirty;

    // ---- 見出しドラッグ ----
    private BlockViewModel? _draggingBlock;
    private List<(NodeViewModel Node, double X, double Y)> _dragOrigins = [];
    private List<(TodoEdge Edge, JunctionPoint[] Points)> _dragWaypoints = [];

    // ---- 中の整列 ----

    /// <summary>
    /// 整列の入口。ブロックを選んでいるときは中だけ、それ以外は従来どおり全体を並べる。
    /// ボタンの文言と Ctrl+L の行き先を 1 か所にまとめ、押す前に何が起きるか分かるようにしている。
    /// </summary>
    public void Align()
    {
        if (IsNaming)
        {
            StatusMessage = "名前の入力を終えてから整列できます。";
            return;
        }

        if (HasSelectedBlock)
        {
            LayoutSelectedBlock();
            return;
        }

        AutoLayout();
    }

    /// <summary>「中を整列」が使えない理由。使えるなら null。</summary>
    private string? BlockLayoutBlockedReason(BlockViewModel? block)
    {
        if (block is null)
        {
            return "整列したいブロックの見出しをクリックしてください。";
        }

        if (IsNaming || IsConnecting)
        {
            return "編集を終えてから整列できます。";
        }

        if (block.TotalCount < BlockService.MinimumSize)
        {
            return $"{BlockService.MinimumSize} 件以上のステップが必要です。";
        }

        // 隠れているカードを黙って動かすと、開いたときに「知らないうちに動いた」ことになる。
        if (block.VisibleCount != block.TotalCount)
        {
            return "すべてのステップを表示すると整列できます。";
        }

        // 固定を一時的に外して並べると、手で留めた意図を壊す。初版は断る側に倒す。
        // ここはメニューの可否として何度も評価されるので、確保も列挙も最小限にする。
        foreach (var id in block.Model.NodeIds)
        {
            if (_byId.TryGetValue(id, out var node) && node.Model.IsPinned)
            {
                return "中のステップの位置固定を解除すると整列できます。";
            }
        }

        return null;
    }

    /// <summary>
    /// 選んでいるブロックの中だけを、依存関係に沿って並べ直す。
    ///
    /// 見出しの位置・外のカード・選択・拡大率と画面位置はどれも動かさない。
    /// 「散らかったから直す」だけの操作なので、直したあとにそのまま編集を続けられることを優先する。
    /// </summary>
    public void LayoutSelectedBlock()
    {
        if (_selectedBlock is not { } block)
        {
            StatusMessage = "整列したいブロックの見出しをクリックしてください。";
            return;
        }

        // メニューを開いてから状況が変わっていることがあるので、実行時にもう一度見る。
        if (BlockLayoutBlockedReason(block) is { } reason)
        {
            StatusMessage = reason;
            return;
        }

        var result = BlockLayoutService.Compute(_project, block.Id, NodeMetrics.LayoutFor(Direction));

        if (!result.IsReady)
        {
            // 失敗と無変化では、履歴・Redo・未保存の印・選択のどれも変えない。
            StatusMessage = MessageFor(result.Status, block);
            return;
        }

        BeginTransaction();

        try
        {
            foreach (var (id, position) in result.Positions)
            {
                if (_byId.TryGetValue(id, out var node))
                {
                    node.Model.X = position.X;
                    node.Model.Y = position.Y;
                    node.NotifyPositionChanged();
                }
            }
        }
        catch (Exception ex)
        {
            RollbackTransaction();
            StatusMessage = $"整列できませんでした（{ex.Message}）。";
            return;
        }

        // 動いたカードは、繋がっていない線にとっても障害物が動いたことになる。
        foreach (var edge in Edges)
        {
            edge.InvalidateRoute();
        }

        var changed = CommitTransaction();
        NotifyVisualsChanged();

        // 全体表示は出さない。拡大率と見ている場所はそのままにして、続けて編集できるようにする。
        FocusCanvasRequested?.Invoke(this, EventArgs.Empty);

        StatusMessage = changed
            ? $"「{block.Title}」の中を整列しました。手動の通過点は保持しています。"
            : $"「{block.Title}」の中はすでに整列されています。";
    }

    private static string MessageFor(BlockLayoutStatus status, BlockViewModel block) => status switch
    {
        BlockLayoutStatus.Unchanged => $"「{block.Title}」の中はすでに整列されています。",
        BlockLayoutStatus.TooFewNodes => $"{BlockService.MinimumSize} 件以上のステップが必要です。",
        BlockLayoutStatus.ContainsPinnedNodes => "中のステップの位置固定を解除すると整列できます。",
        BlockLayoutStatus.OverlapsOutside =>
            "周囲と重なるため整列できません。ブロックを広い場所へ移動してください。",
        _ => "ブロックの情報を確認してください。整列できませんでした。",
    };

    // ---- 名前の直接編集 ----
    private BlockViewModel? _renamingBlock;
    private string _renameOriginal = string.Empty;
    private bool _renamingIsNew;

    /// <summary>境界の計算中に選択が動いて、また境界の計算へ戻るのを防ぐ。</summary>
    private bool _refreshingBounds;

    public ICommand GroupSelectionCommand { get; private set; } = null!;

    public ICommand UngroupBlockCommand { get; private set; } = null!;

    public ICommand RenameBlockCommand { get; private set; } = null!;

    public ICommand SelectBlockNodesCommand { get; private set; } = null!;

    public ICommand RemoveFromBlockCommand { get; private set; } = null!;

    /// <summary>選んでいるブロックの中だけを並べ直す。</summary>
    public ICommand LayoutBlockCommand { get; private set; } = null!;

    /// <summary>整列の入口。ブロックを選んでいるときは中だけ、それ以外は全体。</summary>
    public ICommand AlignCommand { get; private set; } = null!;

    /// <summary>キャンバス上の囲み。</summary>
    public ObservableCollection<BlockViewModel> Blocks { get; } = [];

    public BlockViewModel? SelectedBlock => _selectedBlock;

    public bool HasSelectedBlock => _selectedBlock is not null;

    /// <summary>ブロックのメニューの見出し。どれを掴んだかを名前で確かめられるようにする。</summary>
    public string BlockMenuHeader => _selectedBlock is { } block
        ? $"{Shorten(block.Title)}  ・  {block.CountText}"
        : string.Empty;

    /// <summary>見出しをドラッグしている最中。</summary>
    public bool IsBlockDragging => _draggingBlock is not null;

    /// <summary>
    /// 確定前のブロック操作が動いている（移動中・命名中）。
    /// このあいだは自動保存を見送る。中途半端な座標や名前をファイルに残さないため。
    /// </summary>
    public bool IsBlockEditing => _draggingBlock is not null || _renamingBlock is not null;

    public bool HasBlocks => Blocks.Count > 0;

    /// <summary>いまの選択をそのままブロックにできる（2 件以上・全部が未所属）。</summary>
    public bool CanGroupSelection =>
        _selection.Count >= BlockService.MinimumSize && _selection.All(id => !_blockOfNode.ContainsKey(id));

    /// <summary>いまの選択を既存のブロックに足せる（全部が未所属）。</summary>
    public bool CanAddSelectionToBlock =>
        Blocks.Count > 0 && _selection.Count > 0 && _selection.All(id => !_blockOfNode.ContainsKey(id));

    /// <summary>いまの選択のどれかがブロックに入っている。</summary>
    public bool CanRemoveSelectionFromBlock =>
        _selection.Count > 0 && _selection.Any(id => _blockOfNode.ContainsKey(id));

    /// <summary>
    /// 「ブロックにまとめる」の説明。使えないときは、その理由をそのまま出す。
    /// メニューを灰色にするだけでは「なぜ押せないのか」が分からないので、ツールチップで補う。
    /// </summary>
    public string GroupHint => _selection.Count < BlockService.MinimumSize
        ? $"{BlockService.MinimumSize} 件以上のステップを選んでください。"
        : _selection.Any(id => _blockOfNode.ContainsKey(id))
            ? "すでにブロックに入っているステップが含まれています。所属を外してからまとめてください。"
            : $"選んだ {_selection.Count} 件を 1 つの囲みにまとめます。";

    /// <summary>カードの上か見出しの上で、いま文字を打っている（日本語変換中を含む）。</summary>
    public bool IsNaming => IsBlockEditing || SelectedNode is { IsEditing: true };

    /// <summary>選んでいるブロックの中を整列できる。</summary>
    public bool CanLayoutSelectedBlock => BlockLayoutBlockedReason(_selectedBlock) is null;

    /// <summary>「中を整列」の説明。使えないときは、その理由をそのまま出す。</summary>
    public string BlockLayoutHint =>
        BlockLayoutBlockedReason(_selectedBlock) ?? "中のステップを、依存関係に沿って並べ直します。";

    /// <summary>整列ボタンと Ctrl+L が、いま何を対象にするか。</summary>
    public string AlignLabel => HasSelectedBlock ? "中を整列" : "自動整列";

    public string AlignTooltip => _selectedBlock is { } block
        ? $"「{block.Title}」の中だけを並べ直す (Ctrl+L)"
        : "きれいに並べ直す (Ctrl+L)";

    /// <summary>「ブロックに追加」の説明。</summary>
    public string AddToBlockHint => Blocks.Count == 0
        ? "まだブロックがありません。Ctrl+G で作れます。"
        : _selection.Count == 0
            ? "入れたいステップを選んでください。"
            : _selection.Any(id => _blockOfNode.ContainsKey(id))
                ? "すでにブロックに入っているステップが含まれています。先に所属を外してください。"
                : "選んだステップを、既存のブロックに入れます。";

    private void InitializeBlocks()
    {
        GroupSelectionCommand = new RelayCommand(GroupSelectedNodes, () => CanGroupSelection);
        UngroupBlockCommand = new RelayCommand(UngroupSelectedBlock, () => HasSelectedBlock);
        RenameBlockCommand = new RelayCommand(
            () => BeginBlockRename(_selectedBlock), () => HasSelectedBlock);
        SelectBlockNodesCommand = new RelayCommand(SelectNodesOfBlock, () => HasSelectedBlock);
        RemoveFromBlockCommand = new RelayCommand(
            RemoveSelectionFromBlock, () => CanRemoveSelectionFromBlock);
        LayoutBlockCommand = new RelayCommand(LayoutSelectedBlock, () => CanLayoutSelectedBlock);
        AlignCommand = new RelayCommand(Align, () => !HasSelectedBlock || CanLayoutSelectedBlock);
    }

    // ---- 索引と再構築 ----

    public BlockViewModel? BlockOf(Guid nodeId) =>
        _blockOfNode.TryGetValue(nodeId, out var block) ? block : null;

    /// <summary>モデルの Blocks から、画面用の一覧と索引を作り直す。</summary>
    internal void RebuildBlocks()
    {
        // 消えたステップを指したままの所属や、0 件になった囲みをここで落としておく。
        // 画面に出す前に必ず通るので、壊れた所属が表示や保存へ抜けていかない。
        BlockService.Prune(_project);

        var keepSelected = _selectedBlock?.Id;

        Blocks.Clear();
        _blockById.Clear();
        _blockOfNode.Clear();

        foreach (var model in _project.Blocks)
        {
            var vm = new BlockViewModel(model, this);
            Blocks.Add(vm);
            _blockById[model.Id] = vm;

            foreach (var nodeId in model.NodeIds)
            {
                _blockOfNode[nodeId] = vm;
            }
        }

        if (keepSelected is { } id && _blockById.TryGetValue(id, out var restored))
        {
            _selectedBlock = restored;
            restored.IsSelected = true;
        }
        else if (_selectedBlock is not null)
        {
            _selectedBlock = null;
        }

        UpdateBlockHighlights();
        RefreshBlockBounds();

        OnPropertyChanged(nameof(HasBlocks));
        OnPropertyChanged(nameof(SelectedBlock), nameof(HasSelectedBlock), nameof(BlockMenuHeader));
        NotifyBlockCommandStates();
    }

    /// <summary>ブロック関係のメニューの可否と説明を、まとめて出し直す。</summary>
    internal void NotifyBlockCommandStates() => OnPropertyChanged(
        nameof(CanGroupSelection),
        nameof(CanAddSelectionToBlock),
        nameof(CanRemoveSelectionFromBlock),
        nameof(GroupHint),
        nameof(AddToBlockHint),
        nameof(CanLayoutSelectedBlock),
        nameof(BlockLayoutHint),
        nameof(AlignLabel),
        nameof(AlignTooltip));

    /// <summary>
    /// 囲みの境界と件数を計算し直す。
    /// 境界は「いま見えている所属ノード」の外接矩形なので、絞り込みや折りたたみにも自然に追随する。
    /// </summary>
    internal void RefreshBlockBounds()
    {
        if (_refreshingBounds || Blocks.Count == 0)
        {
            return;
        }

        _refreshingBounds = true;
        try
        {
            ComputeBlockBounds();
        }
        finally
        {
            _refreshingBounds = false;
        }
    }

    private void ComputeBlockBounds()
    {
        var width = NodeMetrics.Width;
        var height = NodeMetrics.Height;
        var rects = new List<NodeRect>();

        foreach (var block in Blocks)
        {
            rects.Clear();
            var total = 0;

            foreach (var nodeId in block.Model.NodeIds)
            {
                if (!_byId.TryGetValue(nodeId, out var node))
                {
                    continue;
                }

                total++;
                if (node.IsVisible)
                {
                    rects.Add(new NodeRect(node.Id, node.X, node.Y, width, height));
                }
            }

            block.Update(BlockGeometry.Compute(rects), rects.Count, total);
        }

        // 全部隠れた囲みを選んだままにはしない。
        if (_selectedBlock is { IsVisible: false })
        {
            SelectBlock(null);
        }
    }

    /// <summary>
    /// いまの表示に合った自動整列の設定に、ブロックの都合を足す。
    ///
    /// 所属ノードは一時的な固定対象にするだけで、モデルの <c>IsPinned</c> は書き換えない
    /// （ユーザーが手で留めた印と混ざると、あとで解除できなくなる）。
    /// 未所属ノードの置き先が囲みに重ならないよう、囲みの占有領域も渡す。
    /// </summary>
    /// <param name="adoptingBlockId">
    /// これから所属を引き継ぐ囲み。中に入る予定のステップを外へ弾かないよう、障害物からは外す。
    /// </param>
    internal LayoutOptions BuildLayoutOptions(Guid? adoptingBlockId = null)
    {
        var options = NodeMetrics.LayoutFor(Direction);

        if (Blocks.Count == 0)
        {
            return options;
        }

        options.FixedIds = BlockService.GroupedNodes(_project);
        options.Obstacles =
        [
            .. Blocks
                .Where(b => b.IsVisible && b.Id != adoptingBlockId)
                .Select(b => b.Bounds),
        ];

        return options;
    }

    // ---- 選択 ----

    /// <summary>ブロックを選ぶ。ノード・線・通過点の選択とは排他にする。</summary>
    public void SelectBlock(BlockViewModel? block)
    {
        if (ReferenceEquals(_selectedBlock, block))
        {
            return;
        }

        EndBlockRename(commit: true);

        _selectedBlock?.IsSelected = false;

        _selectedBlock = block;

        if (block is not null)
        {
            block.IsSelected = true;

            // ノード・線の選択は落とす（同時に選ばれていると Delete の行き先が曖昧になる）。
            ClearEdgeSelection();
            _selection.Clear();
            _connectSourceId = null;

            foreach (var node in Nodes)
            {
                node.IsSelected = false;
                node.IsEditing = false;
            }

            _selectedNode = null;

            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(SelectedNode));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionCount));
            OnPropertyChanged(nameof(HasMultipleSelected));
            OnPropertyChanged(nameof(SelectionSummary));

            StatusMessage =
                $"「{block.Title}」を選びました。見出しをドラッグでまとめて移動、F2 で名前、Ctrl+Shift+G で解除できます。";
        }

        UpdateBlockHighlights();
        OnPropertyChanged(nameof(SelectedBlock), nameof(HasSelectedBlock), nameof(BlockMenuHeader));
        NotifyBlockCommandStates();
        NotifyVisualsChanged();
    }

    /// <summary>ブロック選択中は、その所属ノードだけを補助強調する。</summary>
    private void UpdateBlockHighlights()
    {
        var members = _selectedBlock is { } selected
            ? selected.Model.NodeIds.ToHashSet()
            : [];

        foreach (var node in Nodes)
        {
            node.IsInSelectedBlock = members.Contains(node.Id);
        }
    }

    /// <summary>その ID たちに対応する、画面に載っているカード。</summary>
    private List<NodeViewModel> NodesOf(IEnumerable<Guid> ids)
    {
        var list = new List<NodeViewModel>();
        foreach (var id in ids)
        {
            if (_byId.TryGetValue(id, out var node))
            {
                list.Add(node);
            }
        }

        return list;
    }

    /// <summary>右クリックの「中のステップを選択」。ここで初めてノードの複数選択に変わる。</summary>
    public void SelectNodesOfBlock()
    {
        if (_selectedBlock is not { } block)
        {
            return;
        }

        var targets = NodesOf(block.Model.NodeIds);
        if (targets.Count == 0)
        {
            return;
        }

        var title = block.Title;
        SelectNodes(targets);
        StatusMessage = $"「{title}」の中の {targets.Count} 件を選びました。";
    }

    // ---- 作成 ----

    /// <summary>選んでいるステップを 1 つの囲みにまとめ、そのまま名前を打てる状態にする。</summary>
    public void GroupSelectedNodes()
    {
        var ids = _selection.ToList();

        if (ids.Count < BlockService.MinimumSize)
        {
            StatusMessage = $"{BlockService.MinimumSize} 件以上のステップを選んでから Ctrl+G でまとめられます。";
            return;
        }

        if (ids.Any(id => _blockOfNode.ContainsKey(id)))
        {
            StatusMessage = "すでにブロックに入っているステップが含まれています。所属を外してからまとめてください。";
            return;
        }

        // 作成と最初の命名を 1 回の Undo で戻せるよう、ここから 1 つの操作として扱う。
        BeginTransaction();

        var result = BlockService.Create(_project, ids);
        if (!result.IsOk)
        {
            // 失敗したときのモデルは呼ぶ前のままなので、控えた状態を捨てるだけでよい。
            CancelTransaction();
            StatusMessage = result.Error!;
            return;
        }

        RebuildBlocks();

        if (_blockById.TryGetValue(result.Block!.Id, out var vm))
        {
            SelectBlock(vm);
            BeginBlockRename(vm, keepTransaction: true, isNew: true);
        }

        StatusMessage = $"{ids.Count} 件をブロックにまとめました。名前を入力して Enter で確定します（Esc で取り消し）。";
    }

    // ---- 解除 ----

    public void UngroupSelectedBlock()
    {
        if (_selectedBlock is not { } block)
        {
            StatusMessage = "外したいブロックの見出しをクリックしてから、もう一度 Ctrl+Shift+G を押してください。";
            return;
        }

        EndBlockRename(commit: true);

        var title = block.Title;
        var memberIds = block.Model.NodeIds.ToList();

        BeginTransaction();
        BlockService.Dissolve(_project, block.Id);
        _selectedBlock = null;
        RebuildBlocks();

        // 解除した直後は、そのまま動かし続けられるよう中のステップを選び直す。
        SelectNodes(NodesOf(memberIds));
        CommitTransaction();
        RefreshAll();

        StatusMessage = $"「{title}」の囲みを外しました（ステップと繋がりはそのままです）。Ctrl+Z で戻せます。";
    }

    // ---- 所属の追加・取り外し ----

    public void AddSelectionToBlock(BlockViewModel? block)
    {
        if (block is null || _selection.Count == 0)
        {
            return;
        }

        var ids = _selection.ToList();

        BeginTransaction();
        var result = BlockService.Add(_project, block.Id, ids);

        if (!result.IsOk)
        {
            CancelTransaction();
            StatusMessage = result.Error!;
            return;
        }

        RebuildBlocks();
        CommitTransaction();
        RefreshAll();

        StatusMessage = $"{ids.Count} 件を「{block.Title}」に入れました。Ctrl+Z で戻せます。";
    }

    public void RemoveSelectionFromBlock()
    {
        var ids = _selection.Where(id => _blockOfNode.ContainsKey(id)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        BeginTransaction();
        var removed = BlockService.Remove(_project, ids);
        RebuildBlocks();
        CommitTransaction();
        RefreshAll();

        StatusMessage = removed == 1
            ? "ブロックから外しました（位置と繋がりはそのままです）。Ctrl+Z で戻せます。"
            : $"{removed} 件をブロックから外しました。Ctrl+Z で戻せます。";
    }

    /// <summary>
    /// 起点のステップの所属を、新しく作ったステップへ引き継ぐ。
    /// 追加・取り込み・分割・線への挿入は、どれも「起点と同じまとまりの続き」なので同じ扱いにする。
    /// 呼び出し元の履歴単位（PushUndo）の中で呼ぶこと。
    /// </summary>
    internal void InheritBlock(Guid? anchorId, IEnumerable<Guid> createdIds)
    {
        if (anchorId is not { } anchor || BlockOf(anchor) is not { } block)
        {
            return;
        }

        var ids = createdIds.Where(id => !_blockOfNode.ContainsKey(id)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        BlockService.Add(_project, block.Id, ids);
        RebuildBlocks();
    }

    /// <summary>線に挟んだステップは、両端が同じブロックのときだけ引き継ぐ。</summary>
    internal void InheritBlockFromEdge(Guid fromId, Guid toId, Guid createdId)
    {
        var from = BlockOf(fromId);
        var to = BlockOf(toId);

        if (from is null || to is null || from.Id != to.Id)
        {
            return;
        }

        BlockService.Add(_project, from.Id, [createdId]);
        RebuildBlocks();
    }

    /// <summary>ノードを消したときに所属も落とす。呼び出し元の履歴単位の中で呼ぶこと。</summary>
    internal void ForgetBlockMembership(IEnumerable<Guid> nodeIds)
    {
        if (_project.Blocks.Count == 0)
        {
            return;
        }

        BlockService.Remove(_project, [.. nodeIds]);
        RebuildBlocks();
    }

    // ---- 名前の直接編集 ----

    public void BeginBlockRename(BlockViewModel? block) => BeginBlockRename(block, keepTransaction: false, isNew: false);

    private void BeginBlockRename(BlockViewModel? block, bool keepTransaction, bool isNew)
    {
        if (block is null)
        {
            return;
        }

        if (!keepTransaction)
        {
            BeginTransaction();
        }

        _renamingBlock = block;
        _renameOriginal = block.Title;
        _renamingIsNew = isNew;

        foreach (var other in Blocks)
        {
            other.IsEditing = ReferenceEquals(other, block);
        }
    }

    /// <summary>Enter や外側クリックで確定する。空白だけなら既定名に戻す。</summary>
    public void EndBlockRename(bool commit)
    {
        if (_renamingBlock is not { } block)
        {
            return;
        }

        _renamingBlock = null;
        block.IsEditing = false;

        if (commit)
        {
            if (string.IsNullOrWhiteSpace(block.Model.Title))
            {
                block.Model.Title = TodoBlock.DefaultTitle;
            }
            else
            {
                block.Model.Title = block.Model.Title.Trim();
            }

            block.NotifyTitleChanged();
            OnPropertyChanged(nameof(BlockMenuHeader));
            CommitTransaction();
            return;
        }

        // 取り消し：新規作成中なら囲みごと、名前だけの編集なら名前を戻す。
        if (_renamingIsNew)
        {
            RollbackTransaction();
            StatusMessage = "ブロックの作成を取り消しました。";
            return;
        }

        block.Model.Title = _renameOriginal;
        block.NotifyTitleChanged();
        OnPropertyChanged(nameof(BlockMenuHeader));
        RollbackTransaction();
    }

    /// <summary>見出しの入力欄から名前が書き換わった。確定はまだしない。</summary>
    internal void NotifyBlockRenamed(BlockViewModel block)
    {
        if (ReferenceEquals(_selectedBlock, block))
        {
            OnPropertyChanged(nameof(BlockMenuHeader));
        }
    }

    // ---- 見出しのドラッグ ----

    /// <summary>まとめて動かし始める。動かせないときは false。</summary>
    public bool BeginBlockDrag(BlockViewModel block)
    {
        if (!block.CanMove)
        {
            StatusMessage = "隠れているステップがあるので、このブロックはいま動かせません。";
            return false;
        }

        EndBlockRename(commit: true);
        BeginTransaction();

        _draggingBlock = block;

        // 押した瞬間の座標を控え、毎フレーム「元の位置＋差分」で置き直す。
        // 差分を足し込み続けると、ズーム倍率のぶんだけ誤差が溜まる。
        _dragOrigins = [];
        foreach (var id in block.Model.NodeIds)
        {
            if (_byId.TryGetValue(id, out var node))
            {
                _dragOrigins.Add((node, node.X, node.Y));
            }
        }

        var moving = block.Model.NodeIds.ToHashSet();
        _dragWaypoints = [.. BlockGeometry.InternalEdges(_project, moving)
            .Select(edge => (Edge: edge, Points: edge.Waypoints.ToArray()))];

        OnPropertyChanged(nameof(IsBlockDragging));
        return true;
    }

    public void UpdateBlockDrag(double dx, double dy)
    {
        if (_draggingBlock is null)
        {
            return;
        }

        foreach (var (node, x, y) in _dragOrigins)
        {
            node.Model.X = x + dx;
            node.Model.Y = y + dy;
            node.NotifyPositionChanged();
        }

        // 両端とも動く辺は、通過点も同じ差分で運ぶ（滑らかさの設定は触らない）。
        foreach (var (edge, points) in _dragWaypoints)
        {
            for (var i = 0; i < points.Length && i < edge.Waypoints.Count; i++)
            {
                edge.Waypoints[i] = points[i] with { X = points[i].X + dx, Y = points[i].Y + dy };
            }
        }

        NotifyVisualsChanged();
    }

    public void CommitBlockDrag()
    {
        if (_draggingBlock is null)
        {
            return;
        }

        var title = _draggingBlock.Title;
        var count = _dragOrigins.Count;
        EndBlockDrag();

        var changed = CommitTransaction();
        NotifyVisualsChanged();

        if (changed)
        {
            StatusMessage = $"「{title}」の {count} 件をまとめて動かしました。Ctrl+Z で戻せます。";
        }
    }

    public void CancelBlockDrag()
    {
        if (_draggingBlock is null)
        {
            return;
        }

        // 押した瞬間の座標に戻す。スナップショットからの復元より軽く、選択も崩れない。
        foreach (var (node, x, y) in _dragOrigins)
        {
            node.Model.X = x;
            node.Model.Y = y;
            node.NotifyPositionChanged();
        }

        foreach (var (edge, points) in _dragWaypoints)
        {
            for (var i = 0; i < points.Length && i < edge.Waypoints.Count; i++)
            {
                edge.Waypoints[i] = points[i];
            }
        }

        EndBlockDrag();
        CancelTransaction();
        NotifyVisualsChanged();
        StatusMessage = "ブロックの移動をやめました。";
    }

    private void EndBlockDrag()
    {
        _draggingBlock = null;
        _dragOrigins = [];
        _dragWaypoints = [];
        OnPropertyChanged(nameof(IsBlockDragging));
    }

    /// <summary>保存・タブを閉じる・アプリ終了の前に、編集中の操作を確定する。</summary>
    public void CommitPendingBlockEdit()
    {
        if (_draggingBlock is not null)
        {
            CommitBlockDrag();
        }

        EndBlockRename(commit: true);
    }

    // ---- 操作のトランザクション ----

    /// <summary>変更前の状態を控える。ここではまだ Undo も Redo も触らない。</summary>
    private void BeginTransaction()
    {
        if (_pendingSnapshot is not null)
        {
            CommitTransaction();
        }

        _pendingSnapshot = _project.DeepClone();
        _pendingSelection = CaptureSelection();
        _pendingDirty = IsDirty;
    }

    /// <summary>確定する。実際に差分があったときだけ履歴に積む。積んだら true。</summary>
    private bool CommitTransaction()
    {
        if (_pendingSnapshot is not { } snapshot)
        {
            return false;
        }

        var selection = _pendingSelection;
        _pendingSnapshot = null;
        _pendingSelection = SelectionState.Empty;

        // 微小移動や、元の位置に戻しただけの操作では履歴も未保存状態も増やさない。
        if (!DiffersFrom(snapshot))
        {
            return false;
        }

        _undo.Add(new HistoryEntry(snapshot, selection));
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _lastUndoKey = string.Empty;
        MarkDirty();
        return true;
    }

    /// <summary>取り消す。座標・名前・所属・未保存状態を控えた時点へ戻す。</summary>
    private void RollbackTransaction()
    {
        if (_pendingSnapshot is not { } snapshot)
        {
            return;
        }

        _pendingSnapshot = null;
        var selection = _pendingSelection;
        var dirty = _pendingDirty;
        _pendingSelection = SelectionState.Empty;

        LoadProject(snapshot, _filePath, selection);
        IsDirty = dirty;
    }

    /// <summary>控えた状態を捨てるだけ（モデルは呼び出し元がすでに戻している）。</summary>
    private void CancelTransaction()
    {
        if (_pendingSnapshot is null)
        {
            return;
        }

        _pendingSnapshot = null;
        _pendingSelection = SelectionState.Empty;
        IsDirty = _pendingDirty;
    }

    internal SelectionState CaptureSelection() => _selectedBlock is { } block
        ? new SelectionState(true, [block.Id])
        : new SelectionState(false, [.. _selection]);

    /// <summary>
    /// 控えた状態といまの状態が違うか。
    /// ブロック操作で動きうるのは「囲みの構成・名前」「ノードの座標」「通過点」だけなので、そこだけ見る。
    /// </summary>
    private bool DiffersFrom(TodoProject snapshot)
    {
        if (snapshot.Blocks.Count != _project.Blocks.Count)
        {
            return true;
        }

        for (var i = 0; i < snapshot.Blocks.Count; i++)
        {
            var before = snapshot.Blocks[i];
            var after = _project.Blocks[i];

            if (before.Id != after.Id
                || !string.Equals(before.Title, after.Title, StringComparison.Ordinal)
                || before.NodeIds.Count != after.NodeIds.Count
                || !before.NodeIds.SequenceEqual(after.NodeIds))
            {
                return true;
            }
        }

        if (snapshot.Nodes.Count != _project.Nodes.Count)
        {
            return true;
        }

        var positions = _project.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));
        foreach (var node in snapshot.Nodes)
        {
            if (!positions.TryGetValue(node.Id, out var now)
                || Math.Abs(now.X - node.X) > 0.01
                || Math.Abs(now.Y - node.Y) > 0.01)
            {
                return true;
            }
        }

        var waypoints = _project.Edges.ToDictionary(e => e.Id, e => e.Waypoints);
        foreach (var edge in snapshot.Edges)
        {
            if (!waypoints.TryGetValue(edge.Id, out var now) || now.Count != edge.Waypoints.Count)
            {
                return true;
            }

            for (var i = 0; i < edge.Waypoints.Count; i++)
            {
                if (Math.Abs(now[i].X - edge.Waypoints[i].X) > 0.01
                    || Math.Abs(now[i].Y - edge.Waypoints[i].Y) > 0.01
                    || now[i].IsSmooth != edge.Waypoints[i].IsSmooth)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
