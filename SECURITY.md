# セキュリティとプライバシー

arcusはベータ版です。Windows標準のファイルダイアログとファイルマネージャーのUI情報を読み、利用者が選んだフォルダへの移動を行います。通常権限で利用してください。

## 保存される情報

`%AppData%\arcus` に設定・お気に入り・バックアップ・ログを保存します。ログにはフルパス、ウィンドウ名、ファイル名、UIAの取得値、例外などが含まれる場合があります。暗号化・匿名化・自動ローテーションは実装されていません。診断ツールやスモークテストのJSON・画像も個人情報を含むことがあります。

基本動作に通信は不要で、コード上にテレメトリー、自動アップロード、自動更新の実装はありません。ネットワークフォルダの存在確認や移動は、Windowsによるネットワークアクセスや認証を伴う場合があります。

## 不具合を報告する前に

ログ・スクリーンショット・設定をそのまま公開しないでください。ユーザー名、サーバー名、共有名、ファイル名、社内のパス、認証情報を確認・伏せたうえで、必要な部分だけを共有してください。

脆弱性を見つけた場合は、リポジトリのSecurityタブに「Report a vulnerability」が表示されていれば利用してください。非公開の報告経路が利用できない場合も、攻撃手順や機密データを公開Issueへ投稿しないでください。

## English

This beta stores settings, favorites and diagnostic logs under `%AppData%\arcus`. Logs may contain full paths, window titles, file names and UI Automation values. There is no automatic redaction, encryption or log rotation. Review and redact files before sharing them. No telemetry or automatic update client is implemented; Windows may access the network when a network folder is checked or opened.

Use "Report a vulnerability" in the repository's Security tab when available. Do not post secrets or exploit details in public issues, even if a private reporting channel is unavailable.
