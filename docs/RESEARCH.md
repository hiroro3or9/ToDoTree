# 調査メモ：この先の機能追加と改善の展望

調査日: 2026-09-01 ／ 実装は含みません。

外の事例（依存関係つき Todo、無限キャンバス、グラフ描画）、確立した計画手法、
WPF まわりの技術動向を当たり、いまの ToDoTree（Core 2,117 行 / App 4,548 行 / テスト 1,067 行）に
何が効くかを見立てました。

---

## 1. いちばん大きな発見：依存関係は「維持コスト」で死ぬ

GTD のフォーラムに、まさに ToDoTree が解いている問題の相談があります
——「依存のないタスク（＝いま着手できるもの）だけを自動で出してほしい」。
回答を見ると、これができるアプリは **MyLifeOrganized / OmniFocus / Nirvana / Everdo** 程度で、
多くは「次にやることを手で星付けする」方式です。**着眼点は正しい**と裏が取れました。

ただし同じスレッドに、無視できない反論があります。

> 依存の仕組みは「強すぎて窮屈」になり、常に手入れが要る。
> 「セットアップの維持に、実際の作業より時間を使うことになる」

これが本質的なリスクです。ToDoTree の敵は「機能が足りないこと」ではなく、
**グラフを維持する手間が、グラフから得られる利益を上回ること**。

この見方で優先順位を組み直すと、いちばん価値が高いのは
「依存関係を張る・直す・捨てるコストを下げる」もので、
一括入力・分割・カード上編集（実装済み）はまさにその方向でした。続きの候補は次の通りです。

- **依存の自動推測** … 一括入力で並んだ行を「直列でいいですか？」と提案する
- **依存ゼロでも成立する運用** … 依存を張らないステップは常に「着手できる」扱い（現状そうなっている）
  ことを明示し、必要な場所にだけ線を引けば済むと分かる導線
- **古びた枝の掃除** … 長く触られていないステップ、到達不能になった枝を検出して片付けを促す
- **依存の一括操作** … 選択した複数ステップを「直列に繋ぐ」「同じ先行にぶら下げる」の一発操作

---

## 2. 相互運用：JSON Canvas が現実的な出口

Obsidian が策定した **JSON Canvas** は、無限キャンバスのデータをやり取りするための
オープンな形式で、仕様は公開・自由に実装可能です。構造は ToDoTree とほぼ同型でした。

| JSON Canvas | ToDoTree の対応 |
|---|---|
| `nodes[]`: `id` / `type` / `x` / `y` / `width` / `height` / `color` | `TodoNode` の `Id` / `X` / `Y` ＋ カードの固定サイズ |
| `type: "text"` の `text`（Markdown 可） | `Title` ＋ `Notes` |
| `edges[]`: `id` / `fromNode` / `toNode` / `fromSide` / `toSide` / `label` / `toEnd` | `TodoEdge` の `FromId` / `ToId` / `Label`、辺の出口計算 |
| `color`: 16進 または プリセット 1〜6（赤・橙・黄・緑・水・紫） | 状態や種別の色に対応づけ可能 |
| `type: "group"` の `label` | 将来のグループ化にそのまま使える |

**評価**：Mermaid 書き出し（実装済み）は「絵」で、貼った先では編集できません。
JSON Canvas は **編集できる状態で外に出せる**のが違いです。Obsidian の Canvas で開いて
書き足し、また ToDoTree に戻す、という往復が成立します。

**注意点**：JSON Canvas には状態・見積り・期限にあたるフィールドがありません。
往復させるなら、`.todotree.json` を正本として保ち、Canvas は入出力の一形式として扱うのが安全です
（Canvas 側で足されたノードは「未着手のステップ」として取り込む、程度の割り切り）。

---

## 3. レイアウト：いまの実装に足りない 2 つがはっきりした

Eclipse Layout Kernel (ELK) の Layered アルゴリズムは 5 段階で、
それぞれに複数の戦略があります。いまの `LayeredLayoutEngine` と突き合わせると差がはっきりしました。

| ELK の段階 | ELK の既定 | ToDoTree の現状 | 差 |
|---|---|---|---|
| 循環の除去 | GREEDY | 循環は入力時に拒否 | 問題なし |
| レイヤ割り当て | NETWORK_SIMPLEX | 最長経路法 | 幅が広がりがち |
| 交差の最小化 | LAYER_SWEEP | バリセンター法のスイープ | ほぼ同等 |
| 座標決定 | **BRANDES_KOEPF** | 単純な等間隔配置 | **長い辺が折れる・揃わない** |
| 辺の経路 | polyline / orthogonal / spline | ベジェ直結 | **複数レイヤをまたぐ辺がカードの上を横切る** |

**いちばん効くのは「ダミーノード」**です。いまの実装は、レイヤを 2 つ以上またぐ辺に対して
中間の仮ノードを置いていません。そのため長い辺が途中のカードの上を素通りします。
ダミーノードを入れて、そのぶんの隙間を空ければ、線がカードを避けて通るようになります。
これは純粋な計算なので Core でテストできます。

**もう 1 つの収穫**：ELK は各段階に `INTERACTIVE` 戦略を持ち、
**前回の座標を尊重して並べ直す**ことができます。
「自動整列を押すと手で置いた位置が飛ぶ」問題への、確立された答えがこれです
（いまは `IsPinned` で個別に固定する方式なので、全体を緩く保つ手段がない）。

ELK 自体は Java なので移植は現実的ではありません。**考え方だけ取り込む**のが妥当です。

---

## 4. 予測：単一の日付から、確率的な言い方へ

いまの `ForecastService` は「残り ÷ これまでのペース」で 1 つの日付を出します。
アジャイル界隈で定着している **モンテカルロ法によるスループット予測** は、
過去の完了実績の *ばらつき* を使って何千回もシミュレーションし、
「85% の確率で 9/20 まで」のように分位で答えます。

- 必要なデータは **すでに持っています**（`CompletedAt` が各ステップにある）
- 「1 つの日付」は外れると信用を失いますが、「85% で ◯/◯」は外れても嘘になりません
- 実績が少ないうちは、**PERT の三点見積り**（楽観・最頻・悲観）で代用できます
  （現状の `EstimateMinutes` を 3 つに増やす小さな変更）

**評価**：Core の純粋なロジックで、テストも書ける。費用対効果は高いと見ます。
ただし体験として過剰にならないよう、表示は「9/20 ごろ（早ければ 9/15、遅ければ 9/28）」程度の
柔らかい言い方に落とすのが良さそうです。

---

## 5. 計画手法：クリティカルチェーンの「バッファ」だけ借りる

CCPM（Critical Chain Project Management）は、各タスクの見積りから安全余裕を抜き取り、
**プロジェクト全体の末尾にバッファとしてまとめる**手法です。
枝が本流に合流する手前には**フィーディングバッファ**を置きます。
狙いは、締切ギリギリまで着手しない「学生症候群」と、
与えられた時間いっぱいまで作業が膨らむ「パーキンソンの法則」を潰すこと。

**ToDoTree との相性**：最長経路（クリティカルパス）はすでに計算しているので、
バッファの考え方は自然に乗ります。とくに **バッファ消費率の表示**
（「余裕を 6 割使った／進捗は 4 割」＝赤信号）は、単なる進捗率より意思決定に効きます。

**注意**：CCPM 本体は資源制約のある複数人プロジェクト向けで、1 人には重すぎます。
借りるのは「余裕をタスクごとに隠さず、まとめて可視化する」という考え方だけにすべきです。

---

## 6. WPF の足回り：4 つとも状況が良い

### ダークモード — 自前実装は不要になった

.NET 9 以降の WPF には Fluent テーマ（`PresentationFramework.Fluent`）が同梱され、
`ThemeMode` に `Light` / `Dark` / `System` / `None` を指定できます。
`System` は OS の設定に追従し、ハイコントラストにも対応します。ウィンドウ単位の指定がアプリ単位より優先されます。

**注意**：まだ実験的扱いで、使うには `WPF0001` の警告抑制が要り、将来の破壊的変更があり得ます。
いまの ToDoTree は色を `Themes/Styles.xaml` に集めてあるので、
Fluent に寄せるか、同じ仕組みで自前のダーク配色を足すかは選べます。

### 描画性能 — いまの作りは正しい方向

辺を 1 枚の `OnRender` にまとめている現在の設計は、要素数を増やさない点で定石どおりです。
次に効いてくるのはカード側で、数千個になると `DrawingVisual` / `VisualCollection` による
仮想化が定番の解になります。ただし数百個の段階では体感差は出ないはずなので、
**測ってから**にすべきです。

### UI テスト — 「実行して確かめられない」を潰せる

**FlaUI**（Windows の UI Automation を .NET から扱うライブラリ）が標準的な選択肢です。
GitHub Actions の windows ランナーでも動きます（完全なヘッドレスではなく、実セッションが要る）。
起動して主要ウィンドウが開くことを確かめるだけの**スモークテスト**でも、
いまの「ビルドが通るかも分からない」状態からは大きな前進になります。

### 配布と自動更新

**Velopack** が現行の定番で、インストーラと差分自動更新をまとめて面倒みてくれます。
個人利用なら zip 配布でも足りますが、複数台で使い始めたら効いてきます。

---

## 7. 保存形式：まだ JSON のままでよい

SQLite に CRDT を載せて端末間同期する仕組み（cr-sqlite、SQLite-Sync など）が
実用段階に入っています。ただし ToDoTree の現状は 1 人 1 台・ノード数も数百規模で、
**JSON の利点（中身が読める・git で差分が見える・壊れても直せる）のほうが大きい**と判断します。

`IProjectStore` で抽象化してあるので、次のどちらかが起きたら移行を検討すれば十分です。

- 2 台目の端末で同じプロジェクトを触りたくなった
- ノードが 1,000 を超えて、保存や読み込みが体感で遅くなった

---

## 8. AI によるステップ分解

「ゴールを小さなステップに割る」のは LLM エージェント研究で最も基本的な処理で、手法は枯れています。
ToDoTree には **分割 UI と一括入力がすでにある**ので、接続点は用意できています。

現実的な形は「AI が箇条書きの**下書き**を出し、いまの取り込みダイアログにそのまま流す」こと。
生成物が必ず人の目と手を通る形になり、外すときも 1 か所を消すだけで済みます。

**判断**：優先度は高くありません。手で書き出す速さはすでに十分で、
むしろ「維持コストを下げる」方向（第 1 節）のほうが効きます。やるなら後段。

---

## 9. 総合：この先の見立て

### 第 1 段 — 小さくて確実に効く

1. **長い辺のダミーノード＋座標決定の改善（Brandes–Köpf 相当）** — 見た目の質が一段上がる。Core で完結しテスト可能
2. **FlaUI の起動スモークテスト** — 「動くか分からない」を構造的に潰す
3. **Fluent テーマでダークモード** — 数十行で入る
4. **JSON Canvas 書き出し** — Obsidian で開いて編集できる形の出口。Core で完結

### 第 2 段 — 体験の質

5. **確率的な完了予測**（85% 点、または三点見積り）
6. **バッファ消費の表示**（クリティカルチェーンの考え方だけ借りる）
7. **自動整列の Interactive 化**（手で置いた位置を尊重して並べ直す）
8. **依存の維持コストを下げる仕掛け**（自動推測・一括接続・古い枝の掃除）

### 第 3 段 — 広げる

9. JSON Canvas の読み込み（往復の成立）
10. SQLite ストアと端末間同期（必要が生じてから）
11. AI によるステップ分解の下書き
12. Velopack による配布と自動更新

### あえて追わないもの

- **CCPM の完全実装** — 1 人には重い。バッファの可視化だけで足りる
- **リアルタイム共同編集** — 用途が個人の計画づくりで、複雑さに見合わない
- **描画の仮想化を先回りで入れる** — 数百ノードでは効かない。測ってから

---

## 出典

- JSON Canvas 仕様 <https://jsoncanvas.org/spec/1.0/> ／ 発表 <https://obsidian.md/blog/json-canvas/> ／ <https://github.com/obsidianmd/jsoncanvas>
- 依存関係で次の行動を出したいという相談と反論 <https://forum.gettingthingsdone.com/threads/looking-for-task-manager-that-can-automatically-display-only-non-dependent-tasks-next-actions.18772/>
- ELK Layered の各段階と戦略 <https://eclipse.dev/elk/blog/posts/2025/25-08-21-layered.html> ／ 階層グラフ描画の概説 <https://en.wikipedia.org/wiki/Layered_graph_drawing>
- モンテカルロによる確率的予測 <https://www.industriallogic.com/blog/reckoning-with-reality-with-probabilistic-forecasting/> ／ <https://blog.letpeople.work/p/an-introduction-and-step-by-step-guide-to-monte-carlo-simulations>
- クリティカルチェーン <https://en.wikipedia.org/wiki/Critical_chain_project_management> ／ <https://thedigitalprojectmanager.com/project-management/critical-chain-method/>
- WPF の Fluent テーマ <https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md> ／ <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90>
- 大量ビジュアルの扱い <https://learn.microsoft.com/en-us/archive/blogs/jgoldb/virtualized-wpf-canvas> ／ <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/using-drawingvisual-objects>
- FlaUI <https://github.com/FlaUI/FlaUI>
- Velopack <https://velopack.io/> ／ <https://github.com/velopack/velopack>
- SQLite への CRDT 同期 <https://github.com/sqliteai/sqlite-sync>
