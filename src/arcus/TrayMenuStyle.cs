using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using static Arcus.UiText;

namespace Arcus.Desktop;

// Keep the context menu light and readable independently of the bar theme.
internal static class TrayMenuStyle
{
    private static readonly Font ReadableFont = new("Yu Gothic UI", 10f, FontStyle.Regular);
    private static readonly ToolStripRenderer Renderer = new ReadableTrayRenderer();
    public static ContextMenuStrip Create(bool enabled, EventHandler toggle, EventHandler settings, EventHandler exit)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, DropShadowEnabled = true };
        var enabledItem = new ToolStripMenuItem(T("フォルダバーを有効にする")) { Checked = enabled, CheckOnClick = true };
        enabledItem.Click += toggle;
        menu.Items.Add(enabledItem);
        menu.Items.Add(T("設定を開く…"), null, settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("arcus を終了"), null, exit);
        menu.Opening += (_, _) => Configure(menu);
        menu.SizeChanged += (_, _) => Round(menu);
        menu.Opened += (_, _) => { Configure(menu); Round(menu); };
        Configure(menu);
        return menu;
    }
    private static void Configure(ContextMenuStrip menu)
    {
        menu.Items[0].Text = T("フォルダバーを有効にする");
        menu.Items[1].Text = T("設定を開く…");
        menu.Items[3].Text = T("arcus を終了");
        menu.Renderer = Renderer; menu.Font = ReadableFont;
        menu.BackColor = MenuPalette.Background; menu.ForeColor = MenuPalette.Text;
        var scale = menu.DeviceDpi / 96f;
        menu.Padding = new Padding((int)(8 * scale), (int)(9 * scale), (int)(8 * scale), (int)(9 * scale));
        var width = (int)(254 * scale);
        foreach (ToolStripItem item in menu.Items)
            width = Math.Max(width, TextRenderer.MeasureText(item.Text, ReadableFont).Width + (int)(62 * scale));
        foreach (ToolStripItem item in menu.Items)
        {
            item.Font = ReadableFont; item.ForeColor = MenuPalette.Text; item.BackColor = MenuPalette.Background;
            item.AutoSize = false;
            item.Size = new Size(width, (int)((item is ToolStripSeparator ? 12 : 34) * scale));
            item.Padding = Padding.Empty;
        }
        menu.MinimumSize = new Size(width + menu.Padding.Horizontal, 0);
    }
    private static void Round(ToolStripDropDown menu)
    {
        if (menu.IsDisposed || menu.Width < 2 || menu.Height < 2) return;
        using var path = RoundedPath(new RectangleF(0, 0, menu.Width, menu.Height), 10 * menu.DeviceDpi / 96f);
        var previous = menu.Region; menu.Region = new Region(path); previous?.Dispose();
    }
    internal static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var result = new GraphicsPath(); var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        result.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        result.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        result.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        result.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        result.CloseFigure(); return result;
    }
}
internal static class MenuPalette
{
    public static Color Background => SystemInformation.HighContrast ? SystemColors.Menu : Color.White;
    public static Color Text => SystemInformation.HighContrast ? SystemColors.MenuText : Color.FromArgb(30, 34, 39);
    public static Color Selection => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(242, 245, 247);
    public static Color SelectedText => SystemInformation.HighContrast ? SystemColors.HighlightText : Text;
    public static Color Border => SystemInformation.HighContrast ? SystemColors.MenuText : Color.FromArgb(220, 227, 232);
    public static Color Check => SystemInformation.HighContrast ? SystemColors.MenuText : Color.FromArgb(0, 142, 154);
}
internal sealed class ReadableTrayRenderer : ToolStripProfessionalRenderer
{
    public ReadableTrayRenderer() => RoundedEdges = false;
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(MenuPalette.Background);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = TrayMenuStyle.RoundedPath(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10 * e.ToolStrip.DeviceDpi / 96f);
        using var border = new Pen(MenuPalette.Border); e.Graphics.DrawPath(border, shape);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var scale = (e.Item.Owner?.DeviceDpi ?? 96) / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var shape = TrayMenuStyle.RoundedPath(new RectangleF(0, 1, e.Item.Width, e.Item.Height - 2), 6 * scale);
            using var fill = new SolidBrush(MenuPalette.Selection); e.Graphics.FillPath(fill, shape);
        }
        if (e.Item is not ToolStripMenuItem { Checked: true }) return;
        using var pen = new Pen(e.Item.Selected && SystemInformation.HighContrast ? MenuPalette.SelectedText : MenuPalette.Check, 1.8f * scale)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var center = e.Item.Height / 2f;
        e.Graphics.DrawLines(pen, new[] { new PointF(10 * scale, center), new PointF(14 * scale, center + 4 * scale), new PointF(21 * scale, center - 4 * scale) });
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { /* drawn in the reserved leading column */ }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var scale = (e.Item.Owner?.DeviceDpi ?? 96) / 96f;
        var inset = (int)(34 * scale);
        e.TextRectangle = new Rectangle(inset, 0, Math.Max(1, e.Item.Width - inset - (int)(14 * scale)), e.Item.Height);
        e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        e.TextColor = !e.Item.Enabled ? SystemColors.GrayText : e.Item.Selected ? MenuPalette.SelectedText : MenuPalette.Text;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        using var pen = new Pen(MenuPalette.Border);
        e.Graphics.DrawLine(pen, 10 * scale, e.Item.Height / 2f, e.Item.Width - 10 * scale, e.Item.Height / 2f);
    }
}
