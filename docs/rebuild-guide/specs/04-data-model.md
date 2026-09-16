# 04 データ・保存・復旧

## 方針

1プロジェクト = 1ファイル `*.todotree.json`。
**他環境のファイルとの互換は考えない。** `schemaVersion` は `1` から始め、
フィールドを増やしたら版を上げて、読込側で不足分に既定値を補う。
版が上（未知）のファイルは更新案内とともに拒否する。

JSONの約束: camelCase、インデント付き、日本語をそのまま出力（Unicodeエスケープしない）、
enumは文字列（C#の名前）、nullは省略、読込時のプロパティ名は大文字小文字を区別しない。
日時はDateTimeOffsetのISO形式、IDはGuid、座標はdouble。
未知のプロパティは無視する（再保存で消える）。

## モデル

派生値（着手状態、期限超過など）は保存せず、読込後に計算する。

### Project

| フィールド | 型 | 既定 | 意味 |
|---|---|---|---|
| id | Guid | 新規 | |
| name | string | 新しいプロジェクト | |
| description | string | 空 | |
| schemaVersion | int | 1 | |
| nodes / edges / blocks | 配列 | 空 | null代入時は空配列にする |
| inbox | 配列 | 空 | 未配置の項目 |
| bookmark | object? | null | 1プロジェクトに1つ |

### Node

| フィールド | 型 | 既定 | 意味 |
|---|---|---|---|
| id | Guid | 新規 | |
| title / notes | string | 空 | |
| kind | enum | Step | Start / Step / Milestone / Goal |
| status | enum | NotStarted | NotStarted / InProgress / Done / Cancelled |
| due / completedAt | DateTimeOffset? | null | |
| estimateMinutes | int? | null | 未指定は計算上30分 |
| tags | string配列 | 空 | |
| x / y | double | 0 | world座標 |
| isPinned | bool | false | 自動整列で動かさない |
| createdAt / updatedAt | DateTimeOffset | 現在 | |
| isManuallyBlocked | bool | false | statusとは独立 |
| blockReason | string | 空 | |
| checklist | 配列 | 空 | id / title / isChecked |
| parentTaskId | Guid? | null | 内部ステップの親カード。依存線とは別 |

### Edge

| フィールド | 型 | 既定 |
|---|---|---|
| id / fromId / toId | Guid | |
| label | string? | null |
| colorId | string? | null（既定色） |
| fromSide / toSide | enum | Auto（Auto/Right/Top/Bottom/Left） |
| fromPortId / toPortId | Guid? | null |
| waypoints | 配列 | 空。各要素は x / y / isSmooth(false) |

### Block

| フィールド | 型 | 既定 |
|---|---|---|
| id | Guid | 新規 |
| parentBlockId | Guid? | null |
| title | string | 新しいブロック |
| nodeIds | Guid配列 | 空。**所属の正本** |
| isCollapsed | bool | false |
| colorId | string? | null |
| ports | 配列 | 空。各要素は id / side / position(0〜1) |

### その他

- InboxItem: id / title / createdAt
- ChecklistItem: id / title / isChecked
- WorkBookmark: nodeId / note
- 色ID（ブロック・線の9色）: `slate` `red` `orange` `amber` `green` `teal` `blue` `violet` `pink`。
  nullは既定色。未知のIDは文字列を保持したまま既定色で描く。

ブロックの境界矩形は保存しない。所属カードと子ブロックの座標から計算する。
選択、Undo、検索、折りたたみ表示、フォーカス、吸着ガイドは永続データではない。

## 読込時の検証

不正なファイルは**読み込まずに拒否**する。途中まで読んで壊れた状態を作らない。

- IDの重複（ノード同士、ブロック同士、ノードとブロックの衝突）
- 存在しない端点、自己ループ、同じfrom/toの重複（ポート違いも重複とみなす）
- ブロック端点を展開した後の循環、親ブロックの循環、不正な所属、空のブロック
- 不正なポート参照（存在しないポートID、position範囲外）
- チェック項目の空ID・重複ID、null要素
- 状態と完了日時の不整合（Doneなのに完了日時なし等）
- しおりの参照先が存在しない
- 内部親（parentTaskId）が存在しない、自分自身や子孫を親にしている
- 階層をまたぐ依存線とブロック所属

## 保存経路

1. モデルを検証 → JSON化 → 同じディレクトリの `<path>.tmp` へ書く。
2. 本体があれば `File.Replace(tmp, path, path + ".bak", true)`。初回は `File.Move`。
3. 成功して初めてdirtyを消し、保存先の変更を確定する。

`.bak`は1世代のみ。自動復元UIは作らない。
名前を付けて保存の取消・失敗では保存先を切り替えない。
他のタブが使用中のパスは保存先として拒否する。既に開いているパスを開いたら既存タブへ切り替える。

## 自動保存・終了

Workspaceが5秒周期で全タブを確認する（WPFのDispatcherTimer）。

- dirtyでなければ書かない。編集中フラグ（ドラッグ・命名・接続中）が立っていればスキップ。
  単なるマウスホバーでは抑止しない。
- 保存先があれば本体へ自動保存してdirtyを消す。
- 保存先が未指定なら `%APPDATA%/ToDoTree/autosave/<タブID>.todotree.json` へ退避し、dirtyは維持する。
- 自動保存に失敗したらdirtyを残し、次の周期で再試行する。失敗を成功扱いしない。

閉じる／終了は「保存する・保存しない・キャンセル」。保存に失敗した場合と保存ダイアログを
取り消した場合は閉じない。「保存しない」を選んだら、そのタブの退避ファイルを削除して
復元対象から外す。タブごとのUndo・検索・視野を他タブへ混ぜない。

## 設定・セッション復旧

`%APPDATA%/ToDoTree/settings.json` に保存する。本体JSONと違い既定のPascalCase・数値enumでよい。
読み書きに失敗しても既定値で起動を続ける。

| キー | 内容 |
|---|---|
| OpenProjects | 各項目に DocumentId / FilePath / Zoom / PanX / PanY / HasViewportState |
| ActiveDocumentId | 前回のアクティブタブ |
| HasWorkspaceSession | セッションの有無 |
| KnownProjectPaths | 「今日の完了（全プロジェクト）」が読む既知の保存先 |
| Direction / Theme / NodeStyle | 流れる向き / 配色 / カードかミニマル |

起動時は前回のタブ一覧に従い、保存先ありは本体から、なしはタブIDの退避から復元する。
読めないタブだけスキップする。退避フォルダーの全走査や`.bak`への自動フォールバックはしない。
アクティブIDがなければ先頭タブ。何も復元できなければサンプルを表示する。
しおりがあれば初回復元時だけそこへ移動し、その後のタブ切替では現在の視野を保つ。
