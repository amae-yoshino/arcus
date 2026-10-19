using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Arcus;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using static Arcus.UiText;

namespace Arcus.Desktop;
internal sealed class AppCoordinator : IDisposable
{
    private sealed class Session
    {
        public required WindowInfo Window;
        public required long Started;
        public FolderBarWindow? Bar;
        public List<FolderInfo> Folders = new();
        public bool Navigating, Closed, CollectionComplete;
        public long DetectedAt = Stopwatch.GetTimestamp();
        public double BarShownMs, SnapshotReadyMs;
    }
    private readonly Application app;
    private readonly SettingsManager store;
    private readonly bool smoke;
    private ArcusSettings settings;
    private readonly Dictionary<long, Session> sessions = new();
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem enabled;
    private readonly DispatcherTimer timer;
    private readonly DialogMonitor monitor;
    private SettingsWindow? settingsWindow;
    private bool disposed;
    private bool updatePending;
    public AppCoordinator(Application application, string? settingsDirectory = null, bool smokeMode = false)
    {
        app = application;
        store = new SettingsManager(settingsDirectory); smoke = smokeMode;
        var loaded = store.Load(); settings = loaded.Settings;
        UiText.Language = settings.Language;
        var menu = TrayMenuStyle.Create(settings.Enabled,
            (sender, _) => { settings.Enabled = ((Forms.ToolStripMenuItem)sender!).Checked; Persist(); },
            (_, _) => OpenSettings(), (_, _) => app.Shutdown());
        enabled = (Forms.ToolStripMenuItem)menu.Items[0];
        trayIcon = AppIcon.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Text = T("arcus — フォルダバー"), Icon = trayIcon,
            Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => OpenSettings();
        monitor = new DialogMonitor(skipOwnProcess: !smoke);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Tick();
        // Native-only updates are cheap; no UIA or folder collection in this loop.
        monitor.Changed += () => {
            if (disposed || updatePending) return;
            updatePending = true;
            app.Dispatcher.BeginInvoke(new Action(() => { updatePending = false; Tick(); }), DispatcherPriority.Background);
        };
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        timer.Start();
        app.Dispatcher.BeginInvoke(new Action(Tick), DispatcherPriority.Loaded);
        if (loaded.Notice != null) Notify(loaded.Notice);
    }
    private void Tick()
    {
        if (disposed) return;
        try
        {
            foreach (var pair in sessions.ToArray())
            {
                var session = pair.Value;
                Native.GetWindowThreadProcessId((nint)pair.Key, out var pid);
                if (!Native.IsWindow((nint)pair.Key) || pid != session.Window.ProcessId)
                { session.Closed = true; session.Bar?.Finish(); sessions.Remove(pair.Key); }
            }
            var foreground = Native.GetForegroundWindow();
            foreach (var session in sessions.Values)
            {
                session.Bar?.Reposition(settings.Enabled && !IsExcluded(session.Window.Process) &&
                    (foreground == (nint)session.Window.Handle || (smoke && session.Window.ProcessId == Environment.ProcessId)) &&
                    Native.IsWindowVisible((nint)session.Window.Handle) && !Native.IsIconic((nint)session.Window.Handle), settings.MaximumBarHeight);
            }
            if (!settings.Enabled) return;
            // Collect once even for a dialog opened in the background. Only the active dialog's bar is visible.
            foreach (var window in Native.Windows("#32770"))
            {
                if (sessions.ContainsKey(window.Handle) || IsExcluded(window.Process)) continue;
                if (window.ProcessId == Environment.ProcessId && !smoke) continue;
                if (smoke && window.ProcessId != Environment.ProcessId) continue;
                using var process = Process.GetProcessById((int)window.ProcessId);
                var created = new Session { Window = window, Started = process.StartTime.ToUniversalTime().Ticks };
                sessions.Add(window.Handle, created);
                _ = InspectAsync(created);
            }
        }
        catch (Exception e) { Log(new(ProbeStatus.Failed, "Window monitoring: " + e.Message)); }
    }
    internal bool CaptureSmoke(string directory, bool includeVisuals = false)
    {
        var session = sessions.Values.FirstOrDefault(s => s.Bar?.IsVisible == true && !s.Closed && s.CollectionComplete);
        if (session?.Bar == null) return false;
        var bar = session.Bar;
        bar.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(bar.ActualWidth),
            (int)Math.Ceiling(bar.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(bar);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, "folder-bar.png"))) encoder.Save(file);
        using (var iconBitmap = trayIcon.ToBitmap()) iconBitmap.Save(Path.Combine(directory, "tray-icon.png"), System.Drawing.Imaging.ImageFormat.Png);
        using (var executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!))
        using (var iconBitmap = executableIcon?.ToBitmap()) iconBitmap?.Save(Path.Combine(directory, "executable-icon.png"), System.Drawing.Imaging.ImageFormat.Png);
        if (includeVisuals)
        {
            var preview = new SettingsWindow(settings, _ => { }) { ShowActivated = false };
            preview.Show(); preview.Dispatcher.Invoke(new Action(preview.UpdateLayout), DispatcherPriority.Loaded);
            SaveVisual(preview, "settings-light.png");
            RoundedTheme.Apply(preview, darkOverride: true);
            preview.Dispatcher.Invoke(new Action(preview.UpdateLayout), DispatcherPriority.Render); SaveVisual(preview, "settings-dark.png");
            preview.Close();
            RoundedTheme.Apply(bar, darkOverride: true); bar.UpdateLayout(); SaveVisual(bar, "folder-bar-dark.png");
            bar.ApplyTheme();
        }
        var hwnd = new System.Windows.Interop.WindowInteropHelper(bar).Handle;
        DesktopNative.GetWindowRect((nint)session.Window.Handle, out var dialogRect); DesktopNative.GetWindowRect(hwnd, out var barRect);
        JsonStore.Write(Path.Combine(directory, "desktop-smoke.json"), new {
            Status = "Success", Scope = "Own standard dialog; background display allowed only in smoke mode; production foreground navigation not tested",
            Dialog = session.Window, DialogBounds = dialogRect.Pixels, BarBounds = barRect.Pixels,
            WorkArea = DesktopNative.WorkArea((nint)session.Window.Handle), FolderCount = session.Folders.Count,
            DialogDpi = DesktopNative.GetDpiForWindow((nint)session.Window.Handle), BarDpi = DesktopNative.GetDpiForWindow(hwnd),
            DetectionToBarMs = Math.Round(session.BarShownMs, 1), DetectionToSnapshotMs = Math.Round(session.SnapshotReadyMs, 1),
            Folders = session.Folders, ExtendedStyle = ((long)DesktopNative.GetWindowLongPtr(hwnd, -20)).ToString("X"),
            GlobalTopmost = ((long)DesktopNative.GetWindowLongPtr(hwnd, -20) & 8) != 0 });
        return true;
        void SaveVisual(Window window, string filename)
        {
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(window);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, filename)); png.Save(stream);
        }
    }
    private bool IsExcluded(string process) => settings.ExcludedApplications.Contains(process, StringComparer.OrdinalIgnoreCase);
    private async Task InspectAsync(Session session)
    {
        try
        {
            var result = await ProbeClient.RunAsync(Environment.ProcessPath!, new("dialog", session.Window.Handle, session.Window.ProcessId), TimeSpan.FromSeconds(10));
            for (var retry = 0; retry < 2 && result.Status == ProbeStatus.Unsupported && !disposed && !session.Closed; retry++)
            {
                await Task.Delay(500);
                if (disposed || session.Closed) return;
                result = await ProbeClient.RunAsync(Environment.ProcessPath!, new("dialog", session.Window.Handle, session.Window.ProcessId), TimeSpan.FromSeconds(10));
            }
            if (disposed || session.Closed) return;
            Log(result);
            if (result.Status != ProbeStatus.Success || result.Dialogs?.SingleOrDefault()?.SupportedStructure != true) return;
            session.Bar = new FolderBarWindow((nint)session.Window.Handle, settings, path => _ = NavigateAsync(session, path));
            Tick();
            session.BarShownMs = Stopwatch.GetElapsedTime(session.DetectedAt).TotalMilliseconds;
            var sources = new List<string>();
            if (settings.ExplorerEnabled) sources.Add("Explorer");
            if (settings.FilesEnabled) sources.Add("Files");
            if (settings.OneCommanderEnabled) sources.Add("OneCommander");
            // Filedini is temporarily unavailable in the desktop app, including old settings.
            var responses = await Task.WhenAll(sources.Select(source => ProbeClient.RunAsync(Environment.ProcessPath!, new("collect:" + source), TimeSpan.FromSeconds(10))));
            if (disposed || session.Closed) return;
            foreach (var response in responses) Log(response);
            session.Folders = FolderNormalizer.Merge(responses.SelectMany(x => x.Adapters ?? new()).SelectMany(x => x.Folders));
            var failures = responses.Count(x => x.Status is ProbeStatus.Failed or ProbeStatus.TimedOut or ProbeStatus.Unsupported);
            session.Bar.UpdateFolders(session.Folders, settings.Favorites,
                failures > 0 ? T("{0}件取得・{1}件の取得先が未対応または失敗", session.Folders.Count, failures) : T("{0}件 · このダイアログを閉じるまで一覧を固定", session.Folders.Count));
            session.SnapshotReadyMs = Stopwatch.GetElapsedTime(session.DetectedAt).TotalMilliseconds;
            session.CollectionComplete = true;
            Tick();
        }
        catch (Exception e) { Log(new(ProbeStatus.Failed, "Dialog session: " + e)); }
    }
    private async Task NavigateAsync(Session session, string path)
    {
        if (disposed || session.Closed || session.Navigating || !settings.Enabled) return;
        session.Navigating = true; session.Bar?.SetStatus(T("移動先を確認しています…"), false);
        try
        {
            var result = await ProbeClient.RunAsync(Environment.ProcessPath!,
                new("navigate", session.Window.Handle, session.Window.ProcessId, path, session.Started), TimeSpan.FromSeconds(15));
            Log(result);
            if (disposed || session.Closed) return;
            session.Bar?.SetStatus(result.Status == ProbeStatus.Success ? T("移動しました") : T("移動を確認できません。") + result.Detail, true);
        }
        catch (Exception e) { if (!session.Closed) session.Bar?.SetStatus(e.Message, true); }
        finally { session.Navigating = false; }
    }
    private void OpenSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings, updated => {
            store.Save(updated); settings = updated; enabled.Checked = settings.Enabled;
            UiText.Language = settings.Language;
            tray.Text = T("arcus — フォルダバー");
            foreach (var session in sessions.Values) session.Bar?.UpdateFolders(session.Folders, settings.Favorites, T("設定を反映しました。開いているフォルダ一覧は固定しています。"));
            Tick();
        });
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    private void Persist() { try { store.Save(settings); Tick(); } catch (Exception e) { Notify(e.Message); } }
    private void Notify(string text) { tray.BalloonTipTitle = "arcus"; tray.BalloonTipText = text; tray.ShowBalloonTip(5000); }
    private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => app.Dispatcher.BeginInvoke(new Action(() => {
        foreach (var session in sessions.Values) session.Bar?.ApplyTheme();
        if (settingsWindow != null) RoundedTheme.Apply(settingsWindow);
    }));
    private void Log(ProbeResponse response)
    {
        try { JsonStore.Write(Path.Combine(store.DirectoryPath, "logs", $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json"), response); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* diagnostic IO must not stop dialog monitoring */ }
    }
    public void Dispose()
    {
        disposed = true; timer.Stop(); monitor.Dispose(); SystemEvents.UserPreferenceChanged -= ThemeChanged;
        foreach (var session in sessions.Values) { session.Closed = true; session.Bar?.Finish(); }
        settingsWindow?.Close(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); trayIcon.Dispose();
    }
}
