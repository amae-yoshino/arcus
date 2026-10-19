using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Arcus.Desktop;
internal static class AppIcon
{
    public static Icon CreateTrayIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arcus.Icon")
            ?? throw new InvalidOperationException("arcus icon resource is missing.");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }
    public static BitmapSource CreateWindowImage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Arcus.Icon")
            ?? throw new InvalidOperationException("arcus icon resource is missing.");
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze(); return image;
    }
}
