using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Arcus.Desktop;
internal static class RoundedTheme
{
    public static void Apply(Window window, bool? darkOverride = null)
    {
        if (!window.Resources.Contains("Arcus.ThemeLoaded"))
        {
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/arcus;component/RoundedTheme.xaml", UriKind.Relative) });
            window.Resources["Arcus.ThemeLoaded"] = true;
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = darkOverride ?? (key?.GetValue("AppsUseLightTheme") is int value && value == 0);
        var values = dark
            ? new[] { "#191E25", "#222931", "#2B414A", "#EAF1F5", "#9FAEBA", "#44D0D2", "#394650" }
            : new[] { "#F5F8FA", "#FFFFFF", "#DCF3F4", "#21313D", "#637583", "#008E9A", "#DCE5EA" };
        var names = new[] { "Background", "Surface", "Hover", "Text", "Muted", "Accent", "Stroke" };
        for (var i = 0; i < names.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i])); brush.Freeze();
            window.Resources["Arcus." + names[i]] = brush;
        }
        window.SetResourceReference(Window.BackgroundProperty, "Arcus.Background");
        window.SetResourceReference(Window.ForegroundProperty, "Arcus.Text");
        window.FontFamily = new FontFamily("Yu Gothic UI"); window.FontSize = 12;
        window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
    }
}
