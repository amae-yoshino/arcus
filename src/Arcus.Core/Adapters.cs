using System.Runtime.InteropServices;

namespace Arcus;

public interface IFolderAdapter
{
    string Name { get; }
    AdapterResult Collect();
}

public sealed class ExplorerAdapter : IFolderAdapter
{
    public string Name => "Explorer";
    public AdapterResult Collect()
    {
        var result = new List<FolderInfo>();
        var errors = new List<string>();
        var evidence = new List<ShellItemEvidence>();
        var count = 0;
        object? shell = null; object? windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
            windows = ((dynamic)shell!).Windows();
            count = (int)((dynamic)windows).Count;
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                long? observedHwnd = null; string? observedClass = null; string? observedUrl = null;
                try
                {
                    item = ((dynamic)windows).Item(i);
                    if (item == null) { evidence.Add(new(i, null, null, null, "Null shell item")); continue; }
                    long hwnd = (long)((dynamic)item).HWND;
                    observedHwnd = hwnd; observedClass = Native.ClassName((nint)hwnd);
                    if (observedClass is not ("CabinetWClass" or "ExploreWClass"))
                    { evidence.Add(new(i, hwnd, observedClass, null, "Not an Explorer folder window")); continue; }
                    string location = (string)((dynamic)item).LocationURL;
                    observedUrl = location;
                    // Shell.Windows also registers invisible windows (including test helpers).
                    // IsWindowVisible stays true for minimized windows: those remain eligible.
                    var exists = Native.IsWindow((nint)hwnd);
                    var visible = exists && Native.IsWindowVisible((nint)hwnd);
                    if (!visible)
                    {
                        evidence.Add(new(i, hwnd, observedClass, location, exists ? "Hidden Explorer window excluded" : "Stale Explorer window excluded", exists, visible));
                        continue;
                    }
                    if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || !uri.IsFile)
                    { evidence.Add(new(i, hwnd, observedClass, location, "Location is not an absolute file URL")); continue; }
                    var path = FolderNormalizer.Normalize(uri.LocalPath);
                    if (path == null) { evidence.Add(new(i, hwnd, observedClass, location, "Filesystem path rejected")); continue; }
                    result.Add(new(path, Name, hwnd, $"shell-item:{i}", null, null, null, ProbeStatus.Partial));
                    evidence.Add(new(i, hwnd, observedClass, location, "Filesystem path collected; tab coverage unknown", exists, visible));
                }
                catch (Exception e)
                {
                    errors.Add($"Shell item {i}: {e.GetType().Name}: {e.Message}");
                    evidence.Add(new(i, observedHwnd, observedClass, observedUrl, $"{e.GetType().Name}: {e.Message}"));
                }
                finally { Release(item); }
            }
        }
        finally { Release(windows); Release(shell); }
        return new(Name, result.Count == 0 ? ProbeStatus.Unavailable : ProbeStatus.Partial, result,
            $"Shell.Windows enumerated {count} items; {result.Count} filesystem folders collected. " +
            "Tab identity, active state and all-tab coverage are unverified. " + string.Join("; ", errors), count, evidence);
    }
    private static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj); }
}

// Deliberately read-only until per-application address semantics are verified.
// The UIA tree is evidence; arbitrary absolute-looking text is not a folder source.
public abstract class UiaAdapter : IFolderAdapter
{
    public abstract string Name { get; }
    protected abstract string ProcessName { get; }
    public AdapterResult Collect() => new(Name, ProbeStatus.Unsupported, [],
        $"No validated path selector. Use UIA export on the explicitly selected {ProcessName} window; no tab switching or path guessing.");
}
public sealed class FilediniAdapter : UiaAdapter { public override string Name => "Filedini"; protected override string ProcessName => "Filedini"; }

public sealed class FolderCollector
{
    public static async Task<List<AdapterResult>> CollectAsync(string executable)
    {
        var results = await Task.WhenAll(new[] { "Explorer", "Files", "OneCommander", "Filedini" }.Select(async source =>
        {
            var response = await ProbeClient.RunAsync(executable, new("collect:" + source), TimeSpan.FromSeconds(10));
            return response.Adapters?.SingleOrDefault() ?? new AdapterResult(source, response.Status, [], response.Detail);
        }));
        return results.ToList();
    }
}
