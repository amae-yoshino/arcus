using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace Arcus;

public static class DialogNavigator
{
    public static ProbeResponse Navigate(ProbeRequest request) => NavigateCore(request, true);
    internal static ProbeResponse NavigateCore(ProbeRequest request, bool requireForeground)
    {
        var path = FolderNormalizer.Normalize(request.Path);
        if (path == null || !Directory.Exists(path)) return new(ProbeStatus.Failed, "Destination does not exist or is not an absolute filesystem path.");
        var hwnd = (nint)request.Window;
        Process? expectedProcess = null;
        void Guard()
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (!Native.IsWindow(hwnd) || pid != request.ProcessId || (requireForeground && Native.GetForegroundWindow() != hwnd) || Native.IsIconic(hwnd))
                throw new InvalidOperationException($"Target identity/foreground changed (target={hwnd}, foreground={Native.GetForegroundWindow()}, pid={pid}/{request.ProcessId}, iconic={Native.IsIconic(hwnd)}); remaining operations aborted.");
            // Keep a handle to this process instance, so cached start-time validation
            // cannot accidentally accept a later process reusing the same PID.
            if (expectedProcess == null || expectedProcess.HasExited)
                throw new InvalidOperationException("Target process lifetime changed.");
        }
        try
        {
            expectedProcess = Process.GetProcessById((int)request.ProcessId);
            _ = expectedProcess.SafeHandle; // Pin the process instance across all guard checks.
            if (request.ProcessStarted == 0 || expectedProcess.StartTime.ToUniversalTime().Ticks != request.ProcessStarted)
                return new(ProbeStatus.Failed, "Target process lifetime changed; no input sent.");
            Guard();
            var window = Native.Windows("#32770").SingleOrDefault(w => w.Handle == request.Window);
            if (window == null) return new(ProbeStatus.Failed, "Target disappeared.");
            var before = AutomationProbe.InspectNavigation(window);
            if (!before.SupportedStructure) return new(ProbeStatus.Unsupported, before.Reason, Dialogs: [before]);
            var filename = before.Nodes.Single(n => AutomationProbe.IsFileName(n, before.Nodes));
            var type = before.Nodes.Single(n => n.Type == "ControlType.ComboBox" && n.AutomationId is "1136" or "FileTypeControlHost");
            if (filename.Value == null || type.Selection == null)
                return new(ProbeStatus.Unsupported, "Cannot snapshot filename/type safely; no input sent.", Dialogs: [before]);
            var bandNode = before.Nodes.SingleOrDefault(n => n.Class == "Address Band Root" && n.AutomationId == "41477" && n.Handle != 0);
            if (bandNode == null) return new(ProbeStatus.Unsupported, "Observed address-band profile absent.");
            var band = AutomationElement.FromHandle((nint)bandNode.Handle);
            var address = ActivateAddress(band, Guard);
            if (address == null) return new(ProbeStatus.Unsupported, "Address edit did not appear; no text entered.");
            var value = (ValuePattern)address.GetCurrentPattern(ValuePattern.Pattern);
            if (value.Current.IsReadOnly) return new(ProbeStatus.Unsupported, "Address is read-only.");
            Guard(); value.SetValue(path);
            var go = WaitFor(() => band.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "Item 100"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))), Guard);
            if (go == null)
                return new(ProbeStatus.Unsupported, "Address Go action absent; no commit. Address text may have changed.");
            if (TreeWalker.RawViewWalker.GetParent(go)?.Current.ClassName != "ToolbarWindow32" || !go.Current.IsEnabled || go.Current.IsOffscreen)
                return new(ProbeStatus.Unsupported, "Address Go structure differs; no commit.");
            Guard();
            if (!string.Equals(FolderNormalizer.Normalize(value.Current.Value), path, StringComparison.OrdinalIgnoreCase))
                return new(ProbeStatus.Failed, "Address value changed before commit.");
            var toolbar = TreeWalker.RawViewWalker.GetParent(go);
            var bounds = go.Current.BoundingRectangle;
            var toolbarBounds = toolbar.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 1 || !toolbarBounds.Contains(bounds))
                return new(ProbeStatus.Unsupported, "Go button bounds cannot be safely targeted.");
            var click = (nint)(((int)(bounds.Y + bounds.Height / 2 - toolbarBounds.Y) << 16) |
                (int)(bounds.X + bounds.Width / 2 - toolbarBounds.X));
            Guard();
            // InvokePattern on this toolbar failed in the isolated harness. Send a
            // scoped mouse click to the observed Go rectangle in its own toolbar.
            var goHandle = (nint)toolbar.Current.NativeWindowHandle;
            Native.PostMessage(goHandle, 0x0201, 1, click);
            Native.PostMessage(goHandle, 0x0202, 0, click);
            var breadcrumb = WaitFor(() => FindBreadcrumb(band), Guard);
            if (breadcrumb == null) return new(ProbeStatus.Failed, "Navigation not confirmed; dialog left open.");
            var resultingEdit = ActivateAddress(band, Guard);
            var actual = resultingEdit != null && resultingEdit.TryGetCurrentPattern(ValuePattern.Pattern, out var v)
                ? ((ValuePattern)v).Current.Value : null;
            Guard();
            var after = AutomationProbe.InspectNavigation(window);
            if (!after.SupportedStructure)
                return new(ProbeStatus.Failed, "Dialog structure changed during navigation; inspect manually.", Dialogs: [before, after]);
            var afterFilename = after.Nodes.SingleOrDefault(n => AutomationProbe.IsFileName(n, after.Nodes));
            var afterType = after.Nodes.SingleOrDefault(n => n.Type == "ControlType.ComboBox" && n.AutomationId is "1136" or "FileTypeControlHost");
            if (afterFilename?.Value != filename.Value || afterType?.Selection != type.Selection)
                return new(ProbeStatus.Failed, "Filename or file-type preservation check failed. No save/open invoked; inspect dialog manually.", Dialogs: [before, after]);
            if (!string.Equals(FolderNormalizer.Normalize(actual), path, StringComparison.OrdinalIgnoreCase))
                return new(ProbeStatus.Partial, $"Go invoked; filename/type preserved, but resulting path could not be verified: {actual}", Dialogs: [before, after]);
            return new(ProbeStatus.Success, $"Navigated to {actual}; filename and file type preserved; no save/open invoked.", Dialogs: [before, after]);
        }
        catch (Exception e) { return new(ProbeStatus.Failed, $"{e}. No save/open/default action sent."); }
        finally { expectedProcess?.Dispose(); }
    }

    private static AutomationElement? FindBreadcrumb(AutomationElement band) => band.FindFirst(TreeScope.Descendants, new AndCondition(
        new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar),
        new PropertyCondition(AutomationElement.ClassNameProperty, "ToolbarWindow32")));

    private static AutomationElement? ActivateAddress(AutomationElement band, Action guard)
    {
        AutomationElement? FindEdit() => band.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "41477"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.ClassNameProperty, "Edit")));
        var existing = FindEdit();
        if (existing != null && !existing.Current.IsOffscreen) return existing;
        var toolbar = FindBreadcrumb(band);
        if (toolbar == null || toolbar.Current.IsOffscreen || !toolbar.Current.IsEnabled) return null;
        if (TreeWalker.RawViewWalker.GetParent(toolbar)?.Current.ClassName != "Breadcrumb Parent") return null;
        var rectangle = toolbar.Current.BoundingRectangle;
        if (rectangle.Width < 60 || rectangle.Height < 10) return null;
        // Experimentally checked in our own standard Open/Save dialogs. Targeted
        // mouse messages only, never global keyboard input or dialog commands.
        var point = (nint)(((int)(rectangle.Height / 2) << 16) | ((int)rectangle.Width - 12));
        guard();
        var target = (nint)toolbar.Current.NativeWindowHandle;
        Native.PostMessage(target, 0x0201, 1, point);
        Native.PostMessage(target, 0x0202, 0, point);
        return WaitFor(FindEdit, guard);
    }
    private static AutomationElement? WaitFor(Func<AutomationElement?> find, Action guard)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 1600)
        {
            guard(); var element = find(); if (element != null && !element.Current.IsOffscreen) return element;
            Thread.Sleep(elapsed.ElapsedMilliseconds < 160 ? 20 : 60);
        }
        return null;
    }
}
