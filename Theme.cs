using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Deskcal;

/// <summary>
/// Bang mau va helper ve. Lay tinh than Notion: nen trung tinh, vien cuc nhat, mot
/// mau nhan duy nhat, bo goc nho. Doc lai theme he thong moi lan ve nen doi sang/toi
/// trong Settings la thay ngay.
/// </summary>
public static class Theme
{
    public static bool Dark { get; private set; }

    static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    // ---- nen ----
    public static Color FormBg    => Dark ? C(25, 25, 25)    : C(255, 255, 255);
    public static Color FieldBg   => Dark ? C(37, 37, 37)    : C(255, 255, 255);
    public static Color MenuBg    => Dark ? C(37, 37, 37)    : C(255, 255, 255);
    public static Color CellBg    => Dark ? C(30, 30, 30)    : C(255, 255, 255);
    public static Color OutsideBg => Dark ? C(23, 23, 23)    : C(252, 252, 251);

    // ---- muc, vien ----
    public static Color Text   => Dark ? C(233, 233, 231) : C(55, 53, 47);
    public static Color Muted  => Dark ? C(155, 155, 152) : C(120, 119, 116);
    public static Color Faint  => Dark ? C(110, 110, 108) : C(160, 159, 155);
    public static Color Border => Dark ? C(47, 47, 47)    : C(232, 232, 229);
    public static Color Grid   => Dark ? C(41, 41, 41)    : C(238, 238, 236);

    // ---- trang thai ----
    public static Color Hover => Dark ? C(43, 43, 43) : C(241, 241, 239);
    public static Color Press => Dark ? C(52, 52, 52) : C(232, 232, 229);

    // ---- nhan ----
    public static Color Accent   => Dark ? C(45, 140, 240) : C(35, 131, 226);
    public static Color OnAccent => Color.White;
    public static Color Danger   => Dark ? C(255, 115, 105) : C(212, 76, 71);

    // ---- cot cuoi tuan ----
    public static Color SunTint => Dark ? C(31, 26, 26) : C(253, 251, 250);
    public static Color SatTint => Dark ? C(24, 27, 31) : C(250, 251, 253);
    public static Color SunText => Dark ? C(255, 138, 130) : C(193, 76, 71);
    public static Color SatText => Dark ? C(130, 175, 235) : C(56, 118, 158);

    public static Color Separator() => Border;

    /// <summary>Mau chu / vach cua nhan.</summary>
    public static Color TagColor(string tag) => tag switch
    {
        "work"     => Dark ? C(255, 115, 105) : C(212, 76, 71),
        "ops"      => Dark ? C(255, 163, 68)  : C(203, 113, 54),
        "deadline" => Dark ? C(178, 148, 255) : C(144, 101, 176),
        "personal" => Dark ? C(77, 190, 133)  : C(68, 131, 97),
        _ => Muted,
    };

    /// <summary>Nen pastel cua chip su kien — kieu mau nhan cua Notion.</summary>
    public static Color TagFill(string tag) => tag switch
    {
        "work"     => Dark ? C(56, 35, 33) : C(253, 235, 236),
        "ops"      => Dark ? C(55, 43, 29) : C(250, 235, 221),
        "deadline" => Dark ? C(46, 39, 61) : C(244, 240, 247),
        "personal" => Dark ? C(27, 46, 37) : C(237, 243, 236),
        _ => Hover,
    };

    public static void Refresh()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            Dark = k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { Dark = false; }
    }

    // ---------- font ----------

    static readonly Dictionary<(float, FontStyle), Font> Cache = [];

    /// <summary>
    /// Segoe UI la font UI chuan cua Windows va phu day du tieng Viet. Tranh
    /// "Segoe UI Variable Text": do la variable font, ma TextRenderer ve bang GDI —
    /// GDI khong ho tro variable font, khong nen dua vao.
    /// </summary>
    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        if (Cache.TryGetValue((size, style), out var hit)) return hit;
        foreach (var name in (string[])["Segoe UI", "Tahoma"])
        {
            try
            {
                var f = new Font(new FontFamily(name), size, style);
                Cache[(size, style)] = f;
                return f;
            }
            catch { /* khong co font nay, thu cai tiep theo */ }
        }
        var fallback = new Font(SystemFonts.MenuFont!.FontFamily, size, style);
        Cache[(size, style)] = fallback;
        return fallback;
    }

    // ---------- ve ----------

    /// <summary>
    /// Chu y: dung r.Width-1 / r.Height-1 khi stroke, khong thi net phai va net duoi
    /// bi ve tran ra ngoai control va mat mot nua do antialias.
    /// </summary>
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(0, radius) * 2;
        var p = new GraphicsPath();
        if (d <= 0 || r.Width <= d || r.Height <= d) { p.AddRectangle(r); return p; }
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
        g.SmoothingMode = old;
    }

    public static void StrokeRound(Graphics g, Rectangle r, int radius, Color line, float width = 1f)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var inner = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
        using var path = Rounded(inner, radius);
        using var p = new Pen(line, width);
        g.DrawPath(p, path);
        g.SmoothingMode = old;
    }

    /// <summary>Ve dau tick bang net thay vi dung glyph font.</summary>
    public static void Tick(Graphics g, Rectangle r, Color color, float weight)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, weight) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen,
        [
            new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.73f),
            new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.28f),
        ]);
        g.SmoothingMode = old;
    }

    /// <summary>Mui nhon ve bang net. dir: 1 = xuong, -1 = len.</summary>
    public static void Chevron(Graphics g, Rectangle r, Color color, int dir, float weight = 1.4f)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        float w = r.Width * 0.28f, h = r.Height * 0.16f * dir;
        using var pen = new Pen(color, weight) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen,
        [
            new PointF(cx - w, cy - h),
            new PointF(cx, cy + h),
            new PointF(cx + w, cy - h),
        ]);
        g.SmoothingMode = old;
    }

    /// <summary>Mui nhon ngang, ve bang net. dir: -1 = trai, 1 = phai.</summary>
    public static void ChevronH(Graphics g, Rectangle r, Color color, int dir, float weight = 1.6f)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        float w = r.Width * 0.16f * dir, h = r.Height * 0.22f;
        using var pen = new Pen(color, weight) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen,
        [
            new PointF(cx - w, cy - h),
            new PointF(cx + w, cy),
            new PointF(cx - w, cy + h),
        ]);
        g.SmoothingMode = old;
    }

    /// <summary>Icon dong ho: vong tron + hai kim.</summary>
    public static void ClockGlyph(Graphics g, Rectangle r, Color color, float weight = 1.3f)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = Math.Min(r.Width, r.Height) - 2;
        var face = new Rectangle(r.Left + (r.Width - d) / 2, r.Top + (r.Height - d) / 2, d, d);
        using var pen = new Pen(color, weight) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(pen, face);
        float cx = face.Left + face.Width / 2f, cy = face.Top + face.Height / 2f;
        g.DrawLine(pen, cx, cy, cx, cy - face.Height * 0.28f);          // kim phut
        g.DrawLine(pen, cx, cy, cx + face.Width * 0.22f, cy);           // kim gio
        g.SmoothingMode = old;
    }

    public const TextFormatFlags Flat =
        TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping | TextFormatFlags.SingleLine;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Title bar toi theo theme. DWMWA_USE_IMMERSIVE_DARK_MODE = 20.</summary>
    public static void ApplyTitleBar(IntPtr hwnd)
    {
        try
        {
            int on = Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        }
        catch { /* Windows cu khong co, bo qua */ }
    }
}

/// <summary>
/// Renderer phang cho menu tray. Bo dai gradient xam o le trai, bo highlight xanh
/// vien vuong, doi sang o bo tron mo giong menu Win11.
/// </summary>
public sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    public MenuRenderer() : base(new Palette()) => RoundedEdges = false;

    sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.MenuBg;
        public override Color ImageMarginGradientBegin => Theme.MenuBg;
        public override Color ImageMarginGradientMiddle => Theme.MenuBg;
        public override Color ImageMarginGradientEnd => Theme.MenuBg;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Hover;
        public override Color MenuItemPressedGradientMiddle => Theme.Hover;
        public override Color MenuItemPressedGradientEnd => Theme.Hover;
        public override Color SeparatorDark => Theme.Separator();
        public override Color SeparatorLight => Theme.Separator();
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.MenuBg);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool dim = !e.Item.Enabled || (e.Item.Tag as string) == "done";
        e.TextColor = dim ? Theme.Muted : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var b = e.Item.Bounds;
        Theme.FillRound(e.Graphics, new Rectangle(3, 1, b.Width - 6, b.Height - 2), 5, Theme.Hover);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) =>
        Theme.Tick(e.Graphics, e.ImageRectangle, Theme.Accent, 1.9f);

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var b = e.Item.Bounds;
        using var pen = new Pen(Theme.Separator());
        e.Graphics.DrawLine(pen, 10, b.Height / 2, b.Width - 10, b.Height / 2);
    }
}
