using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Arcus;

namespace Arcus.Desktop;
internal static class TrayMenuPreview
{
    public static int Run(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var context = new ApplicationContext();
        using var menu = TrayMenuStyle.Create(true, (_, _) => { }, (_, _) => { }, (_, _) => { });
        using var timer = new Timer { Interval = 150 };
        Exception? failure = null;
        timer.Tick += (_, _) => {
            timer.Stop();
            try
            {
                menu.Items[1].Select(); Capture("tray-menu.png");
                ((ToolStripMenuItem)menu.Items[0]).Checked = false;
                menu.Items[0].Select(); Capture("tray-menu-disabled.png");
                JsonStore.Write(Path.Combine(directory, "tray-menu.json"), new {
                    Status = "Success", Scope = "Actual popup drawing; preview handlers perform no actions",
                    Font = menu.Font.Name, FontSizePt = menu.Font.SizeInPoints, Dpi = menu.DeviceDpi,
                    Width = menu.Width, Height = menu.Height, Rounded = menu.Region != null,
                    Items = menu.Items.Cast<ToolStripItem>().Select(item => new { item.Text, item.Height, item.Width }) });
            }
            catch (Exception e) { failure = e; File.WriteAllText(Path.Combine(directory, "preview-error.txt"), e.ToString()); }
            finally { menu.Close(); context.ExitThread(); }
        };
        menu.Opened += (_, _) => timer.Start();
        menu.Show(new Point(Screen.PrimaryScreen!.WorkingArea.Left + 60, Screen.PrimaryScreen.WorkingArea.Top + 60));
        Application.Run(context);
        return failure == null ? 0 : 1;
        void Capture(string name)
        {
            menu.Refresh();
            using var bitmap = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
            bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
        }
    }
}
