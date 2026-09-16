# 01 全体設計

## 目的と構成

ゴールまでの作業を、分岐・合流する有向非巡回グラフ（DAG）で管理するWindowsデスクトップアプリ。
依存線A→Bは「BはAが片付くまで待つ」。1ノードは作業、ブロックは作業を囲む整理単位。

```text
ToDoTree.slnx
  src/ToDoTree.Core   net10.0                  モデル、グラフ、計画、配置、JSON保存
  src/ToDoTree.App    net10.0-windows / WPF    WinExe / AssemblyName=ToDoTree
  tests/ToDoTree.Core.Tests                    Coreの自動確認
```

- Nullable / ImplicitUsings を有効化、LangVersion=latest。
- global.jsonでSDKを固定（例: 10.0.100、rollForward=latestFeature）。
- AppはCoreをProjectReferenceで参照。**CoreはWPFを参照しない**。
- アプリ本体に外部NuGet依存を足さない。MVVMも自前（後述）。
- テストは外部パッケージを避けたい環境向けに、`dotnet run`で動くコンソール実行の自作アサーションでよい。
  NuGetが使えるならxUnit等でもよいが、アプリ本体の依存は増やさない。

## 責務

| クラス / 領域 | 責務 |
|---|---|
| TodoProject | 保存する正本。全モデルを深いコピーにできる |
| TodoGraph | ID索引、辺の展開、接続検証、前提／後続の取得 |
| GraphAnalysis | 状態導出、進捗、最長経路 |
| NextActionPlanner / ForecastService | 次にやること、完了見込み、期限逆算 |
| Layout | 座標・矩形・曲線・吸着。WPF型に依存しない |
| JsonProjectStore | 読込検証と原子的保存 |
| WorkspaceViewModel | タブ、アクティブ文書、5秒タイマー、セッション、全プロジェクトの今日の完了 |
| MainViewModel | タブ1つの文書、Undo/Redo、検索・選択・表示状態、編集コマンド |
| Node/Edge/BlockViewModel | 表示文字列・色・派生値・入力とモデルの橋渡し |
| GraphView | Canvas、マウス／キー、ドラッグの開始・確定・取消、画面座標変換 |
| EdgeLayer / MiniMap | DrawingContextによる線・小地図の描画 |
| ThemeManager / NodeMetrics | 明暗配色、カード／ミニマル切替 |

MainViewModelとGraphViewは機能別のpartialに分けてよい。
ICommand、INotifyPropertyChanged、ObservableCollection、RelayCommand、ObservableObjectを自前で用意する。

## 状態と更新

- 永続データ: 名前、説明、タスク、依存線、ブロック、受信箱、しおり。
- タブ固有の一時状態: 選択、検索、タグ、フォーカス、枝折りたたみ、Undo/Redo、表示位置。
- アプリ設定: テーマ、表示方式、配置方向、タブ順序、アクティブID、各タブの表示位置、既知の保存先。
- 永続データを変更したら、索引・辺・着手状態・計画・一覧・見た目・コマンド可否を更新してdirtyにする。
- Undoはモデルの**深いコピー**で履歴化する。参照を共有したままの履歴はチェックリスト等で壊れる。
- 一括完了、移動、所属変更などは操作単位で1件の履歴にする。中断した操作は状態・Redoを保持。
- 検索やホバーのプレビューだけではdirty・Undoを増やさない。

## 起動と検証

セッションを復元し、何も復元できなければサンプルを表示する。Ctrl+Nで新規文書。
検証では独立した設定パス・保存フォルダーを注入し、日常使用中のデータを使わない。

```powershell
dotnet restore ToDoTree.slnx
dotnet build ToDoTree.slnx --no-restore
dotnet run --project tests/ToDoTree.Core.Tests
dotnet run --project src/ToDoTree.App
```

段階途中は作成済みのプロジェクトだけビルドしてよい。

## 初回サンプル

名前「アプリをリリースする」、説明「スタートからゴールまでを細かいステップに割って、分岐と合流で管理する例です。」

| タイトル | 種別 | 状態 | 見積(分) |
|---|---|---|---:|
| アイデアを決める | Start | Done | 60 |
| やることを書き出す | Step | Done | 90 |
| 画面のラフを描く | Step | InProgress | 120 |
| データ構造を決める | Step | NotStarted | 90 |
| 開発環境を用意する | Step | Done | 45 |
| 画面を実装する | Step | NotStarted | 480 |
| 保存機能を実装する | Step | NotStarted | 240 |
| つないで動作確認 | Milestone | NotStarted | 180 |
| アイコンを作る | Step | NotStarted | 120 |
| 紹介用のスクショを撮る | Step | NotStarted | 60 |
| 公開する | Goal | NotStarted | 60 |

線: アイデア→書き出す、書き出す→ラフ／データ構造／アイコン、アイデア→開発環境、
ラフ→画面実装、開発環境→画面実装、データ構造→保存機能、開発環境→保存機能、
画面実装→動作確認、保存機能→動作確認、動作確認→スクショ、アイコン→スクショ、
スクショ→公開、動作確認→公開。Done状態のノードはCompletedAtに生成時刻を入れる。
作成後に自動整列を1回かけて座標を決める。
