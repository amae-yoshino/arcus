# ビルド・テスト

## 必要環境

- Windows 11 x64
- .NET SDK 10.0.402（global.json）。latestPatchで同じ機能帯の新しいパッチを許可。
- スクリプトを使う場合はPowerShell 7。
- GUI検証は対話型デスクトップで実施。通常の管理者権限は不要。

## ソースから実行

リポジトリのルートで実行します。

```powershell
dotnet --info
dotnet restore arcus.sln
dotnet build arcus.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tests/Arcus.Tests -c Release --no-build
```

起動対象は `src/arcus/bin/Release/net10.0-windows/arcus.exe` です。SDKのターゲティングパック等の復元や脆弱性情報の取得にはネット接続が必要な場合があります。追加のNuGetパッケージ参照はありません。

## ランタイム非同梱の単一exe

```powershell
dotnet publish src/arcus/arcus.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:UseSharedCompilation=false -m:1 -nr:false -o artifacts/publish
```

実行には.NET 10 Windows Desktop Runtime x64が必要です。PDBや開発用ツールを一般利用者向けZIPへ入れません。署名やバイト単位の再現性はこの手順だけでは保証しません。

Directory.Build.propsでDeterministicとPathMapを設定し、デバッグ参照等にローカルの絶対ビルドパスが残りにくいよう、コンパイラーのソースパスを `/_/arcus/` に置換しています。

## テスト範囲

`tests/Arcus.Tests/Fixtures` は、公開してよい合成データです。テスト実行ファイルの出力先へ自動コピーします。生のUIAや個人の保存タブ情報を必要としません。必須fixtureがない場合はスキップせず失敗します。

テストには、パス正規化、重複排除、設定の保存と復旧、ファイル名欄の識別、Filesの設定タブ除外、保存タブ解析、OneCommanderの整合性検査、旧設定互換性、日本語・英語の選択を含みます。合成データの成功は外部アプリの実移動の検証とは区別します。

## 任意のGUI検証

ビルドしたexeへ以下の引数を渡します。自作ダイアログを表示して閉じ、指定フォルダへ画像・JSON・テスト専用設定を書き出します。普段のAppData設定と自動起動は保存しません。

```powershell
& ./artifacts/publish/arcus.exe --visual-smoke ./artifacts/smoke-ja ja
& ./artifacts/publish/arcus.exe --visual-smoke ./artifacts/smoke-en en
& ./artifacts/publish/arcus.exe --menu-smoke ./artifacts/menu-en en
```

GUIの実行終了コードと `desktop-smoke.json` を確認してください。画像・JSONには実機フォルダが含まれるため公開しません。検証用UIだけは自身のバックグラウンドダイアログへのバー表示を許可します。実運用の前面チェックや外部アプリでの移動成功を証明するテストではありません。

開発者向けの `FolderInspector` と `DialogNavigator` はソリューションに含めていますが、一般利用者向けZIPには入れません。診断結果の取り扱いはSECURITY.mdを参照してください。
