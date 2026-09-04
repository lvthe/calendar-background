using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Deskcal;

public static class Paths
{
    public static string Dir =>
        Environment.GetEnvironmentVariable("DESKCAL_DATA") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "deskcal");

    public static string Db => Path.Combine(Dir, "deskcal.db");
    public static string LogFile => Path.Combine(Dir, "deskcal.log");

    /// <summary>
    /// Ban Node cu de DB trong .\data canh exe. Neu con thi chuyen sang LocalAppData
    /// mot lan, vi Downloads la cho hay bi don.
    /// </summary>
    public static void MigrateLegacy()
    {
        try
        {
            if (File.Exists(Db)) return;
            // exe co the nam trong dist\ hoac bin\..., nen tim ca thu muc lam viec
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "data", "deskcal.db"),
                Path.Combine(Directory.GetCurrentDirectory(), "data", "deskcal.db"),
            ];
            var old = candidates.FirstOrDefault(File.Exists);
            if (old is null) return;
            Directory.CreateDirectory(Dir);
            File.Copy(old, Db);
            Log.Write($"chuyen DB cu tu {old} sang {Db}");
        }
        catch (Exception e) { Log.Write($"khong chuyen duoc DB cu: {e.Message}"); }
    }
}

/// <summary>App khong co console nen moi thu phai vao file log.</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Write(string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.Dir);
                var f = new FileInfo(Paths.LogFile);
                if (f.Exists && f.Length > 1_000_000) f.Delete();   // khoi phinh vo han
                File.AppendAllText(Paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
        }
        catch { /* log that bai thi thoi, khong the lam gi hon */ }
    }
}

/// <summary>
/// Ban truoc tung co che do ve lich thang vao anh nen roi dat lam wallpaper. Da bo.
/// Ai lo dang bat luc nang cap thi wallpaper cua ho van dang tro vao file app sinh ra,
/// va khong con nut nao de tra lai — nen dong nay tu tra ho mot lan roi don sach.
/// Xoa duoc sau vai ban.
/// </summary>
public static class LegacyWallpaper
{
    const string Key = @"Software\deskcal";
    const int SPI_SETDESKWALLPAPER = 0x0014;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfoW(int action, int param, string? value, int winIni);

    public static void Undo()
    {
        try
        {
            var generated = Path.Combine(Paths.Dir, "wallpaper.bmp");
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true);
            if (k is null) return;

            using (var d = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            {
                var now = d?.GetValue("Wallpaper") as string ?? "";
                // chi dung tay vao wallpaper khi no dung la file minh sinh ra
                if (string.Equals(now, generated, StringComparison.OrdinalIgnoreCase)
                    && k.GetValue("WallpaperBackup") is string old && File.Exists(old))
                {
                    SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, old, 0x03);
                    Log.Write($"nen: bo che do wallpaper, tra lai {old}");
                }
            }

            k.DeleteValue("WallpaperMode", false);
            k.DeleteValue("WallpaperBackup", false);
            if (File.Exists(generated)) File.Delete(generated);
        }
        catch (Exception e) { Log.Write($"nen: don che do wallpaper cu that bai: {e.Message}"); }
    }
}

public static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "deskcal";

    static string ExePath => Environment.ProcessPath ?? AppContext.BaseDirectory + "deskcal.exe";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is not null;
        }
    }

    public static void Toggle()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (k is null) return;
            if (Enabled) { k.DeleteValue(Name, false); Log.Write("tat chay cung Windows"); }
            else { k.SetValue(Name, $"\"{ExePath}\""); Log.Write($"bat chay cung Windows: {ExePath}"); }
        }
        catch (Exception e) { Log.Write($"khong doi duoc autostart: {e.Message}"); }
    }
}

public static class TrayIcon
{
    /// <summary>
    /// Ve icon luc chay, khoi phai kem file .ico. Ve o kich thuoc gap 8 lan roi thu
    /// nho bang bicubic (supersampling) — ve truc tiep o 16px thi net cheo bi nhoe.
    /// Lay dung SmallIconSize nen 125%/150% scaling khong bi mo.
    /// </summary>
    public static Icon Make()
    {
        var want = SystemInformation.SmallIconSize;
        int s = Math.Clamp(Math.Min(want.Width, want.Height), 16, 64);
        int big = s * 8;

        using var hi = new Bitmap(big, big);
        using (var g = Graphics.FromImage(hi))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // o bo tron an nhieu pixel hon hinh tron o 16px, nen de thay hon tren taskbar
            var box = new Rectangle(0, 0, big - 1, big - 1);
            using var bg = new SolidBrush(Color.FromArgb(37, 99, 235));
            using var path = Theme.Rounded(box, (int)(big * 0.28));
            g.FillPath(bg, path);

            using var pen = new Pen(Color.White, big * 0.11f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };
            g.DrawLines(pen,
            [
                new PointF(big * 0.26f, big * 0.52f),
                new PointF(big * 0.44f, big * 0.70f),
                new PointF(big * 0.75f, big * 0.30f),
            ]);
        }

        using var small = new Bitmap(s, s);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(hi, new Rectangle(0, 0, s, s));
        }
        return Icon.FromHandle(small.GetHicon());
    }
}
