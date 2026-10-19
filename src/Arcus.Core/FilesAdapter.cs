using System.Windows.Automation;

namespace Arcus;

public sealed record FilesWindowState(string RootId, string[] TabIds, string[] SelectedTabIds,
    string[] VisibleFileListIds, string?[] PathValues);
public sealed record FilesWindowResult(WindowInfo Window, ProbeStatus Status, string Detail,
    FolderInfo? Folder = null, FilesWindowState? Before = null, FilesWindowState? After = null);

public static class FilesPathPolicy
{
    public static FilesWindowResult Evaluate(WindowInfo window, FilesWindowState before, FilesWindowState after)
    {
        FilesWindowResult Reject(string reason) => new(window, ProbeStatus.Unavailable, reason, Before: before, After: after);
        if (!window.Process.Equals("Files", StringComparison.OrdinalIgnoreCase) || window.Class != "WinUIDesktopWin32WindowClass")
            return Reject("Not the observed Files WinUI window profile.");
        if (before.VisibleFileListIds.Length == 0 || after.VisibleFileListIds.Length == 0)
            return Reject("No visible FileList: settings/home/unsupported view or transition. Cached paths are not adopted.");
        if (before.SelectedTabIds.Length != 1 || after.SelectedTabIds.Length != 1 ||
            !before.TabIds.Contains(before.SelectedTabIds[0]) || !after.TabIds.Contains(after.SelectedTabIds[0]))
            return Reject("Cannot identify one selected tab belonging to the Files tab control.");
        if (before.RootId != after.RootId || before.SelectedTabIds[0] != after.SelectedTabIds[0] ||
            !before.TabIds.Order().SequenceEqual(after.TabIds.Order()) ||
            !before.VisibleFileListIds.Order().SequenceEqual(after.VisibleFileListIds.Order()))
            return Reject("Tab/window/pane context changed while reading; snapshot discarded.");
        if (before.PathValues.Length != 1 || after.PathValues.Length != 1)
            return Reject("Ambiguous CurrentPathGet sources; no path guessed.");
        var path = FolderNormalizer.Normalize(before.PathValues[0]);
        var secondPath = FolderNormalizer.Normalize(after.PathValues[0]);
        if (path == null || !string.Equals(path, secondPath, StringComparison.OrdinalIgnoreCase))
            return Reject("CurrentPathGet is not a stable absolute filesystem path.");
        return new(window, ProbeStatus.Partial,
            "Selected-tab/active-pane CurrentPathGet only. Inactive tabs and other panes are not enumerated.",
            new(path, "Files", window.Handle, before.SelectedTabIds[0], null, true, null, ProbeStatus.Partial), before, after);
    }
}

public sealed class FilesAdapter : IFolderAdapter
{
    public string Name => "Files";
    public AdapterResult Collect()
    {
        var evidence = new List<FilesWindowResult>();
        var windows = Native.Windows().Where(w => w.Process.Equals("Files", StringComparison.OrdinalIgnoreCase) &&
            w.Class == "WinUIDesktopWin32WindowClass").ToArray();
        foreach (var window in windows)
        {
            try
            {
                Native.GetWindowThreadProcessId((nint)window.Handle, out var pid);
                if (pid != window.ProcessId) { evidence.Add(new(window, ProbeStatus.Unavailable, "Window identity changed.")); continue; }
                var root = AutomationElement.FromHandle((nint)window.Handle);
                if (root.Current.ProcessId != window.ProcessId)
                { evidence.Add(new(window, ProbeStatus.Unavailable, "UIA process identity differs.")); continue; }
                var before = ReadState(root);
                var after = ReadState(root);
                Native.GetWindowThreadProcessId((nint)window.Handle, out pid);
                if (pid != window.ProcessId || !Native.IsWindowVisible((nint)window.Handle))
                    evidence.Add(new(window, ProbeStatus.Unavailable, "Window disappeared or became hidden."));
                else evidence.Add(FilesPathPolicy.Evaluate(window, before, after));
            }
            catch (Exception e) { evidence.Add(new(window, ProbeStatus.Failed, $"{e.GetType().Name}: {e.Message}")); }
        }
        var folders = evidence.Where(x => x.Folder != null).Select(x => x.Folder!).ToList();
        // One shared package restore file, read once even when multiple windows exist.
        var saved = new List<FilesSessionResult>();
        foreach (var pid in windows.Select(x => x.ProcessId).Distinct())
        {
            var session = FilesSessionReader.ReadForRunningProcess(pid);
            if (session.FilePath != null && saved.Any(x => x.FilePath == session.FilePath)) continue;
            saved.Add(session); folders.AddRange(session.Folders);
        }
        folders = FolderNormalizer.Merge(folders);
        return new(Name, folders.Count > 0 ? ProbeStatus.Partial : ProbeStatus.Unavailable, folders,
            $"{windows.Length} Files windows inspected; {folders.Count} unique folders, including {folders.Count(x => x.IsCached)} saved-only folders. " +
            "Live paths take precedence. Restore data may be stale or incomplete. No tab switching.",
            EnumeratedCount: windows.Length, WindowEvidence: evidence, SavedSessions: saved);
    }
    private static FilesWindowState ReadState(AutomationElement root)
    {
        var tabControls = root.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "HorizontalTabView")));
        if (tabControls.Count != 1) return new(Id(root), [], [], [], []);
        var tabControl = tabControls[0];
        var tabs = tabControl.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
        // WinUI TabView's SelectionPattern can return the inner list item rather than
        // the TabItem peer. Read selection from the actual enumerated tab peers.
        var selected = tabs.Cast<AutomationElement>().Where(x =>
            x.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection) &&
            ((SelectionItemPattern)selection).Current.IsSelected).ToArray();
        var paths = root.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathGet"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "TextBox")));
        // This WinUI provider exposes FileList in RawView but omits it from
        // FindAll's results. Walk structural containers, never file rows.
        var views = new List<AutomationElement>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var visited = 0;
        Walk(root, 0);
        return new(Id(root), tabs.Cast<AutomationElement>().Select(Id).ToArray(), selected.Select(Id).ToArray(),
            views.Where(x => !x.Current.IsOffscreen).Select(Id).ToArray(),
            paths.Cast<AutomationElement>().Select(x => x.TryGetCurrentPattern(ValuePattern.Pattern, out var v)
                ? ((ValuePattern)v).Current.Value : null).ToArray());
        void Walk(AutomationElement element, int depth)
        {
            if (++visited > 1500 || depth > 24 || watch.Elapsed > TimeSpan.FromSeconds(3))
                throw new InvalidOperationException("Files structural scan exceeded its bound.");
            var current = element.Current;
            if (current.AutomationId == "FileList" && current.ControlType == ControlType.List)
            { views.Add(element); return; }
            if (current.ControlType == ControlType.Tree || current.ControlType == ControlType.List) return;
            var child = TreeWalker.RawViewWalker.GetFirstChild(element);
            while (child != null)
            { Walk(child, depth + 1); child = TreeWalker.RawViewWalker.GetNextSibling(child); }
        }
    }
    private static string Id(AutomationElement element) => string.Join('.', element.GetRuntimeId());
}
