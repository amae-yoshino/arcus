using static Arcus.UiText;
using System;
using System.Threading;
using System.Windows;
using Arcus;
using System.Runtime.CompilerServices;

namespace Arcus.Desktop;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--worker") return ProbeWorker.Run(args[1], args[2]);
        return RunDesktop(args);
    }
    // Shell-only workers should not JIT the desktop/tray startup path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunDesktop(string[] args)
    {
        if (args.Length == 3 && args[0] == "--menu-smoke" && args[2] is "ja" or "en") { UiText.Language = args[2]; return TrayMenuPreview.Run(args[1]); }
        if (args.Length == 3 && args[0] == "--visual-smoke" && args[2] is "ja" or "en") return DesktopSmoke.Run(args[1], includeVisuals: true, language: args[2]);
        if (args.Length == 2 && args[0] == "--menu-smoke") return TrayMenuPreview.Run(args[1]);
        if (args.Length == 2 && args[0] == "--smoke") return DesktopSmoke.Run(args[1]);
        if (args.Length == 2 && args[0] == "--visual-smoke") return DesktopSmoke.Run(args[1], includeVisuals: true);
        if (args.Length == 2 && args[0] == "--smoke-save") return DesktopSmoke.Run(args[1], saveDialog: true);
        using var singleton = new Mutex(true, @"Local\arcus.Desktop", out var created);
        if (!created) { UiText.Language = new SettingsManager().Load().Settings.Language; MessageBox.Show(T("arcus は既に起動しています。タスクトレイから設定を開いてください。"), "arcus"); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var coordinator = new AppCoordinator(app);
        app.Run();
        return 0;
    }
}
