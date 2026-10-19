arcus
=====

Windows標準の「開く」「名前を付けて保存」に、フォルダ移動バーを追加します。

必要環境: Windows 11 x64 / .NET 10 Windows Desktop Runtime x64
ランタイムは同梱しません。Microsoft公式配布元:
https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0

使い方:
1. ZIPを展開しarcus.exeを起動します。
2. トレイの虹アイコンから設定を開き、取得元・お気に入りを設定します。
3. 表示言語はWindows (Auto)、日本語、Englishから選べます。
4. 対応する標準ファイルダイアログを開き、バーのフォルダをクリックします。
5. 保存・ファイル選択の確定は元のダイアログで行ってください。

対応: Windows Explorer / Files / OneCommander（いずれも取得範囲に制限あり）。
一覧はダイアログを開いたときに取得して固定します。
Files・OneCommanderの保存タブ情報は古い場合があります。
現在の全タブの網羅や、すべての外部アプリでの動作は保証しません。
移動失敗・タイムアウト後は、アドレス欄や実際の場所を確認してください。

更新: トレイから終了し、新しい版を展開・起動します。
設定は %AppData%\arcus\settings.json に保持されます。
exeの場所を変更し自動起動を利用する場合は設定を保存し直してください。
削除: 自動起動を無効にして保存→終了→展開先フォルダを削除します。
設定・ログも消したい場合だけ %AppData%\arcus を別途削除してください。

ログ: %AppData%\arcus\logs
パス・ファイル名・ウィンドウ名などを含む場合があります。
自動匿名化・暗号化・自動ローテーションはありません。
不具合報告にはバージョン・Windows・対象アプリ・再現手順を添え、
ログや画像の機密情報は必ず伏せてください。

MIT License / Copyright (c) 2026 amae yoshino
LICENSE、THIRD_PARTY_NOTICES.mdも確認してください。
