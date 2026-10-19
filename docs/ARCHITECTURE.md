# 構成と動作

```text
arcus/
  README.md / README.en.md
  LICENSE / THIRD_PARTY_NOTICES.md / SECURITY.md
  arcus.sln / Directory.Build.props / global.json
  src/
    arcus/                 常駐アプリ、フォルダバー、設定、トレイ、アイコン
    Arcus.Core/            取得アダプター、UIA、ダイアログ移動、設定、翻訳
    FolderInspector/       開発者向けの読み取り診断ツール
    DialogNavigator/       開発者向けの移動検証ツール
    app.manifest
  tests/Arcus.Tests/
    Fixtures/              個人データを含まない合成JSON
  scripts/                 診断、配布ZIPの作成
  docs/                    導入・開発・配布の説明
    distribution/          配布ZIPに入れる説明書
  .github/ISSUE_TEMPLATE/   不具合報告フォーム
```

## 実行時

- トレイ常駐アプリがWinEventと250msのネイティブ監視で対象ダイアログを検出。
- 構造が確認できたダイアログごとにフォルダを1回取得して固定し、前面状態と位置に合わせてバーを表示。
- 収集は取得元ごとのワーカーで最大10秒。操作も独立ワーカーで隔離。Shell COMはSTA、UIAはウィンドウを持たないMTAで実行。
- フォルダ移動時はHWND・PID・プロセス開始時刻・前面状態等を確認し、アドレス欄を操作。ファイル名・形式が保持されたか、移動先を読めるか確認。
- ファイルマネージャーのタブ切り替え、DLL注入、システムファイル変更、グローバルなキーボード入力は実装しない。
- 診断情報はローカルに保存。テレメトリー・自動アップロード・自動更新クライアントは実装していない。
