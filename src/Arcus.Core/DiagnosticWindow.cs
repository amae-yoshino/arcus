using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Arcus;

public sealed class DiagnosticWindow : Window
{
    private readonly bool inspector;
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"), FontSize = 12 };
    private readonly TextBlock status = new() { Text = "準備完了", Margin = new Thickness(0, 8, 0, 8) };
    private readonly ComboBox windows = new() { MinWidth = 420, DisplayMemberPath = "Display" };
    private readonly TextBox destination = new() { MinWidth = 350, Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
    private readonly List<Button> actions = [];
    private readonly string executable = Environment.ProcessPath!;
    private readonly string reportFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "arcus", "diagnostics");
    private DialogMonitor? monitor;
    private ProbeResponse? last;
    private bool busy;
    private DispatcherTimer? detectionTimer;
    private string dialogSet = "";

    public DiagnosticWindow(bool inspectorMode)
    {
        inspector = inspectorMode;
        Title = inspector ? "arcus — Folder Inspector (Phase 1)" : "arcus — Dialog Navigator (Phase 1)";
        Width = 1100; Height = 760; MinWidth = 760; MinHeight = 500;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = Title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = inspector
            ? "読み取り専用。取得不能なタブのパスや使用日時は推測しません。"
            : "実験用：このPCで観測した標準ダイアログ構造に限定して移動します。外部アプリは未検証です。", Margin = new Thickness(0, 8, 0, 12) });
        var row = new WrapPanel(); header.Children.Add(row);
        AddButton(row, inspector ? "フォルダを1回取得" : "ダイアログを検出", async () => {
            if (inspector)
            {
                var adapters = await FolderCollector.CollectAsync(executable);
                await ShowAsync(new(ProbeStatus.Partial, "One-shot snapshot. Normalized folders are listed below the raw adapter evidence.", adapters));
            }
            else await ShowAsync(await ProbeClient.RunAsync(executable, new("dialogs"), TimeSpan.FromSeconds(20)));
        });
        AddButton(row, "ウィンドウ一覧を更新", () => { RefreshWindows(); return Task.CompletedTask; });
        AddButton(row, "JSONを保存", ExportAsync);
        var selection = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; header.Children.Add(selection);
        selection.Children.Add(windows);
        AddButton(selection, "選択ウィンドウのUIAを採取", async () => {
            if (windows.SelectedItem is not WindowInfo w) throw new InvalidOperationException("ウィンドウを選択してください。");
            await ShowAsync(await ProbeClient.RunAsync(executable, new("tree", w.Handle, w.ProcessId), TimeSpan.FromSeconds(10)));
        });
        if (!inspector)
        {
            var nav = new WrapPanel(); header.Children.Add(nav); nav.Children.Add(destination);
            AddButton(nav, "3秒後に移動検証", async () => {
                if (windows.SelectedItem is not WindowInfo w) throw new InvalidOperationException("対象を選択してください。");
                using var process = Process.GetProcessById((int)w.ProcessId);
                var started = process.StartTime.ToUniversalTime().Ticks;
                status.Text = "3秒以内に選択したダイアログを手動で前面にしてください。";
                var path = destination.Text;
                await Task.Delay(3000);
                await ShowAsync(await ProbeClient.RunAsync(executable, new("navigate", w.Handle, w.ProcessId, path, started), TimeSpan.FromSeconds(10)));
            });
            var harness = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; header.Children.Add(harness);
            AddButton(harness, "検証用「開く」", () => { new OpenFileDialog { Title = "arcus 検証用 — 開く" }.ShowDialog(this); return Task.CompletedTask; });
            AddButton(harness, "検証用「保存」", () => {
                new SaveFileDialog { Title = "arcus 検証用 — 保存", FileName = "arcus-filename-preservation.txt",
                    Filter = "Text (*.txt)|*.txt|All (*.*)|*.*" }.ShowDialog(this);
                // Never write a file, even if a human confirms this test dialog.
                return Task.CompletedTask;
            });
        }
        header.Children.Add(status);
        root.Children.Add(output);
        Loaded += (_, _) => {
            RefreshWindows();
            if (!inspector)
            {
                try
                {
                    monitor = new DialogMonitor();
                    detectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    detectionTimer.Tick += async (_, _) => {
                        detectionTimer.Stop();
                        if (busy) { detectionTimer.Start(); return; }
                        var candidates = Native.Windows().Where(w => w.Class == "#32770").ToList();
                        var signature = string.Join(";", candidates.OrderBy(w => w.Handle).Select(w => $"{w.Handle}:{w.ProcessId}"));
                        if (signature == dialogSet) return;
                        dialogSet = signature;
                        if (candidates.Count == 0) { status.Text = "対応候補のダイアログはありません。"; return; }
                        busy = true; foreach (var button in actions) button.IsEnabled = false;
                        try { RefreshWindows(); await ShowAsync(await ProbeClient.RunAsync(executable, new("dialogs"), TimeSpan.FromSeconds(20))); }
                        catch (Exception e) { status.Text = e.Message; }
                        finally { busy = false; foreach (var button in actions) button.IsEnabled = true; }
                    };
                    monitor.Changed += () => { detectionTimer.Stop(); detectionTimer.Start(); };
                    detectionTimer.Start();
                }
                catch (Exception e) { status.Text = e.Message; }
            }
        };
        Closed += (_, _) => { detectionTimer?.Stop(); monitor?.Dispose(); };
    }

    private void RefreshWindows()
    {
        var selected = (windows.SelectedItem as WindowInfo)?.Handle;
        var list = Native.Windows().Where(w => w.ProcessId != Environment.ProcessId).ToList();
        windows.ItemsSource = list;
        windows.SelectedItem = list.FirstOrDefault(w => w.Handle == selected);
    }
    private void AddButton(Panel parent, string text, Func<Task> operation)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 0, 0, 0) };
        actions.Add(button); parent.Children.Add(button);
        button.Click += async (_, _) => {
            if (busy) return;
            busy = true; foreach (var b in actions) b.IsEnabled = false;
            try { await operation(); }
            catch (Exception e) { status.Text = e.Message; output.Text = e.ToString(); }
            finally { busy = false; foreach (var b in actions) b.IsEnabled = true; }
        };
    }
    private Task ShowAsync(ProbeResponse response)
    {
        last = response; status.Text = $"{response.Status}: {response.Detail}";
        output.Text = JsonSerializer.Serialize(response, JsonStore.Options);
        if (response.Adapters != null) output.Text += "\n\nNormalized snapshot:\n" + JsonSerializer.Serialize(
            FolderNormalizer.Merge(response.Adapters.SelectMany(x => x.Folders)), JsonStore.Options);
        var file = System.IO.Path.Combine(reportFolder, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        JsonStore.Write(file, response);
        return Task.CompletedTask;
    }
    private Task ExportAsync()
    {
        if (last == null) throw new InvalidOperationException("先に結果を取得してください。");
        var picker = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "arcus-probe.json" };
        if (picker.ShowDialog(this) == true) JsonStore.Write(picker.FileName, last);
        return Task.CompletedTask;
    }
}

public static class DiagnosticApp
{
    public static int Run(string[] args, bool inspector)
    {
        if (args.Length == 3 && args[0] == "--worker") return ProbeWorker.Run(args[1], args[2]);
        if (args.Length == 3 && args[0] == "--harness") return Harness.Run(args[1], args[2]);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new DiagnosticWindow(inspector));
    }
}
