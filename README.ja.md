# LiveSplit.TheRun.Races

[therun.gg](https://therun.gg/races) の公開レースに対応する、非公式の
LiveSplit用レースプロバイダーです。LiveSplit既存のレースプロバイダーと
同じ形式で右クリックメニューに表示され、レースルームをLiveSplit内の
WebView2ウィンドウで開きます。

このプロジェクトはtherun.ggおよびLiveSplitの公式コンポーネントではありません。

[English README](README.md)

## 機能

- LiveSplitの右クリックメニューに **therun.gg Races** を追加します。
- 公開中かつ参加受付中（`pending`）のレースを表示します。
- `starting` と `progress` のレースは、進行中レースとして別枠に表示します。
- 進行中レースを開いてもLiveSplitのOffsetを変更せず、レース開始監視も開始しません。
- レースルームを開く直前に、参加可能な状態か再確認します。
- レースルームをLiveSplit内で開き、専用WebView2プロファイルにログイン状態を保存します。
- 公式版・軽量HTML版のどちらでも、ウィンドウを閉じると開始前のレースからUnjoinします。カウントダウン開始後はForfeitを送信しません。
- 公式ページ版と軽量HTML版を別々のDLLとして提供します。コンポーネント内に表示切替設定はありません。
- ルームを開いた時点で、設定されたカウントダウン秒数を負のOffsetとして設定します。
- ルームから離れたときは、レース参加前のOffsetへ戻します。タイマー動作中の場合はリセット後に戻します。
- LiveSplitの0秒がレース開始時刻と一致するように、一度だけタイマーを開始します。
- 開始後のタイマー再補正は行いません。
- 即時開始、全員Ready後の手動開始、指定時刻開始に対応します。いずれもtherun.ggが通常の`starting`状態と`startTime`を通知することが前提です。
- Upload Keyを設定すると、タイマー情報の送信と、完走時・リセット時のLSSアップロードを行えます。
- 公式 `LiveSplit.TheRun` コンポーネントと同じUpload Keyを共有します。
- 現在のレイアウトで公式コンポーネントが動作している場合は、本コンポーネントからの送信を止めて二重送信を防ぎます。

## 公式版コンポーネントと軽量HTML版コンポーネント

`LiveSplit.TheRun.Races.dll` は公式therun.ggレースページを表示します。通常利用では
この公式ページ版を推奨します。therun.gg側で追加される機能を含め、完全なレース画面を利用できます。

`LiveSplit.TheRun.Races.Lite.dll` は、埋め込みの小さなHTMLレース画面だけを表示します。
公式レースページがLiveSplit内で読み込めない、または正常に動作しない場合のみ使用してください。
Lite版で部屋を開くには、保存済みのtherun.gg Upload Keyが必要です。

軽量HTML版では、次の機能を利用できます。

- レース状態、カウントダウン、参加者、現在のスプリット、進捗、タイムの表示
- レース開始前のJoin、Ready、Unready、Unjoin
- WebView2プロファイルに保存されたtherun.ggログインセッションの再利用

FinishとForfeitは軽量HTML版から操作できません。また、チーム管理、チャット、
モデレーション、詳細グラフ、配信表示などには対応しません。
進行中のレース部屋を開いた場合は操作ボタンを一切表示しません。
これらの機能は公式ページで利用してください。

### Lite版の開発・サポートについて

Lite版は、通常の公式ページ版が利用環境で動作しない場合にのみ使用してください。
Lite版は現状のまま提供され、これ以降の機能開発、互換性対応、利用者サポートは行いません。

## 動作条件

- LiveSplit 1.8.37、または互換性のあるバージョン

## ダウンロード

次のどちらか一方を選んでください。通常版とLite版を同時にインストールしないでください。

- **通常版（推奨）：** [v0.4.0 Releaseからダウンロード](https://github.com/Okashi-sweets/LiveSplit.TheRun.Races/releases/tag/v0.4.0)
  `LiveSplit.TheRun.Races.dll` をダウンロードします。公式therun.ggレースページを表示するバージョンです。
- **Lite版（非サポートの代替版）：** [Lite v0.4.0 Releaseからダウンロード](https://github.com/Okashi-sweets/LiveSplit.TheRun.Races/releases/tag/lite_v0.4.0)
  `LiveSplit.TheRun.Races.Lite.dll` をダウンロードします。通常版が利用環境で動作しない場合のみ使用してください。

## インストール

1. 上記の対応するReleaseリンクから、使用するバージョンのDLLをダウンロードします。
2. 使用するどちらか一方のDLLをLiveSplitの `Components` フォルダーへコピーします。
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

dotnet build src/LiveSplit.TheRun.Races.Lite/LiveSplit.TheRun.Races.Lite.csproj `
  -p:LsSrcPath=C:/path/to/LiveSplit/src
```

LiveSplitリリース版のDLLを参照してビルドする場合：

```powershell
dotnet build src/LiveSplit.TheRun.Races/LiveSplit.TheRun.Races.csproj `
  -p:LsBinPath=C:/path/to/LiveSplit

dotnet build src/LiveSplit.TheRun.Races.Lite/LiveSplit.TheRun.Races.Lite.csproj `
  -p:LsBinPath=C:/path/to/LiveSplit
```

## ライセンス

本プロジェクトは[MIT License](LICENSE)で公開されます。タイマー送信処理の一部は、
MIT Licenseで公開されている公式
[`LiveSplit.TheRun`](https://github.com/therungg/LiveSplit.TheRun)
コンポーネントを基にしています。帰属表示と第三者ライセンスについては
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)を参照してください。
