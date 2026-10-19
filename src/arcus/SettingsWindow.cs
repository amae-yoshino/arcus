using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Arcus;
using Microsoft.Win32;
using static Arcus.UiText;

namespace Arcus.Desktop;
internal sealed class FavoriteRow
{
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
internal sealed class SettingsWindow : Window
{
    private readonly ObservableCollection<FavoriteRow> favorites;
    private readonly DataGrid grid;
    private readonly CheckBox enabled, explorer, files, oneCommander, startup;
    private readonly TextBox maximum, excluded;
    private readonly ComboBox language;
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
    private readonly Action<ArcusSettings> save;
    public SettingsWindow(ArcusSettings current, Action<ArcusSettings> onSave)
    {
        Title = T("arcus の設定"); Width = 820; Height = 680; MinWidth = 650; MinHeight = 540;
        Icon = AppIcon.CreateWindowImage();
        RoundedTheme.Apply(this);
        save = onSave;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var options = new StackPanel(); DockPanel.SetDock(options, Dock.Top); root.Children.Add(options);
        options.Children.Add(new TextBlock { Text = T("arcus の設定"), FontSize = 22, Margin = new Thickness(0, 0, 0, 12) });
        enabled = Check(options, T("フォルダバーを有効にする"), current.Enabled);
        explorer = Check(options, T("Windows Explorerで開いているフォルダを表示"), current.ExplorerEnabled);
        files = Check(options, T("Filesで開いているフォルダを表示"), current.FilesEnabled);
        oneCommander = Check(options, T("OneCommanderで開いているフォルダを表示"), current.OneCommanderEnabled);
        startup = Check(options, T("Windowsログイン時に起動する"), StartupEnabled());
        var languageRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; options.Children.Add(languageRow);
        languageRow.Children.Add(new TextBlock { Text = T("表示言語 / Language"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        language = new ComboBox { Width = 220, ItemsSource = new[] { "Windows (Auto)", "日本語", "English" }, SelectedIndex = current.Language == "ja" ? 1 : current.Language == "en" ? 2 : 0 };
        languageRow.Children.Add(language);
        var height = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; options.Children.Add(height);
        height.Children.Add(new TextBlock { Text = T("バーの最大高さ（64〜600 DIP） "), VerticalAlignment = VerticalAlignment.Center });
        maximum = new TextBox { Text = current.MaximumBarHeight.ToString(), Width = 70 }; height.Children.Add(maximum);
        options.Children.Add(new TextBlock { Text = T("表示しないアプリ（実行ファイル名を1行ずつ。例: notepad.exe）") });
        excluded = new TextBox { Text = string.Join(Environment.NewLine, current.ExcludedApplications), AcceptsReturn = true,
            Height = 58, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 8) }; options.Children.Add(excluded);
        options.Children.Add(new TextBlock { Text = T("お気に入り（セルを編集できます）"), Margin = new Thickness(0, 5, 0, 5) });
        favorites = new(current.Favorites.Select(f => new FavoriteRow { Path = f.Path, DisplayName = f.DisplayName }));
        grid = new DataGrid { ItemsSource = favorites, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column };
        grid.Columns.Add(new DataGridTextColumn { Header = T("表示名"), Binding = new System.Windows.Data.Binding("DisplayName"), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = T("フォルダの絶対パス"), Binding = new System.Windows.Data.Binding("Path"), MinWidth = 260, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var commands = new WrapPanel(); footer.Children.Add(commands);
        Button(commands, T("追加"), () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) favorites.Add(new FavoriteRow { Path = picker.FolderName }); });
        Button(commands, T("削除"), () => { if (grid.SelectedItem is FavoriteRow row) favorites.Remove(row); });
        Button(commands, T("上へ"), () => Move(-1)); Button(commands, T("下へ"), () => Move(1));
        Button(commands, T("インポート"), () => { var picker = new OpenFileDialog { Filter = "JSON (*.json)|*.json" }; if (picker.ShowDialog(this) != true) return;
            var imported = FavoritesManager.Import(picker.FileName); favorites.Clear(); foreach (var f in imported) favorites.Add(new FavoriteRow { Path = f.Path, DisplayName = f.DisplayName }); });
        Button(commands, T("エクスポート"), () => { Commit(); var picker = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "arcus-favorites.json" };
            if (picker.ShowDialog(this) == true) FavoritesManager.Export(picker.FileName, Favorites()); });
        footer.Children.Add(error);
        var bottomRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; footer.Children.Add(bottomRow);
        var bottom = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(bottom, Dock.Right); bottomRow.Children.Add(bottom);
        var version = new TextBlock {
            Text = "arcus v" + typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0],
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 5) };
        version.SetResourceReference(TextBlock.ForegroundProperty, "Arcus.Muted"); bottomRow.Children.Add(version);
        Button(bottom, T("保存"), () => {
            Commit();
            if (!int.TryParse(maximum.Text, out var pixels)) throw new InvalidDataException(T("最大高さは整数で入力してください。"));
            var updated = new ArcusSettings { Language = language.SelectedIndex == 1 ? "ja" : language.SelectedIndex == 2 ? "en" : "auto", Enabled = enabled.IsChecked == true, ExplorerEnabled = explorer.IsChecked == true,
                FilesEnabled = files.IsChecked == true, OneCommanderEnabled = oneCommander.IsChecked == true, FilediniEnabled = false, MaximumBarHeight = pixels,
                ExcludedApplications = excluded.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList(), Favorites = Favorites() }.Validate();
            save(updated); SetStartup(startup.IsChecked == true); Close();
        });
        Button(bottom, T("キャンセル"), Close);
        var table = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(6), BorderThickness = new Thickness(1), Child = grid };
        table.SetResourceReference(Border.BackgroundProperty, "Arcus.Surface");
        table.SetResourceReference(Border.BorderBrushProperty, "Arcus.Stroke"); root.Children.Add(table);
    }
    private static CheckBox Check(Panel parent, string label, bool value)
    { var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 3, 0, 3) }; parent.Children.Add(check); return check; }
    private void Button(Panel parent, string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 5) };
        button.Click += (_, _) => { try { action(); error.Text = ""; } catch (Exception e) { error.Text = e.Message; } }; parent.Children.Add(button);
    }
    private void Commit() { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
    private System.Collections.Generic.List<Favorite> Favorites() => favorites.Select(f => new Favorite(f.Path, f.DisplayName)).ToList();
    private void Move(int delta) { Commit(); var index = grid.SelectedIndex; if (index >= 0 && index + delta >= 0 && index + delta < favorites.Count) { favorites.Move(index, index + delta); grid.SelectedIndex = index + delta; } }
    private static bool StartupEnabled()
    { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("arcus") is string; }
    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("arcus", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("arcus", false);
    }
}
