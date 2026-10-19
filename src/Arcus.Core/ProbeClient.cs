using System.Diagnostics;
using System.IO;

namespace Arcus;

public static class ProbeClient
{
    public static async Task<ProbeResponse> RunAsync(string executable, ProbeRequest request, TimeSpan timeout)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arcus-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var input = System.IO.Path.Combine(folder, "request.json");
        var output = System.IO.Path.Combine(folder, "response.json");
        Process? process = null;
        try
        {
            JsonStore.Write(input, request);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--worker"); start.ArgumentList.Add(input); start.ArgumentList.Add(output);
            process = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start");
            using var deadline = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(deadline.Token);
            return File.Exists(output) ? JsonStore.Read<ProbeResponse>(output)
                : new(ProbeStatus.Failed, $"Worker exited {process.ExitCode} without a response");
        }
        catch (OperationCanceledException)
        {
            if (process is { HasExited: false }) { process.Kill(true); await process.WaitForExitAsync(); }
            return new(ProbeStatus.TimedOut, "Worker deadline exceeded. A navigation timeout has unknown outcome; inspect the dialog before retrying.");
        }
        catch (Exception e) { return new(ProbeStatus.Failed, $"{e.GetType().Name}: {e.Message}"); }
        finally
        {
            process?.Dispose();
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

public static class ProbeWorker
{
    // Each process performs exactly one request. COM stays on its STA; UIA on a windowless MTA.
    public static int Run(string input, string output)
    {
        ProbeResponse response;
        try
        {
            var request = JsonStore.Read<ProbeRequest>(input);
            if (request.Operation.StartsWith("collect:"))
            {
                IFolderAdapter adapter = request.Operation[8..] switch {
                    "Explorer" => new ExplorerAdapter(), "Files" => new FilesAdapter(), "OneCommander" => new OneCommanderAdapter(),
                    "Filedini" => new FilediniAdapter(), _ => throw new ArgumentException("Unknown adapter") };
                var result = request.Operation == "collect:Explorer" ? adapter.Collect()
                    : Task.Run(adapter.Collect).GetAwaiter().GetResult();
                response = new(result.Status, result.Detail, [result]);
            }
            else response = Task.Run(() => ExecuteUia(request)).GetAwaiter().GetResult();
        }
        catch (Exception e) { response = new(ProbeStatus.Failed, $"{e.GetType().Name}: {e.Message}"); }
        JsonStore.Write(output, response);
        return 0;
    }

    private static ProbeResponse ExecuteUia(ProbeRequest request)
    {
        switch (request.Operation)
        {
            case "windows":
                return new(ProbeStatus.Success, "Visible top-level windows on the calling desktop", Windows: Native.Windows());
            case "dialogs":
                var dialogs = Native.Windows().Where(w => w.Class == "#32770")
                    .Select(AutomationProbe.Inspect).ToList();
                return new(ProbeStatus.Success, "Read-only shell-dialog structure scan", Dialogs: dialogs);
            case "tree":
                Native.GetWindowThreadProcessId((nint)request.Window, out var pid);
                if (pid != request.ProcessId || !Native.IsWindow((nint)request.Window))
                    return new(ProbeStatus.Failed, "Window identity changed");
                var snapshot = AutomationProbe.ReadSnapshot((nint)request.Window);
                return new(snapshot.Status, snapshot.Detail, Nodes: snapshot.Nodes);
            case "dialog":
                var target = Native.Windows().SingleOrDefault(w => w.Handle == request.Window && w.ProcessId == request.ProcessId);
                if (target == null) return new(ProbeStatus.Failed, "Dialog identity changed");
                var inspected = AutomationProbe.InspectStructure(target);
                return new(inspected.SupportedStructure ? ProbeStatus.Success : ProbeStatus.Unsupported, inspected.Reason, Dialogs: [inspected]);
            case "navigate": return DialogNavigator.Navigate(request);
            case "harness-address": return Harness.PrepareAddress(request);
            case "harness-navigate": return Harness.NavigateOwn(request);
            default: return new(ProbeStatus.Unsupported, "Unknown operation");
        }
    }
}
