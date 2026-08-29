using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Deskcal;

/// <summary>
/// Ve lich thanh mot panel roi ghep len anh nen hien co, va dat ket qua lam wallpaper.
///
/// Chon cach nay thay vi reparent cua so vao WorkerW (kieu Lively / Wallpaper Engine):
/// WorkerW la hack dua vao noi bo Explorer, vo moi lan Explorer restart hoac Windows
/// update, va nam duoi lop icon nen cung khong bam duoc. Lich mot thang thi mot ngay
/// chi doi vai lan — mot tam anh sinh lai la du, va no khong bao gio vo.
///
/// Tuong tac van o icon khay va cua so lich nhu cu; wallpaper chi de liec.
/// </summary>
public static class Wallpaper
{
    const string Key = @"Software\deskcal";
    const int SPI_SETDESKWALLPAPER = 0x0014;
    const int SPIF_UPDATEINIFILE = 0x01;
    const int SPIF_SENDCHANGE = 0x02;

    // ---- ti le panel, doi o day neu muon bo cuc khac ----

    /// <summary>
    /// Panel chiem bao nhieu be ngang / chieu cao man hinh. Ti le 0.52/0.60 tren man
    /// 16:9 cho ra khung ~1.54:1, gan bang cua so lich that (1404x918) — de hep hon thi
    /// o ngay bi bop ngang va ten viec cut thanh "Hop t...".
    /// </summary>
    const float PanelW = 0.52f, PanelH = 0.60f;

    /// <summary>Cach mep phai va can giua theo chieu doc.</summary>
    const float MarginRight = 0.04f;

    /// <summary>
    /// Chieu cao LOGIC cua lich, truoc khi nhan theo man hinh. Chot con so nay roi suy
    /// ra scale (thay vi chot scale) thi 4K va 1440p ra dung cung mot bo cuc, chi khac
    /// do net — do la ly do khong hardcode kich thuoc o dau ca.
    /// </summary>
    const float LogicalH = 900f;

    // Panel ve kin, khong lam trong suot. Da thu: de trong thi vung sang tren anh nen
    // (den thanh pho, may trang) xuyen len va chu gan nhu khong doc duoc. Doc duoc quan
    // trong hon dep, va anh nen van con thay o ba phan tu man hinh con lai.

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfoW(int action, int param, string? value, int winIni);

    // ---------- trang thai ----------

    /// <summary>Nguoi dung da bat che do nay chua.</summary>
    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("WallpaperMode") is int v && v == 1;
        }
    }

    /// <summary>
    /// Anh nen goc, luu lai luc bat de con duong ma tra ve. Phai giu rieng: neu cu doc
    /// wallpaper hien tai lam nen thi lan sinh thu hai se ghep panel len chinh anh da
    /// co panel, chong nhau dan mai.
    /// </summary>
    static string? Backup
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("WallpaperBackup") as string;
        }
    }

    static string Output => Path.Combine(Paths.Dir, "wallpaper.bmp");

    /// <summary>
    /// Anh dung lam nen. Uu tien ban backup; chua co thi lay wallpaper dang dat, mien
    /// no khong phai anh chinh minh sinh ra (ghep panel len anh da co panel thi chong
    /// nhau dan mai).
    /// </summary>
    static string? Source
    {
        get
        {
            if (Backup is { Length: > 0 } b && File.Exists(b)) return b;
            var now = CurrentWallpaper();
            if (now.Length > 0 && !string.Equals(now, Output, StringComparison.OrdinalIgnoreCase)
                && File.Exists(now)) return now;
            return null;
        }
    }

    /// <summary>
    /// Sinh anh ra file ma KHONG dat lam wallpaper — de xem thu bo cuc truoc khi bat.
    /// Goi bang `deskcal.exe --wallpaper-preview out.bmp [rong cao]`.
    /// </summary>
    public static void Preview(Store store, Size screen, string path)
    {
        Theme.Refresh();
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var canvas = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            DrawBackdrop(g, screen);
            DrawPanel(g, store, screen, today);
        }
        canvas.Save(path, ImageFormat.Bmp);
    }

    static string CurrentWallpaper()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return k?.GetValue("Wallpaper") as string ?? "";
    }

    // ---------- bat / tat ----------

    public static void Enable(Store store)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (k is null) return;
                // chi luu backup khi anh dang dat KHONG phai anh minh sinh ra
                var now = CurrentWallpaper();
                if (!string.Equals(now, Output, StringComparison.OrdinalIgnoreCase) && now.Length > 0)
                    k.SetValue("WallpaperBackup", now, RegistryValueKind.String);
                k.SetValue("WallpaperMode", 1, RegistryValueKind.DWord);
            }
            Log.Write($"nen: bat, anh goc = {Backup ?? "(khong co)"}");
            Refresh(store, force: true);
        }
        catch (Exception e) { Log.Write($"nen: bat that bai: {e.Message}"); }
    }

    public static void Disable()
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
                k?.SetValue("WallpaperMode", 0, RegistryValueKind.DWord);

            var old = Backup;
            if (old is { Length: > 0 } && File.Exists(old))
            {
                SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, old, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                Log.Write($"nen: tat, tra lai {old}");
            }
            else Log.Write("nen: tat, khong co anh goc de tra lai");
        }
        catch (Exception e) { Log.Write($"nen: tat that bai: {e.Message}"); }
    }

    // ---------- sinh anh ----------

    /// <summary>Chu ky cua lan ve gan nhat — doi thi moi ve lai.</summary>
    static string _last = "";

    /// <summary>
    /// Ve lai neu co gi doi. Goi moi luot quet 60s, nen doi do phan giai (cam man hinh,
    /// doi may 4K sang 1440p), sang ngay moi, hay doi sang/toi deu tu bat duoc — khong
    /// can hook SystemEvents, vi hook do ban tren thread khac va keo theo phai dong bo
    /// truy cap SQLite.
    /// </summary>
    public static void Refresh(Store store, bool force = false)
    {
        if (!Enabled) return;
        try
        {
            var screen = Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
            var today = DateOnly.FromDateTime(DateTime.Today);
            Theme.Refresh();

            string sig = $"{screen.Width}x{screen.Height}|{today:yyyy-MM-dd}|{Theme.Dark}|{Stamp(store, today)}";
            if (!force && sig == _last) return;

            Compose(store, screen, today);
            SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, Output,
                                  SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            _last = sig;
            Log.Write($"nen: ve lai {screen.Width}x{screen.Height} ({(Theme.Dark ? "toi" : "sang")})");
        }
        catch (Exception e) { Log.Write($"nen: ve that bai: {e}"); }
    }

    /// <summary>Dau van tay cua du lieu dang hien, de biet co gi doi ma ve lai.</summary>
    static string Stamp(Store store, DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        var from = first.AddDays(-(int)first.DayOfWeek);
        var items = store.Range(from, from.AddDays(41));
        return string.Join(";", items.Select(o => $"{o.Id}:{o.On:MMdd}:{(o.Done ? 1 : 0)}:{o.Title.Length}"));
    }

    static void Compose(Store store, Size screen, DateOnly today)
    {
        using var canvas = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            DrawBackdrop(g, screen);
            DrawPanel(g, store, screen, today);
        }

        // BMP chu khong PNG: Windows doc BMP lam wallpaper chac chan nhat, va khong
        // phai lo chuyen cache anh theo ten file.
        var tmp = Output + ".tmp";
        canvas.Save(tmp, ImageFormat.Bmp);
        File.Move(tmp, Output, overwrite: true);
    }

    /// <summary>Anh nen goc, phu kin man hinh kieu Fill (giu ti le, cat phan thua).</summary>
    static void DrawBackdrop(Graphics g, Size screen)
    {
        using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(240, 240, 238)))
            g.FillRectangle(b, 0, 0, screen.Width, screen.Height);

        var src = Source;
        if (src is null) return;

        try
        {
            using var img = Image.FromFile(src);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, Cover(img.Size, screen));
        }
        catch (Exception e) { Log.Write($"nen: khong doc duoc anh goc: {e.Message}"); }
    }

    /// <summary>
    /// Hinh chu nhat de ve <paramref name="src"/> phu kin <paramref name="dst"/>: giu
    /// ti le, lay canh nao thieu lam chuan, phan thua tran deu ra hai ben.
    /// </summary>
    public static Rectangle Cover(Size src, Size dst)
    {
        if (src.Width <= 0 || src.Height <= 0) return new Rectangle(Point.Empty, dst);
        float k = Math.Max((float)dst.Width / src.Width, (float)dst.Height / src.Height);
        int w = (int)Math.Ceiling(src.Width * k), h = (int)Math.Ceiling(src.Height * k);
        return new Rectangle((dst.Width - w) / 2, (dst.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Panel nam ben PHAI: icon desktop mac dinh xep tu trai sang, de ben trai thi lich
    /// nam duoi day icon.
    /// </summary>
    public static Rectangle PanelRect(Size screen)
    {
        int w = (int)(screen.Width * PanelW);
        int h = (int)(screen.Height * PanelH);
        int x = screen.Width - w - (int)(screen.Width * MarginRight);
        int y = (screen.Height - h) / 2;
        return new Rectangle(x, y, w, h);
    }

    static void DrawPanel(Graphics g, Store store, Size screen, DateOnly today)
    {
        var rect = PanelRect(screen);

        float scale = Math.Clamp(rect.Height / LogicalH, 0.8f, 4f);

        int radius = (int)(14 * scale);

        // Lich phai thut vao trong, khong duoc ve sat mep: goc bo cua panel se cat mat
        // mot goc, ma o goc duoi-trai do dung la cho badge ngay hom nay -> "30" bi xen
        // mat nua. Thut bang dung ban kinh bo la chac chan khong cham vao cung arc.
        int pad = radius;
        var inner = new Size(rect.Width - pad * 2, rect.Height - pad * 2);

        // Ve lich ra bitmap RIENG roi dan vao, tuyet doi khong dung TranslateTransform:
        // TextRenderer ve qua GDI nen KHONG theo Graphics.Transform. Dich bang transform
        // thi hinh khoi (GDI+) chay theo con chu thi dung yen — vong tron ngay hom nay
        // troi khoi so "30", nen chip troi khoi ten viec.
        //
        // Ca hai bitmap deu 24bpp, KHONG co kenh alpha: GDI ghi de alpha ve 0 o vung
        // glyph, nen ve len 32bppArgb roi ghep bang ColorMatrix la anh nen chay xuyen
        // qua dung cho co chu.
        using var cal = new Bitmap(inner.Width, inner.Height, PixelFormat.Format24bppRgb);
        using (var cg = Graphics.FromImage(cal))
        using (var view = new CalendarView(store) { Month = today })
            view.Render(cg, inner, scale);

        using var panel = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb);
        using (var pg = Graphics.FromImage(panel))
        {
            using (var card = new SolidBrush(Theme.CellBg))
                pg.FillRectangle(card, 0, 0, rect.Width, rect.Height);
            pg.DrawImageUnscaled(cal, pad, pad);
        }

        using var path = Theme.Rounded(rect, radius);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // vien mo lam panel tach khoi anh nen, khong thi chu chim vao anh
        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        using (var soft = Theme.Rounded(Rectangle.Inflate(rect, (int)(6 * scale), (int)(6 * scale)), radius))
            g.FillPath(shadow, soft);

        var clip = g.Clip;
        g.SetClip(path);
        g.DrawImage(panel, rect);
        g.Clip = clip;

        using (var pen = new Pen(Color.FromArgb(60, Theme.Text), Math.Max(1f, scale)))
            g.DrawPath(pen, path);
        g.SmoothingMode = SmoothingMode.Default;
    }
}
