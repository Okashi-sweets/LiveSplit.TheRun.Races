# LiveSplit.TheRun.Races

[therun.gg](https://therun.gg/races) の公開レースに対応する、非公式の
LiveSplit用レースプロバイダーです。LiveSplit既存のレースプロバイダーと
同じ形式で右クリックメニューに表示され、レースルームをLiveSplit内の
WebView2ウィンドウで開きます。

このプロジェクトはtherun.ggおよびLiveSplitの公式コンポーネントではありません。

[English README](README.md)

## 機能

- LiveSplitの右クリックメニューに **therun.gg Races** を追加します。
- 公開中かつ参加受付中（`pending`）のレースだけを表示します。
- レースルームを開く直前に、参加可能な状態か再確認します。
- レースルームをLiveSplit内で開き、専用WebView2プロファイルにログイン状態を保存します。
- 公式版・軽量HTML版のどちらでも、ウィンドウを閉じると開始前のレースからUnjoinします。カウントダウン開始後はForfeitを送信しません。
- 初期設定では公式レースページを表示します。レースプロバイダー設定から、参加者進捗と基本操作に対応する軽量HTML版へ切り替えられます。
- ルームを開いた時点で、設定されたカウントダウン秒数を負のOffsetとして設定します。
- ルームから離れたときは、レース参加前のOffsetへ戻します。タイマー動作中の場合はリセット後に戻します。
- LiveSplitの0秒がレース開始時刻と一致するように、一度だけタイマーを開始します。
- 開始後のタイマー再補正は行いません。
- 即時開始、全員Ready後の手動開始、指定時刻開始に対応します。いずれもtherun.ggが通常の`starting`状態と`startTime`を通知することが前提です。
- Upload Keyを設定すると、タイマー情報の送信と、完走時・リセット時のLSSアップロードを行えます。
- 公式 `LiveSplit.TheRun` コンポーネントと同じUpload Keyを共有します。
- 現在のレイアウトで公式コンポーネントが動作している場合は、本コンポーネントからの送信を止めて二重送信を防ぎます。

## 公式版と軽量HTML版

初期設定では公式therun.ggレースページを使用します。通常利用では公式版を推奨します。
therun.gg側で追加される機能を含め、完全なレース画面を利用できます。

軽量HTML版は、このコンポーネントに埋め込まれた小さなレース画面です。
公式レースページがLiveSplit内で読み込めない、または正常に動作しない場合のみ、
**therun.gg Races** の設定にある **Use lightweight HTML race room** を有効にしてください。
有効化には、保存済みのtherun.gg Upload Keyが必要です。選択内容は保存され、
次にレースルームを開いたときから適用されます。

軽量HTML版では、次の機能を利用できます。

- レース状態、カウントダウン、参加者、現在のスプリット、進捗、タイムの表示
- レース開始前のJoin、Ready、Unready、Unjoin
- WebView2プロファイルに保存されたtherun.ggログインセッションの再利用
- 完全なレースルームへ移動する **Official page** ボタン

FinishとForfeitは軽量HTML版から操作できません。また、チーム管理、チャット、
モデレーション、詳細グラフ、配信表示などには対応しません。
これらの機能は公式ページで利用してください。また、軽量HTML版はtherun.ggのAPI挙動に
依存するため、将来のAPI変更後にはコンポーネントの更新が必要になる場合があります。

## 動作条件

- LiveSplit 1.8.37、または互換性のあるバージョン

## インストール

1. 最新のGitHub Releaseから `LiveSplit.TheRun.Races.dll`、`LICENSE`、`THIRD-PARTY-NOTICES.md` を含む配布ZIPをダウンロードします。
2. DLLをLiveSplitの `Components` フォルダーへコピーします。
3. LiveSplitを再起動します。
4. LiveSplitのレースプロバイダー設定で **therun.gg Races** を有効化・設定します。

## Upload Keyと送信データ

### Upload Keyの取得方法

1. [therun.gg](https://therun.gg/)にログインします。
2. [therun.gg/livesplit](https://therun.gg/livesplit)を開きます。ユーザーメニューの
   **LiveSplit** からも開けます。
3. 表示されたUpload Keyの欄をクリックして、キーをコピーします。
4. LiveSplitで **therun.gg Races** のレースプロバイダー設定を開きます。
5. **therun.gg upload key** にキーを貼り付け、**Save and test** を押します。

Upload Keyを入手した第三者は、あなたの代わりにランをアップロードできる可能性が
あります。キーは公開しないでください。

Upload Keyは、公式therun.ggコンポーネントと同じファイルに保存されます。

```text
%LOCALAPPDATA%\Livesplit.TheRun\uploadkey.txt
```

キーをGitリポジトリへコミットしないでください。

送信を有効にすると、スタート、スプリット、スキップ、Undo、一時停止、再開、
リセット時にタイマーとスプリットの状態がtherun.ggへ送信されます。完走時と
リセット時にはLSSもアップロードされます。アップロードするLSSからゲーム・
セグメントのアイコンを除去し、初期設定ではレイアウトファイルのパスも除去します。

WebView内のtherun.gg／Twitchログイン情報は次の場所に保存されます。

```text
%LOCALAPPDATA%\LiveSplit\TheRunWebView2
```

## デバッグログ

診断情報は次のファイルへ記録されます。

```text
%LOCALAPPDATA%\LiveSplit\TheRunRaces\debug.log
```

ログが約2 MBに達すると、以前の内容は `debug.log.old` へ移動します。
Upload KeyとCookieはログに記録しません。

## ビルド

LiveSplitのソースを参照してビルドする場合：

```powershell
dotnet build src/LiveSplit.TheRun.Races/LiveSplit.TheRun.Races.csproj `
  -p:LsSrcPath=C:/path/to/LiveSplit/src
```

LiveSplitリリース版のDLLを参照してビルドする場合：

```powershell
dotnet build src/LiveSplit.TheRun.Races/LiveSplit.TheRun.Races.csproj `
  -p:LsBinPath=C:/path/to/LiveSplit
```

## ライセンス

本プロジェクトは[MIT License](LICENSE)で公開されます。タイマー送信処理の一部は、
MIT Licenseで公開されている公式
[`LiveSplit.TheRun`](https://github.com/therungg/LiveSplit.TheRun)
コンポーネントを基にしています。帰属表示と第三者ライセンスについては
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)を参照してください。
