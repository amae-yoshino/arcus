using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Arcus;

public sealed record FilesSessionResult(ProbeStatus Status, List<FolderInfo> Folders, string Detail,
    string? FilePath = null, DateTime? LastWriteUtc = null);

public static class FilesSessionReader
{
    private const string SupportedFamily = "Files_1y0xx7n9077q4";
    private const int MaxBytes = 4 * 1024 * 1024;
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder name);

    public static FilesSessionResult ReadForRunningProcess(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
        if (process == 0) return new(ProbeStatus.Unavailable, [], "Cannot identify Files package.");
        try
        {
            uint length = 256;
            var family = new StringBuilder((int)length);
            if (GetPackageFamilyName(process, ref length, family) != 0 || family.ToString() != SupportedFamily)
                return new(ProbeStatus.Unsupported, [], "No validated session-file location for this Files package.");
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", family.ToString(), "LocalState", "settings", "user_settings.json");
            try
            {
                var stamp = File.GetLastWriteTimeUtc(path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > MaxBytes) return new(ProbeStatus.Unavailable, [], "Session file exceeds size bound.", path);
                var bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                if (stream.Length != bytes.Length || File.GetLastWriteTimeUtc(path) != stamp)
                    return new(ProbeStatus.Unavailable, [], "Session file changed during read; discarded.", path);
                return Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')) with { FilePath = path, LastWriteUtc = stamp };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            { return new(ProbeStatus.Unavailable, [], $"Cannot read saved tabs: {e.GetType().Name}", path); }
        }
        finally { CloseHandle(process); }
    }

    public static FilesSessionResult Parse(string json)
    {
        var folders = new List<FolderInfo>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("LastSessionTabList", out var tabs) || tabs.ValueKind != JsonValueKind.Array)
                return new(ProbeStatus.Unavailable, [], "Saved tab list absent.");
            if (tabs.GetArrayLength() > 512) return new(ProbeStatus.Unavailable, [], "Saved tab count exceeds bound.");
            var index = 0; var skipped = 0;
            foreach (var tab in tabs.EnumerateArray())
            {
                var tabId = $"saved-tab:{index++}";
                try
                {
                    if (tab.ValueKind != JsonValueKind.String) { skipped++; continue; }
                    using var entry = JsonDocument.Parse(tab.GetString()!);
                    var root = entry.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("InitialPageType", out var page) || page.ValueKind != JsonValueKind.String ||
                        page.GetString() != "Files.App.Views.ShellPanesPage" ||
                        !root.TryGetProperty("NavigationParameter", out var panes) || panes.ValueKind != JsonValueKind.Object)
                    { skipped++; continue; }
                    Add("LeftPaneNavPathParam", "left"); Add("RightPaneNavPathParam", "right");
                    void Add(string property, string pane)
                    {
                        if (!panes.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return;
                        var path = FolderNormalizer.Normalize(value.GetString());
                        if (path == null || path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0) return;
                        // Restore entries cannot be assigned to a current window or active tab.
                        folders.Add(new(path, "Files", 0, tabId, pane, null, null, ProbeStatus.Partial, IsCached: true));
                    }
                }
                catch (JsonException) { skipped++; }
            }
            return new(folders.Count > 0 ? ProbeStatus.Partial : ProbeStatus.Unavailable, folders,
                $"Saved tabs: {tabs.GetArrayLength()}; filesystem panes: {folders.Count}; skipped entries: {skipped}. " +
                "Restore data may be stale or incomplete; not verified current tabs. Saved selection index is ignored.");
        }
        catch (JsonException) { return new(ProbeStatus.Unavailable, [], "Invalid session JSON."); }
    }
}
