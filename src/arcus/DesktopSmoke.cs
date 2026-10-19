using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Arcus;
using Microsoft.Win32;

namespace Arcus.Desktop;
internal static class DesktopSmoke
{
    internal static int Run(string outputDirectory, bool includeVisuals = false, bool saveDialog = false, string language = "auto")
    {
        var directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        var settingsDirectory = Path.Combine(directory, "settings");
        new SettingsManager(settingsDirectory).Save(new ArcusSettings { Language = language, Favorites = new() {
            new(Environment.CurrentDirectory, "arcus 作業フォルダ"), new(Path.Combine(Environment.CurrentDirectory, "docs"), "ドキュメント") } });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var coordinator = new AppCoordinator(app, settingsDirectory, smokeMode: true);
        var owner = new Window { Title = "arcus smoke test", Width = 400, Height = 120, ShowActivated = false };
        var success = false; var ticks = 0; var finished = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => {
            if (finished) return;
            ticks++;
            try { success = coordinator.CaptureSmoke(directory, includeVisuals); }
            catch (Exception e) { File.WriteAllText(Path.Combine(directory, "smoke-error.txt"), e.ToString()); }
            if (!success && ticks < 35) return;
            finished = true; timer.Stop();
            var dialog = Native.Windows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && w.Class == "#32770");
            if (dialog != null) Native.PostMessage((nint)dialog.Handle, 0x0010, 0, 0);
        };
        owner.Loaded += (_, _) => {
            timer.Start();
            if (saveDialog) new SaveFileDialog { InitialDirectory = Environment.CurrentDirectory, FileName = "arcus-preserve-this.txt", Filter = "Text (*.txt)|*.txt|All (*.*)|*.*", Title = "arcus — 保存ダイアログ検証" }.ShowDialog(owner);
            else new OpenFileDialog { InitialDirectory = Environment.CurrentDirectory, Filter = "Text (*.txt)|*.txt|All (*.*)|*.*", Title = "arcus — 標準ダイアログ検証" }.ShowDialog(owner);
            timer.Stop(); owner.Close(); app.Shutdown();
        };
        owner.Show(); app.Run();
        if (!success) JsonStore.Write(Path.Combine(directory, "desktop-smoke.json"), new ProbeResponse(ProbeStatus.Failed, "Own-dialog bar did not reach a rendered folder snapshot before the smoke deadline."));
        return success ? 0 : 1;
    }
}
