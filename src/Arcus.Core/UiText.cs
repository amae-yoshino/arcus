using System.Globalization;

namespace Arcus;

public static class UiText
{
    public static string Language { get; set; } = "auto";
    public static string Resolve(string language, string? culture = null) => language is "ja" or "en" ? language :
        (culture ?? CultureInfo.CurrentUICulture.Name).StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    public static string T(string text, params object[] args)
    {
        var translated = Resolve(Language) == "en" && English.TryGetValue(text, out var value) ? value : text;
        return args.Length == 0 ? translated : string.Format(CultureInfo.CurrentCulture, translated, args);
    }
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["arcus の設定"] = "arcus Settings",
        ["フォルダバーを有効にする"] = "Enable folder bar",
        ["Windows Explorerで開いているフォルダを表示"] = "Show folders open in Windows Explorer",
        ["Filesで開いているフォルダを表示"] = "Show folders open in Files",
        ["OneCommanderで開いているフォルダを表示"] = "Show folders open in OneCommander",
        ["Windowsログイン時に起動する"] = "Start when I sign in to Windows",
        ["表示言語 / Language"] = "Language / 表示言語",
        ["バーの最大高さ（64〜600 DIP） "] = "Maximum bar height (64–600 DIP) ",
        ["表示しないアプリ（実行ファイル名を1行ずつ。例: notepad.exe）"] = "Excluded apps (one executable name per line, e.g. notepad.exe)",
        ["お気に入り（セルを編集できます）"] = "Favorites (click a cell to edit)",
        ["表示名"] = "Display name",
        ["フォルダの絶対パス"] = "Absolute folder path",
        ["追加"] = "Add", ["削除"] = "Remove", ["上へ"] = "Move up", ["下へ"] = "Move down",
        ["インポート"] = "Import", ["エクスポート"] = "Export", ["保存"] = "Save", ["キャンセル"] = "Cancel",
        ["最大高さは整数で入力してください。"] = "Enter a whole number for the maximum height.",
        ["設定を開く…"] = "Settings…", ["arcus を終了"] = "Exit arcus",
        ["arcus — フォルダバー"] = "arcus — Folder bar",
        ["エクスプローラーで開いているフォルダ"] = "Folders open in Windows Explorer",
        ["{0}で開いているフォルダ"] = "Folders open in {0}",
        ["保存済みタブの情報です。現在の状態と異なる場合があります。"] = "Saved tab information; it may differ from the current state.",
        ["{0}の復元データ（古い情報の場合があります）"] = "{0} saved tabs (may be outdated)",
        ["お気に入り"] = "Favorites", ["なし"] = "None",
        ["開いているフォルダを取得中…"] = "Reading open folders…",
        ["{0}件取得・{1}件の取得先が未対応または失敗"] = "{0} folders · {1} sources unsupported or failed",
        ["{0}件 · このダイアログを閉じるまで一覧を固定"] = "{0} folders · List stays fixed until this dialog closes",
        ["移動先を確認しています…"] = "Checking destination…", ["移動しました"] = "Folder changed",
        ["移動を確認できません。"] = "Could not confirm the folder change. ",
        ["設定を反映しました。開いているフォルダ一覧は固定しています。"] = "Settings applied. The open-folder list stays fixed.",
        ["arcus は既に起動しています。タスクトレイから設定を開いてください。"] = "arcus is already running. Open Settings from the system tray.",
        ["バーの最大高さは64〜600 DIPにしてください。"] = "Maximum bar height must be between 64 and 600 DIP.",
        ["設定の一覧がnullです。"] = "A settings list is null.",
        ["お気に入りがnullです。"] = "A favorite is null.",
        ["絶対フォルダパスではありません: {0}"] = "Not an absolute folder path: {0}",
        ["設定をバックアップから復旧しました。"] = "Settings were restored from a backup.",
        ["設定を読み込めないため初期状態で起動しました。元ファイルは保持されています。"] = "Settings could not be read. Defaults were loaded and the original files were kept."
    };
}
