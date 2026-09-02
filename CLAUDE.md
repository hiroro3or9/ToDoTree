# CLAUDE.md

ToDoTree（C# / WPF の Todo 管理アプリ）で作業するときの約束ごと。

## Git の進め方（標準）

変更は必ずこの順で進める。`main` に直接コミットしない。

1. `main` から作業ブランチを切る
2. 意味のあるまとまりでコミットする（本文付き）
3. `main` へ `--no-ff` でマージする（履歴に枝を残す）
4. push する

### ブランチ名

`<型>/<内容>` の kebab-case。型はコミットの型に合わせる。

| 型 | 用途 | 例 |
|---|---|---|
| `feature/` | 機能追加 | `feature/ui-refresh-and-project-tabs` |
| `fix/` | バグ修正 | `fix/detail-panel-bindings` |
| `docs/` | ドキュメントのみ | `docs/git-workflow` |
| `build/` | ビルド・依存・SDK | `build/net10-slnx-tunit` |

### コミットメッセージ

- 1 行目は Conventional Commits の型とスコープ ＋ 日本語の要約。
  例: `fix(ui): タブ対応で外れた詳細パネルのバインディングを繋ぎ直す`
- 空行を挟んで本文。**何を変えたかより、なぜそうしたか**を書く。
  症状・原因・影響範囲が後から追えるように箇条書きで。
- 使う型は `feat` `fix` `build` `docs` `refactor` `test` `perf` `chore`。
- **Claude の署名行は付けない**（`Co-Authored-By: Claude ...` と `Claude-Session: ...`）。
- author / committer は `hiroro3or9 <hiroki_02130307@yahoo.co.jp>` に統一する。
  サンドボックスから コミットするときは git の設定が無いので、
  `git -c user.name='hiroro3or9' -c user.email='hiroki_02130307@yahoo.co.jp' commit ...` と明示する。

## この作業環境（Cowork のサンドボックス）の制約

リポジトリは Windows 上にあり、サンドボックスからはマウント経由で見えている。
そのため次の操作は**サンドボックスからはできない**。ユーザーに Windows 側で
実行してもらうこと。黙って迂回しない。

| できないこと | 症状 | どうするか |
|---|---|---|
| ファイルの削除 | `Operation not permitted` | 消す代わりに `mv` で退避する。必要なら削除許可を求める |
| `git checkout` / `reset --hard` / 通常の `git merge` | 作業ツリーのファイルを置き換えられず失敗する | Windows 側で実行してもらう。マージだけなら `git commit-tree` で作業ツリーに触れずに組める |
| `git push` | `could not read Username for 'https://github.com'` | 認証情報は Windows 側にある。コマンドを伝えて実行してもらう |
| ビルド・実行 | `dotnet` が無い。WPF は Windows 必須 | `dotnet build` / `dotnet run` は Windows 側で。画面の確認も同様 |

`commit` `add` `commit-tree` `update-ref` `log` `diff` はサンドボックスから問題なく使える。

### git のロックファイル

削除ができない都合で、git コマンドのたびに `.git/index.lock` や `.git/HEAD.lock` が
残ることがある。残ったままだと次の git 操作が `File exists` で止まるので、
コマンドのあとに `.git/_stale_locks/` へ退避しておく。溜まった退避先は
Windows 側でまとめて削除してよい。

## コードの約束

- UI の色は直接書かず、`Themes/Palette.Light.xaml` と `Palette.Dark.xaml` の
  キーを `DynamicResource` で引く。両方のパレットに同じキーを必ず用意する。
- 画面に依存しないロジックは `ToDoTree.Core` に置き、テストで固める。
  WPF に依存するのは `ToDoTree.App` だけ。
- 外部パッケージは足さない（アプリ本体は依存ゼロで動かす方針）。
