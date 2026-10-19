# arcus

[English](README.en.md)

Windows標準の「開く」「名前を付けて保存」ダイアログに、フォルダ移動用のバーを追加する常駐アプリです。開いているフォルダとお気に入りを一覧で確認し、クリックして移動できます。

**[Windows版をダウンロード（ZIP・ベータ版）](https://github.com/amae-yoshino/arcus/releases/download/v1.2.0-beta.1/arcus-1.2.0-beta.1-win-x64.zip)** · [リリース情報](https://github.com/amae-yoshino/arcus/releases/tag/v1.2.0-beta.1)

Windows 11 x64向けです。実行には.NET 10 Windows Desktop Runtime x64が必要です。詳しくは[動作環境](#動作環境)をご覧ください。

## 主な機能

- 対応するファイルダイアログの外側にフォルダバーを自動表示。
- Explorer・Files・OneCommanderから取得したフォルダへクリックで移動。
- お気に入りの追加・名前編集・並べ替え・JSONインポート／エクスポート。
- 日本語／英語表示。初期設定はWindowsの表示言語に従い、日本語以外は英語。設定の「表示言語 / Language」から変更可能。
- 角丸のバーとトレイメニュー。バー・設定画面はWindowsのアプリ配色に追従。
- バーの最大高さ、表示しないアプリ、ログイン時の自動起動を設定可能。

## 動作環境

- Windows 11 x64
- [.NET 10 Windows Desktop Runtime x64](https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0)

配布版にはランタイムを同梱しません。SDK・ASP.NET Core Runtimeだけでは不足する場合があるため、**Windows Desktop Runtime**を導入してください。通常の管理者権限なしで実行します。基本機能にインターネット接続は不要です。

## 導入・使い方

1. 上のリンクから `arcus-1.2.0-beta.1-win-x64.zip` をダウンロードし、書き込み可能な任意のフォルダへ展開します。
2. `arcus.exe` を起動します。虹のアイコンがタスクトレイに表示されます。隠れているインジケーターも確認してください。
3. アイコンを右クリックして「設定を開く…」を選び、取得元やお気に入りを設定します。
4. 対応アプリで標準の「開く」「名前を付けて保存」を開き、バーのフォルダをクリックします。
5. ファイルの選択・保存の確定は、元のダイアログで行ってください。

一時停止・終了はトレイメニューから行えます。現在のフォルダ一覧は、ダイアログを開いたときに取得して固定します。更新するときはダイアログを開き直してください。

ソースからビルドする場合は[ビルド手順](docs/BUILD.md)を参照してください。

## ファイルマネージャーの対応範囲

| 対象 | 取得方法・制限 |
|---|---|
| Windows Explorer | Shell COMが公開するフォルダを取得。全タブ・非アクティブタブの網羅は保証しません |
| Files | 選択中フォルダを直接取得し、保存済みタブ・左右ペイン情報で補完。確認したUI構造はFiles 4.3 |
| OneCommander | 公開CurrentPathGetから現在フォルダを取得し、保存済みタブ情報で補完。確認した版は3.108 |

**Files・OneCommanderの保存済み情報には、古いパスや閉じたタブが含まれ、新しいタブが欠ける場合があります。現在開いている全タブのリアルタイム取得ではありません。**

## 既知の制限

- アプリ独自のダイアログ、権限が異なるアプリ、Windows更新でUI構造が変わった場合などは未対応・取得失敗となる場合があります。
- 外部アプリでのフォーカス切り替え、UNC、長いパス、複数モニターでの移動などの実機互換性は検証途中です。
- 移動失敗・タイムアウト時にはアドレス欄の文字だけ変わるなど、結果が不明な場合があります。状態を確認してから操作を続けてください。
- Windows共通ダイアログの表示言語はOS側に従います。

## 設定・ログ・プライバシー

保存先は `%AppData%\arcus` です。

| 内容 | 場所 |
|---|---|
| 設定・お気に入り | `settings.json` |
| 設定のバックアップ | `settings.json.bak` |
| 取得・移動ログ | `logs/` |
| 診断ツールの結果 | `diagnostics/` |

**ログにはパス・ファイル名・ウィンドウ名が含まれる場合があります。** 自動匿名化・暗号化・ログの自動削除はありません。Issueへ添付する前に確認・伏字処理をしてください。詳細は[SECURITY.md](SECURITY.md)を参照してください。

## 更新・削除

更新時はトレイからarcusを終了し、新しいZIPを展開して起動します。設定はAppDataに残ります。保存場所を変えた場合、自動起動を利用しているときは設定を保存し直してください。ベータ版間の移行前には設定のバックアップをお勧めします。

削除するときは、自動起動を無効にして保存し、arcusを終了して展開先フォルダを削除します。設定も消したい場合だけ `%AppData%\arcus` を別途削除してください。詳しくは[導入・更新手順](docs/INSTALL.md)を参照してください。

## 開発・構成

.NET 10 SDK 10.0.402（`global.json`）を使用します。追加のNuGetパッケージはありません。

```powershell
dotnet build arcus.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tests/Arcus.Tests -c Release --no-build
```

- [ビルド・テスト](docs/BUILD.md)
- [構成と動作](docs/ARCHITECTURE.md)

## 不具合報告

arcusのバージョン、Windowsのビルド、対象アプリ、ファイルマネージャーの種類・版、表示言語、再現手順を添えてください。機密情報を含むログや画像をそのまま公開しないでください。脆弱性の報告方法は[SECURITY.md](SECURITY.md)を確認してください。

## ライセンス

[MIT License](LICENSE) — Copyright (c) 2026 amae yoshino。第三者コードのライセンス表示は[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)を参照してください。
