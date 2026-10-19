using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace Arcus;

public sealed record OneCommanderWindowState(string RootId, string[] BrowserIds, string[] PathIds, string?[] PathValues);
public sealed record OneCommanderWindowResult(WindowInfo Window, ProbeStatus Status, string Detail,
    FolderInfo? Folder = null, OneCommanderWindowState? Before = null, OneCommanderWindowState? After = null);

public sealed class OneCommanderAdapter : IFolderAdapter
{
    public string Name => "OneCommander";
    public static bool IsWindowProfile(WindowInfo window) => window.Process.Equals("OneCommander", StringComparison.OrdinalIgnoreCase) &&
        window.Class.StartsWith("HwndWrapper[OneCommander.exe;", StringComparison.OrdinalIgnoreCase);
    public AdapterResult Collect()
    {
        var windows = Native.Windows().Where(IsWindowProfile).ToArray();
        var evidence = new List<OneCommanderWindowResult>();
        var saved = new List<FilesSessionResult>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            try
            {
                Native.GetWindowThreadProcessId((nint)window.Handle, out var pid);
                if (pid != window.ProcessId) continue;
                var root = AutomationElement.FromHandle((nint)window.Handle);
                if (root.Current.ProcessId != pid) continue;
                var before = ReadState(root); var after = ReadState(root);
                Native.GetWindowThreadProcessId((nint)window.Handle, out var afterPid);
                if (afterPid != pid || !Native.IsWindowVisible((nint)window.Handle))
                    evidence.Add(new(window, ProbeStatus.Unavailable, "Window identity/visibility changed."));
                else evidence.Add(Evaluate(window, before, after));
            }
            catch (Exception e) { evidence.Add(new(window, ProbeStatus.Failed, "CurrentPathGet: " + e.GetType().Name)); }
        }
        foreach (var pid in windows.Select(x => x.ProcessId).Distinct())
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                var executable = process.MainModule?.FileName;
                if (executable == null) continue;
                var directory = ResolveSettingsDirectory(executable);
                if (directory == null || !directories.Add(directory)) continue;
                var files = Directory.EnumerateFiles(directory, "Tabs*.json")
                    .Where(x => IsSavedTabsFile(Path.GetFileName(x))).Order(StringComparer.OrdinalIgnoreCase).Take(129).ToArray();
                if (files.Length > 128) { saved.Add(new(ProbeStatus.Unavailable, [], "Saved tabs file count exceeds bound.")); continue; }
                foreach (var file in files) saved.Add(ReadSavedFile(file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            { saved.Add(new(ProbeStatus.Unavailable, [], "OneCommander saved tabs unavailable: " + e.GetType().Name)); }
        }
        if (!windows.Any(w => Native.IsWindowVisible((nint)w.Handle) &&
            Native.GetWindowThreadProcessId((nint)w.Handle, out var pid) != 0 && pid == w.ProcessId))
            return new(Name, ProbeStatus.Unavailable, [], "No stable visible OneCommander window.", OneCommanderEvidence: evidence);
        var folders = FolderNormalizer.Merge(evidence.Where(x => x.Folder != null).Select(x => x.Folder!).Concat(saved.SelectMany(x => x.Folders)));
        return new(Name, folders.Count > 0 ? ProbeStatus.Partial : ProbeStatus.Unavailable, folders,
            $"{windows.Length} OneCommander windows; {folders.Count} unique folders, including {folders.Count(x => x.IsCached)} saved-only folders. " +
            "CurrentPathGet is read-only; saved tabs may be stale and cannot establish all current tabs/panes. RecentFolders is not used.",
            EnumeratedCount: windows.Length, SavedSessions: saved, OneCommanderEvidence: evidence);
    }

    private static OneCommanderWindowState ReadState(AutomationElement root)
    {
        var paths = root.FindAll(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "CurrentPathGet"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "TextBox")));
        var browsers = root.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "BrowserUC"));
        return new(Id(root), browsers.Cast<AutomationElement>().Where(x => !x.Current.IsOffscreen).Select(Id).ToArray(),
            paths.Cast<AutomationElement>().Select(Id).ToArray(), paths.Cast<AutomationElement>().Select(x =>
                x.TryGetCurrentPattern(ValuePattern.Pattern, out var value) ? ((ValuePattern)value).Current.Value : null).ToArray());
    }
    public static OneCommanderWindowResult Evaluate(WindowInfo window, OneCommanderWindowState before, OneCommanderWindowState after)
    {
        OneCommanderWindowResult Reject(string detail) => new(window, ProbeStatus.Unavailable, detail, Before: before, After: after);
        if (!IsWindowProfile(window)) return Reject("Not the observed OneCommander WPF profile.");
        if (before.BrowserIds.Length == 0 || after.BrowserIds.Length == 0 || before.RootId != after.RootId ||
            !before.BrowserIds.Order().SequenceEqual(after.BrowserIds.Order())) return Reject("Browser context absent or changed.");
        if (before.PathIds.Length != 1 || after.PathIds.Length != 1 || before.PathIds[0] != after.PathIds[0] ||
            before.PathValues.Length != 1 || after.PathValues.Length != 1) return Reject("CurrentPathGet absent, ambiguous or replaced.");
        var path = SafePath(before.PathValues[0]);
        if (path == null || !string.Equals(path, SafePath(after.PathValues[0]), StringComparison.OrdinalIgnoreCase))
            return Reject("CurrentPathGet is not a stable absolute filesystem path.");
        return new(window, ProbeStatus.Partial, "Current folder from the public CurrentPathGet UIA field; tab and pane identities not exposed.",
            new(path, "OneCommander", window.Handle, null, null, true, null, ProbeStatus.Partial), before, after);
    }

    private static string? ResolveSettingsDirectory(string executable)
    {
        var beside = Path.Combine(Path.GetDirectoryName(executable)!, "Settings");
        if (File.Exists(Path.Combine(beside, "OneCommanderV3.json"))) return beside;
        if (executable.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
        {
            var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneCommander", "Settings");
            return File.Exists(Path.Combine(store, "OneCommanderV3.json")) ? store : null;
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OneCommander", "OneCommander.exe");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneCommander", "Settings");
        return string.Equals(executable, installed, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(local, "OneCommanderV3.json")) ? local : null;
    }
    public static bool IsSavedTabsFile(string name) => Regex.IsMatch(name, @"^Tabs[^\\/]*[12]\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static FilesSessionResult ReadSavedFile(string file)
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4 * 1024 * 1024) return new(ProbeStatus.Unavailable, [], "Saved tabs file exceeds size bound.", file);
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            if (stream.Length != bytes.Length || File.GetLastWriteTimeUtc(file) != stamp)
                return new(ProbeStatus.Unavailable, [], "Saved tabs changed during read.", file);
            return ParseSavedTabs(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), Path.GetFileName(file)) with { FilePath = file, LastWriteUtc = stamp };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new(ProbeStatus.Unavailable, [], "Saved tabs read failed: " + e.GetType().Name, file); }
    }
    public static FilesSessionResult ParseSavedTabs(string json, string fileName)
    {
        if (!IsSavedTabsFile(fileName)) return new(ProbeStatus.Unavailable, [], "Not a OneCommander saved-tabs filename.");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 512)
                return new(ProbeStatus.Unavailable, [], "Invalid saved-tabs array.");
            var folders = new List<FolderInfo>(); var index = 0;
            var pane = fileName.EndsWith("1.json", StringComparison.OrdinalIgnoreCase) ? "browser:1" : "browser:2";
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var tabId = $"saved:{fileName}:{index++}";
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("IsFolder", out var folder) || folder.ValueKind != JsonValueKind.True ||
                    !entry.TryGetProperty("FullPath", out var value) || value.ValueKind != JsonValueKind.String) continue;
                var path = SafePath(value.GetString());
                if (path != null) folders.Add(new(path, "OneCommander", 0, tabId, pane, null, null, ProbeStatus.Partial, IsCached: true));
            }
            return new(folders.Count > 0 ? ProbeStatus.Partial : ProbeStatus.Unavailable, folders,
                "Saved tabs only. Entries may describe closed windows or older sessions; activity and window association are not inferred.");
        }
        catch (JsonException) { return new(ProbeStatus.Unavailable, [], "Invalid saved-tabs JSON."); }
    }
    private static string? SafePath(string? value)
    {
        var path = FolderNormalizer.Normalize(value);
        return path != null && path.IndexOfAny(['*', '?', '"', '<', '>', '|']) < 0 ? path : null;
    }
    private static string Id(AutomationElement element) => string.Join('.', element.GetRuntimeId());
}
