using System.IO;
using System.Text.Json;

namespace Arcus;

public enum ProbeStatus { Success, Partial, Unavailable, Failed, TimedOut, Unsupported }
public sealed record FolderInfo(string Path, string Source, long Window, string? TabId,
    string? PaneId, bool? IsActive, DateTimeOffset? LastUsed, ProbeStatus Status, bool IsCached = false);
public sealed record ShellItemEvidence(int Index, long? Window, string? Class, string? LocationUrl, string Outcome,
    bool? WindowExists = null, bool? WindowVisible = null);
public sealed record AdapterResult(string Source, ProbeStatus Status, List<FolderInfo> Folders, string Detail,
    int? EnumeratedCount = null, List<ShellItemEvidence>? Evidence = null, List<FilesWindowResult>? WindowEvidence = null,
    List<FilesSessionResult>? SavedSessions = null, List<OneCommanderWindowResult>? OneCommanderEvidence = null);
public sealed record WindowInfo(long Handle, uint ProcessId, string Process, string Class, string Title)
{
    public string Display => $"{Process} | 0x{Handle:X} | {Title}";
}
public sealed record UiNode(string RuntimeId, string? ParentId, string Type, string Name,
    string AutomationId, string Class, long Handle, bool Offscreen, string? Value,
    bool Writable, bool Invokable, string? Selection, string? HelpText = null);
public sealed record UiaSnapshot(ProbeStatus Status, List<UiNode> Nodes, string Detail);
public sealed record DialogInfo(WindowInfo Window, bool SupportedStructure, string Reason, List<UiNode> Nodes);
public sealed record ProbeRequest(string Operation, long Window = 0, uint ProcessId = 0,
    string? Path = null, long ProcessStarted = 0);
public sealed record ProbeResponse(ProbeStatus Status, string Detail,
    List<AdapterResult>? Adapters = null, List<DialogInfo>? Dialogs = null,
    List<UiNode>? Nodes = null, List<WindowInfo>? Windows = null);

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Empty JSON");
}

public static class FolderNormalizer
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Never resolve relative paths, shell namespace names, or device paths.
        if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\")) return null;
        if (!(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] is '\\' or '/'))
            && !path.StartsWith(@"\\")) return null;
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (full.StartsWith(@"\\") && full.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2) return null;
            return System.IO.Path.TrimEndingDirectorySeparator(full);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static List<FolderInfo> Merge(IEnumerable<FolderInfo> items) => items
        .Select(x => (Item: x, Normal: Normalize(x.Path))).Where(x => x.Normal != null)
        .GroupBy(x => x.Normal!, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.OrderBy(x => x.Item.IsCached).ThenByDescending(x => x.Item.LastUsed).First().Item with { Path = g.Key })
        .OrderByDescending(x => x.LastUsed.HasValue).ThenByDescending(x => x.LastUsed)
        .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
}
