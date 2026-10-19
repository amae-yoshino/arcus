using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Arcus;

public static class Native
{
    public delegate bool EnumProc(nint hwnd, nint parameter);
    public delegate void EventProc(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumProc callback, nint parameter);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, EventProc callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, uint message, nint wparam, nint lparam);
    public static string ClassName(nint hwnd) { var s = new StringBuilder(256); GetClassName(hwnd, s, s.Capacity); return s.ToString(); }
    public static bool HasChildClass(nint hwnd, string expected)
    {
        var found = false;
        EnumChildWindows(hwnd, (child, _) => { if (ClassName(child) != expected) return true; found = true; return false; }, 0);
        return found;
    }
    public static List<nint> ChildrenWithClass(nint hwnd, string expected)
    {
        var found = new List<nint>();
        EnumChildWindows(hwnd, (child, _) => { if (ClassName(child) == expected) found.Add(child); return true; }, 0);
        return found;
    }
    public static List<WindowInfo> Windows(string? classFilter = null)
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) => {
            if (!IsWindowVisible(h)) return true;
            var windowClass = ClassName(h);
            if (classFilter != null && windowClass != classFilter) return true;
            GetWindowThreadProcessId(h, out var pid);
            var title = new StringBuilder(1024); GetWindowText(h, title, title.Capacity);
            string name;
            try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
            catch { name = "<inaccessible>"; }
            list.Add(new(h, pid, name, windowClass, title.ToString()));
            return true;
        }, 0);
        return list;
    }
}

// Install on the WPF dispatcher thread: out-of-context hooks need its message loop.
public sealed class DialogMonitor : IDisposable
{
    private readonly Native.EventProc callback;
    private readonly List<nint> hooks = [];
    public event Action? Changed;
    public DialogMonitor(bool skipOwnProcess = true)
    {
        callback = (_, _, _, obj, child, _, _) => {
            if (obj != 0 || child != 0) return;
            try { Changed?.Invoke(); } catch { /* never cross native boundary */ }
        };
        foreach (var range in new (uint Min, uint Max)[] { (3, 3), (0x8000, 0x8003), (0x800B, 0x800B) })
        {
            var hook = Native.SetWinEventHook(range.Min, range.Max, 0, callback, 0, 0, skipOwnProcess ? 2u : 0u);
            if (hook == 0) { Dispose(); throw new InvalidOperationException("SetWinEventHook failed"); }
            hooks.Add(hook);
        }
    }
    public void Dispose() { foreach (var h in hooks) Native.UnhookWinEvent(h); hooks.Clear(); GC.KeepAlive(callback); }
}
