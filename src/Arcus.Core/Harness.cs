using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows.Automation;

namespace Arcus;

// Creates only our own test dialog, captures its UIA tree, then cancels it.
public static class Harness
{
    public static ProbeResponse NavigateOwn(ProbeRequest request)
    {
        using var owner = Process.GetProcessById((int)request.ProcessId);
        if (owner.ProcessName != "DialogNavigator" || !owner.MainWindowTitle.Contains("arcus isolated diagnostic harness"))
            return new(ProbeStatus.Failed, "Not our isolated harness");
        var result = DialogNavigator.NavigateCore(request, false);
        return result with { Detail = "OWN-HARNESS ONLY (foreground check tested separately): " + result.Detail };
    }
    public static ProbeResponse PrepareAddress(ProbeRequest request)
    {
        using var owner = Process.GetProcessById((int)request.ProcessId);
        // This experimental mouse message is limited to the harness process created by us.
        if (owner.ProcessName != "DialogNavigator" || !owner.MainWindowTitle.Contains("arcus isolated diagnostic harness"))
            return new(ProbeStatus.Failed, "Not our isolated harness");
        Native.GetWindowThreadProcessId((nint)request.Window, out var pid);
        if (pid != request.ProcessId) return new(ProbeStatus.Failed, "Owner mismatch");
        var root = AutomationElement.FromHandle((nint)request.Window);
        var toolbar = root.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar)));
        if (toolbar == null) return new(ProbeStatus.Unsupported, "Observed breadcrumb toolbar absent");
        var rect = toolbar.Current.BoundingRectangle;
        var point = (nint)(((int)(rect.Height / 2) << 16) | ((int)rect.Width - 12));
        Native.PostMessage((nint)toolbar.Current.NativeWindowHandle, 0x0201, 1, point);
        Native.PostMessage((nint)toolbar.Current.NativeWindowHandle, 0x0202, 0, point);
        Thread.Sleep(400);
        var address = root.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "41477"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        if (address != null && request.Path != null && address.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            ((ValuePattern)pattern).SetValue(request.Path);
            Thread.Sleep(400);
        }
        return new(ProbeStatus.Success, "Experimental address activation/value in own harness only; no commit", Nodes: AutomationProbe.Read((nint)request.Window));
    }
    public static int Run(string kind, string report)
    {
        report = System.IO.Path.GetFullPath(report);
        var initial = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!, "initial");
        Directory.CreateDirectory(initial);
        for (var i = 0; i < 12; i++) Directory.CreateDirectory(System.IO.Path.Combine(initial, $"folder-{i:D2}"));
        var app = new Application();
        using var monitor = new DialogMonitor(skipOwnProcess: false);
        var eventCount = 0;
        monitor.Changed += () => Interlocked.Increment(ref eventCount);
        var owner = new Window { Title = "arcus isolated diagnostic harness", Width = 450, Height = 140 };
        owner.Content = new System.Windows.Controls.TextBlock { Text = "検証用ダイアログを採取中。保存・開く操作は実行しません。", TextWrapping = TextWrapping.Wrap };
        Exception? failure = null;
        owner.Loaded += async (_, _) => {
            var capture = Task.Run(async () => {
                WindowInfo? target = null;
                for (var i = 0; i < 40 && target == null; i++)
                {
                    await Task.Delay(100);
                    target = Native.Windows().FirstOrDefault(w => w.ProcessId == Environment.ProcessId && w.Class == "#32770");
                }
                if (target == null) throw new InvalidOperationException("Own dialog did not appear");
                try
                {
                    await Task.Delay(700);
                    using var own = Process.GetCurrentProcess();
                    var operation = kind.EndsWith("nav") ? "harness-navigate" : kind.EndsWith("address") ? "harness-address" : "tree";
                    var timing = Stopwatch.StartNew();
                    var result = await ProbeClient.RunAsync(Environment.ProcessPath!, new(operation, target.Handle, target.ProcessId,
                        Environment.CurrentDirectory, own.StartTime.ToUniversalTime().Ticks), TimeSpan.FromSeconds(15));
                    timing.Stop();
                    JsonStore.Write(report, result with { Detail = result.Detail + $"; WinEvent callbacks observed: {Volatile.Read(ref eventCount)}" });
                    JsonStore.Write(report + ".timing.json", new { result.Status, WorkerRoundTripMs = timing.Elapsed.TotalMilliseconds,
                        Scope = "Own standard dialog only; includes worker startup, navigation and filename/type verification. Foreground check tested separately.",
                        InitialDirectory = initial, Destination = Environment.CurrentDirectory,
                        SnapshotNodeCounts = result.Dialogs?.Select(d => d.Nodes.Count).ToArray() });
                }
                finally
                {
                    Native.GetWindowThreadProcessId((nint)target.Handle, out var pid);
                    if (pid == Environment.ProcessId) Native.PostMessage((nint)target.Handle, 0x0010, 0, 0);
                }
            });
            if (kind.StartsWith("save")) new SaveFileDialog { InitialDirectory = initial, FileName = "arcus-preserve-this.txt", Filter = "Text (*.txt)|*.txt|All (*.*)|*.*" }.ShowDialog(owner);
            else new OpenFileDialog { InitialDirectory = initial, Filter = "Text (*.txt)|*.txt|All (*.*)|*.*" }.ShowDialog(owner);
            try { await capture; } catch (Exception e) { failure = e; }
            owner.Close();
        };
        app.Run(owner);
        if (failure == null) return 0;
        File.WriteAllText(report + ".error.txt", failure.ToString()); return 1;
    }
}
