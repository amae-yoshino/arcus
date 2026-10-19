using System.Diagnostics;
using System.Windows.Automation;

namespace Arcus;

public static class AutomationProbe
{
    // Ignore file rows; capture only the fields required for structure and preservation.
    public static DialogInfo InspectStructure(WindowInfo window) => InspectFields(window, false);
    public static DialogInfo InspectNavigation(WindowInfo window) => InspectFields(window, true);
    private static DialogInfo InspectFields(WindowInfo window, bool includeValues)
    {
        var nodes = new List<UiNode>();
        if (window.Class != "#32770" || !Native.HasChildClass((nint)window.Handle, "DUIViewWndClassName"))
            return new(window, false, "Not a standard shell dialog structure.", nodes);
        var root = AutomationElement.FromHandle((nint)window.Handle);
        var candidates = root.FindAll(TreeScope.Descendants, new OrCondition(
            new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"), new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"))),
            new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "1136"), new PropertyCondition(AutomationElement.AutomationIdProperty, "FileTypeControlHost")))));
        foreach (AutomationElement candidate in candidates)
        {
            var parent = TreeWalker.RawViewWalker.GetParent(candidate);
            var parentNode = parent == null ? null : ReadStructureNode(parent, null, includeValues);
            var node = ReadStructureNode(candidate, parentNode?.RuntimeId, includeValues);
            if (node.Type == "ControlType.Edit" && (parentNode == null ||
                !IsFileName(node, new List<UiNode> { parentNode }))) continue;
            if (parentNode != null && nodes.All(x => x.RuntimeId != parentNode.RuntimeId)) nodes.Add(parentNode);
            nodes.Add(node);
        }
        if (includeValues)
        {
            var addressBands = Native.ChildrenWithClass((nint)window.Handle, "Address Band Root");
            if (addressBands.Count == 1) nodes.Add(ReadStructureNode(AutomationElement.FromHandle(addressBands[0]), null, false));
        }
        var valid = HasUniqueFileFields(nodes);
        return new(window, valid, valid ? "Focused shell/filename/filetype structure verified; file rows omitted."
            : "Required unique filename/filetype structure absent.", nodes);
    }
    public static bool HasUniqueFileFields(List<UiNode> nodes) => nodes.Count(n => IsFileName(n, nodes)) == 1 &&
        nodes.Count(n => n.Type == "ControlType.ComboBox" && n.AutomationId is "1136" or "FileTypeControlHost") == 1;
    private static UiNode ReadStructureNode(AutomationElement element, string? parent, bool includeValues = false)
    {
        var current = element.Current;
        string? value = null, selection = null;
        var writable = false;
        if (includeValues && element.TryGetCurrentPattern(ValuePattern.Pattern, out var v))
        { var pattern = (ValuePattern)v; value = pattern.Current.Value; writable = !pattern.Current.IsReadOnly; }
        if (includeValues && element.TryGetCurrentPattern(SelectionPattern.Pattern, out var s))
            selection = string.Join(" | ", (((SelectionPattern)s).Current.GetSelection() ?? []).Where(x => x != null).Select(x => x.Current.Name));
        return new(string.Join('.', element.GetRuntimeId()), parent, current.ControlType.ProgrammaticName,
            current.Name, current.AutomationId, current.ClassName, current.NativeWindowHandle,
            current.IsOffscreen, value, writable, false, selection);
    }
    public static List<UiNode> Read(nint hwnd) => ReadSnapshot(hwnd).Nodes;
    public static UiaSnapshot ReadSnapshot(nint hwnd)
    {
        var nodes = new List<UiNode>();
        var issues = new List<string>();
        var limited = false;
        var watch = Stopwatch.StartNew();
        Walk(AutomationElement.FromHandle(hwnd), null, 0);
        return new(limited || issues.Count > 0 ? ProbeStatus.Partial : ProbeStatus.Success, nodes,
            $"Raw UIA: {nodes.Count} nodes; truncated={limited}; provider issues={issues.Count}. " + string.Join("; ", issues.Take(8)));
        void Walk(AutomationElement element, string? parent, int depth)
        {
            if (depth > 24 || nodes.Count >= 1500 || watch.Elapsed > TimeSpan.FromSeconds(5)) { limited = true; return; }
            try
            {
                var p = element.Current;
                var id = string.Join('.', element.GetRuntimeId());
                string? value = null; bool writable = false;
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var v))
                { var pattern = (ValuePattern)v; value = pattern.Current.Value; writable = !pattern.Current.IsReadOnly; }
                string? selection = null;
                if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var s))
                    selection = ((SelectionItemPattern)s).Current.IsSelected.ToString();
                if (element.TryGetCurrentPattern(SelectionPattern.Pattern, out var cs))
                    selection = string.Join(" | ", (((SelectionPattern)cs).Current.GetSelection() ?? []).Where(x => x != null).Select(x => x.Current.Name));
                nodes.Add(new(id, parent, p.ControlType?.ProgrammaticName ?? "Unknown", p.Name, p.AutomationId,
                    p.ClassName, p.NativeWindowHandle, p.IsOffscreen, value, writable,
                    element.TryGetCurrentPattern(InvokePattern.Pattern, out _), selection, p.HelpText));
                var walker = TreeWalker.RawViewWalker;
                var child = walker.GetFirstChild(element);
                while (child != null && nodes.Count < 1500 && watch.Elapsed < TimeSpan.FromSeconds(5))
                { Walk(child, id, depth + 1); child = walker.GetNextSibling(child); }
                if (child != null) limited = true;
            }
            catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or
                System.Runtime.InteropServices.COMException or NullReferenceException)
            { issues.Add($"{e.GetType().Name}: {e.Message}"); }
        }
    }

    public static DialogInfo Inspect(WindowInfo window)
    {
        var snapshot = ReadSnapshot((nint)window.Handle);
        var nodes = snapshot.Nodes;
        // Class + shell content + filename edit + file type combo. Folder pickers lack the last pair.
        var shell = nodes.Any(n => n.Class == "DUIViewWndClassName");
        var filename = nodes.Any(n => IsFileName(n, nodes));
        var filetype = nodes.Any(n => n.AutomationId is "1136" or "FileTypeControlHost" && n.Type == "ControlType.ComboBox");
        var supported = snapshot.Status == ProbeStatus.Success && window.Class == "#32770" && shell && filename && filetype;
        return new(window, supported, supported
            ? "Shell file dialog candidate; navigation performs additional address-profile and foreground checks."
            : "Required structure absent or UIA scan incomplete. Read-only inspection only. " + snapshot.Detail, nodes);
    }
    public static bool IsFileName(UiNode node, List<UiNode> nodes) => node.Type == "ControlType.Edit" && node.Class == "Edit" &&
        ((node.AutomationId == "1148" && nodes.Any(p => p.RuntimeId == node.ParentId && p.AutomationId == "1148" && p.Type == "ControlType.ComboBox")) ||
         (node.AutomationId == "1001" && nodes.Any(p => p.RuntimeId == node.ParentId && p.AutomationId == "FileNameControlHost")));
}
