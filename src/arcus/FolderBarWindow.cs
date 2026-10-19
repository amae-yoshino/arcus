using static Arcus.UiText;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Arcus;
using System.Windows.Input;

namespace Arcus.Desktop;
internal sealed class FolderBarWindow : Window
{
    private readonly nint target;
    private readonly StackPanel content = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 3, 6, 3) };
    private readonly List<Button> buttons = new();
    private readonly Action<string> navigate;
    private int maxHeight;
    private bool disposed;
    private PixelRect? lastPlacement;
    private uint lastDpi;
    public FolderBarWindow(nint dialog, ArcusSettings settings, Action<string> onNavigate)
    {
        target = dialog; navigate = onNavigate; maxHeight = settings.MaximumBarHeight;
        Title = "arcus"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; SizeToContent = SizeToContent.Manual;
        Width = 700; Height = 110;
        var shell = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content } };
        shell.SetResourceReference(Border.BorderBrushProperty, "Arcus.Stroke");
        shell.SetResourceReference(Border.BackgroundProperty, "Arcus.Background"); Content = shell;
        SourceInitialized += (_, _) => {
            var helper = new WindowInteropHelper(this); helper.Owner = target;
            var style = DesktopNative.GetWindowLongPtr(helper.Handle, -20);
            DesktopNative.SetWindowLongPtr(helper.Handle, -20, (nint)((long)style | 0x08000000 | 0x00000080));
            HwndSource.FromHwnd(helper.Handle)?.AddHook((nint hwnd, int message, nint wp, nint lp, ref bool handled) => {
                if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE
                return 0;
            });
        };
        UpdateFolders([], settings.Favorites, T("開いているフォルダを取得中…"));
        new WindowInteropHelper(this).EnsureHandle();
    }
    public void UpdateFolders(List<FolderInfo> folders, List<Favorite> favorites, string detail)
    {
        content.Children.Clear(); buttons.Clear();
        AddSection(T("エクスプローラーで開いているフォルダ"), folders.Where(x => !x.IsCached && x.Source == "Explorer").Select(x => x.Path).ToList(), null);
        foreach (var source in folders.Where(x => !x.IsCached && x.Source != "Explorer" && x.Source != "OneCommander").GroupBy(x => x.Source))
            AddSection(T("{0}で開いているフォルダ", source.Key), source.Select(x => x.Path).ToList(), null);
        var oneCommander = FolderNormalizer.Merge(folders.Where(x => x.Source == "OneCommander"));
        if (oneCommander.Count > 0)
            AddSection(T("{0}で開いているフォルダ", "OneCommander"), oneCommander.Select(x => x.Path).ToList(), null,
                oneCommander.Select(x => x.IsCached ? x.Path + "\n" + T("保存済みタブの情報です。現在の状態と異なる場合があります。") : x.Path).ToList());
        foreach (var saved in folders.Where(x => x.IsCached && x.Source != "OneCommander").GroupBy(x => x.Source))
            AddSection(T("{0}の復元データ（古い情報の場合があります）", saved.Key), saved.Select(x => x.Path).ToList(), null);
        AddSection(T("お気に入り"), favorites.Select(x => x.Path).ToList(), favorites.Select(x => x.DisplayName).ToList());
        status.Text = detail; status.FontSize = 10; status.Margin = new Thickness(6, 2, 6, 2);
        status.SetResourceReference(TextBlock.ForegroundProperty, "Arcus.Muted"); content.Children.Add(status); ApplyTheme();
    }
    public void SetStatus(string text, bool enabled)
    { status.Text = text; foreach (var button in buttons) button.IsEnabled = enabled; }
    private void AddSection(string title, List<string> paths, List<string>? names, List<string>? toolTips = null)
    {
        var heading = new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 4, 6, 2) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Arcus.Accent"); content.Children.Add(heading);
        var wrap = new WrapPanel { Margin = new Thickness(3, 0, 3, 0) }; content.Children.Add(wrap);
        var labels = FolderLabels.Create(paths);
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i]; var label = names != null && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : labels[i];
            var caption = new StackPanel { Orientation = Orientation.Horizontal };
            // Include the stroke beyond x=15 inside the layout slot (15 + 0.6 DIP).
            var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M1,3 L6,3 8,5 15,5 15,13 1,13 Z"), Width = 16, Height = 16,
                StrokeThickness = 1.2, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Arcus.Accent"); caption.Children.Add(icon);
            caption.Children.Add(new TextBlock { Text = label, MaxWidth = 204, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Height = 32, MaxWidth = 260, MinWidth = 42, Padding = new Thickness(12, 2, 12, 2),
                Margin = new Thickness(3), ToolTip = toolTips?[i] ?? path, Content = caption, Focusable = false };
            KeyboardNavigation.SetIsTabStop(button, false);
            button.Click += (_, _) => navigate(path); buttons.Add(button); wrap.Children.Add(button);
        }
        if (paths.Count == 0) wrap.Children.Add(new TextBlock { Text = T("なし"), Margin = new Thickness(6, 2, 6, 2), Opacity = 0.65 });
    }
    public void ApplyTheme() => RoundedTheme.Apply(this);
    public void Reposition(bool visible, int maximumHeight)
    {
        if (disposed) return;
        maxHeight = maximumHeight;
        if (!visible || !DesktopNative.GetWindowRect(target, out var rect)) { Hide(); return; }
        var work = DesktopNative.WorkArea(target);
        var dpi = Math.Max(96u, DesktopNative.GetDpiForWindow(target)) / 96d;
        content.Measure(new Size(Math.Max(1, Math.Min(rect.Pixels.Width, work.Width) / dpi - 22), double.PositiveInfinity));
        var desired = (int)Math.Ceiling(Math.Min(maxHeight, content.DesiredSize.Height + 20) * dpi);
        var placement = BarPlacement.Calculate(rect.Pixels, work, desired);
        if (placement.Height < 24 * dpi) { Hide(); return; }
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (lastPlacement != placement || lastDpi != DesktopNative.GetDpiForWindow(target))
        {
            DesktopNative.SetWindowPos(hwnd, 0, placement.Left, placement.Top, placement.Width, placement.Height, 0x0010 | 0x0004);
            DesktopNative.RoundWindow(hwnd, placement.Width, placement.Height, (int)Math.Round(14 * dpi));
            lastPlacement = placement; lastDpi = DesktopNative.GetDpiForWindow(target);
        }
    }
    public void Finish() { disposed = true; Close(); }
}
