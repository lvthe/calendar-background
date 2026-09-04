using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Deskcal;

/// <summary>
/// Lop lich mo nam tren hinh nen, duoi moi cua so khac. Khac che do wallpaper o
/// Wallpaper.cs: hinh nen that giu nguyen (Spotlight van tu doi anh moi ngay), va
/// bam duoc — tick viec xong, mo form sua ngay tren desktop.
///
/// Ba thu phai co, thieu cai nao la hong:
///  - WS_EX_NOACTIVATE: bam vao khong cuop focus cua app dang lam viec. Chuot van
///    nhan binh thuong, chi la khong kich hoat cua so.
///  - WS_EX_TOOLWINDOW: khong hien trong Alt+Tab.
///  - Chan WM_WINDOWPOSCHANGING de ep hwndInsertAfter = HWND_BOTTOM. Khong lam thi
///    cu bam mot cai la Windows keo no len truoc moi cua so khac.
/// </summary>
public sealed class DesktopPanel : Form
{
    const string Key = @"Software\deskcal";

    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WM_WINDOWPOSCHANGING = 0x0046;
    const uint SWP_NOZORDER = 0x0004;
    static readonly IntPtr HWND_BOTTOM = 1;

    /// <summary>
    /// Do mo. Khac ban wallpaper: o day Windows ghep ca cua so bang layered window nen
    /// chu giu nguyen tuong phan so voi nen panel — khong dinh loi GDI xoa alpha o vung
    /// glyph nhu khi tu ve len bitmap.
    /// </summary>
    const double PanelOpacity = 0.90;

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    readonly Store _store;
    readonly CalendarView _view;
    readonly Label _title = new();
    Size _laidOutFor;

    /// <summary>Ban khi du lieu doi, de tray va cua so lich cap nhat theo.</summary>
    public event Action? Changed;

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("DesktopPanel") is int v && v == 1;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            k?.SetValue("DesktopPanel", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    public DesktopPanel(Store store)
    {
        _store = store;
        Theme.Refresh();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.CellBg;
        Opacity = PanelOpacity;
        TopMost = false;

        Font = Theme.Font(10f);

        _view = new CalendarView(_store) { Dock = DockStyle.Fill };
        _view.DayActivated += AddOn;
        _view.EventActivated += Edit;
        _view.StatusToggled += Toggle;

        _title.Dock = DockStyle.Top;
        _title.Height = Font.Height * 2;
        _title.Font = Theme.Font(13f, FontStyle.Bold);
        _title.ForeColor = Theme.Text;
        _title.BackColor = Theme.CellBg;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Padding = new Padding(Font.Height, 0, 0, 0);

        // Fill them TRUOC, control Top them sau moi xep len tren no — thu tu dock cua
        // WinForms nguoc voi thu tu trong Controls.
        Controls.Add(_view);
        Controls.Add(_title);

        Layout_();
        Sync();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>Show() khong duoc keo focus khoi app nguoi dung dang go do.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_WINDOWPOSCHANGING && m.LParam != IntPtr.Zero)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(m.LParam);
            wp.InsertAfter = HWND_BOTTOM;
            wp.Flags &= ~SWP_NOZORDER;      // phai bo co nay, khong thi Windows lo InsertAfter
            Marshal.StructureToPtr(wp, m.LParam, false);
        }
        base.WndProc(ref m);
    }

    // ---------- bo cuc ----------

    /// <summary>
    /// Dung chung PanelRect voi che do wallpaper nen hai che do nam dung mot cho, va
    /// hinh hoc do da co test.
    /// </summary>
    void Layout_()
    {
        var screen = Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
        var r = Wallpaper.PanelRect(screen);
        Bounds = r;
        _laidOutFor = screen;

        // goc bo: cat bang Region, khong thi panel la mot khoi chu nhat cung o giua nen
        int radius = (int)(14 * (DeviceDpi / 96f));
        using var path = Theme.Rounded(new Rectangle(0, 0, r.Width, r.Height), radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    /// <summary>
    /// Goi moi luot quet 60s. Doi do phan giai (cam man ngoai, doi may) thi xep lai;
    /// doi ngay hoac doi du lieu thi ve lai. Khong hook SystemEvents vi event do ban
    /// tren thread khac, keo theo phai dong bo truy cap SQLite.
    /// </summary>
    public void Tick()
    {
        var screen = Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
        if (screen != _laidOutFor) Layout_();

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_view.Month.Year != today.Year || _view.Month.Month != today.Month) { _view.Month = today; Sync(); }
        else Reload();
    }

    public void Reload()
    {
        Theme.Refresh();
        BackColor = _title.BackColor = Theme.CellBg;
        _title.ForeColor = Theme.Text;
        _view.Reload();
        Sync();
    }

    void Sync() => _title.Text = $"Tháng {_view.Month.Month}, {_view.Month.Year}";

    // ---------- bam ----------

    void Toggle(Occurrence o)
    {
        _store.SetDone(o.Id, o.On, !o.Done);
        Log.Write((o.Done ? "nen: mo lai #" : "nen: xong #") + o.Id + " " + o.On.ToString("yyyy-MM-dd"));
        Done();
    }

    void AddOn(DateOnly date)
    {
        using var f = new EventForm(null, date, false);
        if (f.ShowDialog() != DialogResult.OK || f.Result is null) return;
        long id = _store.Add(f.Result);
        Log.Write($"nen: them #{id} \"{f.Result.Title}\" {f.Result.Date:yyyy-MM-dd}");
        Done();
    }

    void Edit(Occurrence o)
    {
        var row = _store.Get(o.Id);
        if (row is null) { Reload(); return; }

        using var f = new EventForm(row, o.On, o.Done);
        if (f.ShowDialog() != DialogResult.OK) return;

        if (f.DeleteRequested)
        {
            _store.Delete(o.Id);
            Log.Write($"nen: xoa #{o.Id} \"{row.Title}\"");
            Done();
            return;
        }

        if (f.Result is null) return;
        _store.UpdateAndSetDone(f.Result, o.On, f.DoneChecked);
        Log.Write($"nen: sua #{o.Id} \"{f.Result.Title}\" {f.Result.Date:yyyy-MM-dd}");
        Done();
    }

    void Done()
    {
        _view.Reload();
        Changed?.Invoke();
    }
}
