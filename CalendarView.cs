using System.Drawing.Drawing2D;

namespace Deskcal;

/// <summary>
/// Luoi lich thang ve tay. Khong xep control vao tung o: 42 o x nhieu su kien la
/// hang tram control, vua cham vua khong the tao dang nhu mong muon. Ve tay thi
/// paint va hit-test dung chung mot ham tinh toa do nen khong bao gio lech nhau.
///
/// Moi so do deu nhan theo DPI. Man hinh 150% khien font 9pt cao ~19px, neu de
/// LineH cung 22px thi dau nang cua tieng Viet bi cat mat.
/// </summary>
public sealed class CalendarView : Control
{
    const int Rows = 6;

    readonly Store _store;
    readonly Dictionary<DateOnly, List<Occurrence>> _byDay = [];

    DateOnly _month;
    DateOnly? _hoverDay;
    Occurrence? _hoverEvent;
    bool _hoverStatus;

    public event Action<DateOnly>? DayActivated;
    public event Action<Occurrence>? EventActivated;

    /// <summary>Bam vao o trang thai ben trai chip — doi xong / chua xong.</summary>
    public event Action<Occurrence>? StatusToggled;

    // Khi ve ra wallpaper thi khong co control that: kich thuoc va ti le lay tu day
    // thay vi tu Width/Height/DeviceDpi. Hit-test dung chung ColX/RowY nen phai di qua
    // W/H/S luon, khong duoc doc thang Width/Height — lech mot cai la bam sai o ngay.
    Size? _renderSize;
    float? _renderScale;

    int W => _renderSize?.Width ?? Width;
    int H => _renderSize?.Height ?? Height;

    // ---- so do theo DPI (goc tinh o 96 dpi) ----
    float S => _renderScale ?? DeviceDpi / 96f;
    int Px(double logical) => (int)Math.Round(logical * S);
    int HeaderH => Px(34);
    int DayNumH => Px(32);
    int LineH => Px(23);
    int PadX => Px(7);
    int Badge => Px(26);
    int DotSize => Px(7);
    int TextIndent => Px(15);
    int StatusSize => Px(13);

    /// <summary>Ngay dau thang dang xem.</summary>
    public DateOnly Month
    {
        get => _month;
        set { _month = new DateOnly(value.Year, value.Month, 1); Reload(); }
    }

    public CalendarView(Store store)
    {
        _store = store;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    public void Reload()
    {
        _byDay.Clear();
        var from = FirstCell();
        foreach (var o in _store.Range(from, from.AddDays(Rows * 7 - 1)))
        {
            if (!_byDay.TryGetValue(o.On, out var list)) _byDay[o.On] = list = [];
            list.Add(o);
        }
        Invalidate();
    }

    DateOnly FirstCell()
    {
        var first = new DateOnly(_month.Year, _month.Month, 1);
        return first.AddDays(-(int)first.DayOfWeek);   // Sunday = 0, khop voi header
    }

    // ---------- toa do ----------

    int ColX(int i) => W * i / 7;
    int RowY(int r) => HeaderH + (H - HeaderH) * r / Rows;

    IEnumerable<(Rectangle Box, DateOnly Date, int Col)> Cells()
    {
        var start = FirstCell();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < 7; c++)
                yield return (Rectangle.FromLTRB(ColX(c), RowY(r), ColX(c + 1), RowY(r + 1)),
                              start.AddDays(r * 7 + c), c);
    }

    /// <summary>Cac dong su kien trong mot o. More &gt; 0 la dong "+N khac".</summary>
    List<(Rectangle Box, Occurrence? Ev, int More)> Lines(Rectangle cell, List<Occurrence>? evs)
    {
        var outp = new List<(Rectangle, Occurrence?, int)>();
        if (evs is null || evs.Count == 0) return outp;

        int top = cell.Top + DayNumH;
        int room = Math.Max(0, cell.Bottom - Px(3) - top);
        int fit = room / LineH;
        if (fit <= 0) return outp;

        // con thua thi danh 1 dong cho "+N khac"
        int show = evs.Count <= fit ? evs.Count : Math.Max(0, fit - 1);
        int w = cell.Width - PadX * 2;
        for (int i = 0; i < show; i++)
            outp.Add((new Rectangle(cell.Left + PadX, top + i * LineH, w, LineH), evs[i], 0));

        if (show < evs.Count)
            outp.Add((new Rectangle(cell.Left + PadX, top + show * LineH, w, LineH), null, evs.Count - show));
        return outp;
    }

    // ---------- ve ----------

    protected override void OnPaint(PaintEventArgs e) => PaintCore(e.Graphics);

    /// <summary>
    /// Ve lich ra mot Graphics bat ky o kich thuoc bat ky — dung cho wallpaper.
    /// Tra control ve trang thai cu o finally, khong thi hit-test cua cua so that
    /// se dung nham so do cua wallpaper.
    /// </summary>
    public void Render(Graphics g, Size size, float scale)
    {
        _renderSize = size;
        _renderScale = scale;
        try { PaintCore(g); }
        finally { _renderSize = null; _renderScale = null; }
    }

    void PaintCore(Graphics g)
    {
        Theme.Refresh();
        // KHONG dung g.Clear: no xoa toan bo surface, ma khi ve ra wallpaper thi
        // surface do con dang giu anh nen phia duoi.
        using (var bg = new SolidBrush(Theme.CellBg)) g.FillRectangle(bg, 0, 0, W, H);

        DrawWeekHeader(g);

        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var (box, date, col) in Cells())
        {
            bool outside = date.Month != _month.Month || date.Year != _month.Year;
            DrawCellBg(g, box, date, col, outside);
            DrawDayNumber(g, box, date, col, outside, today);

            _byDay.TryGetValue(date, out var evs);
            foreach (var (lbox, ev, more) in Lines(box, evs))
            {
                if (ev is not null) DrawEvent(g, lbox, ev);
                else DrawMore(g, lbox, more);
            }
        }

        DrawGrid(g);
    }

    /// <summary>
    /// Font tinh bang point. O cua so that, WinForms da tu phong theo DPI he thong
    /// nen khong duoc nhan them. Nhung khi ve ra bitmap thi khong ai phong ho, phai
    /// tu nhan theo scale — khong thi o thi to ma chu van be xiu.
    /// </summary>
    Font Fnt(float size, FontStyle style = FontStyle.Regular) =>
        Theme.Font(_renderScale is { } r ? size * r : size, style);

    // NoClipping o moi lan ve chu: dau nang tieng Viet (ậ ẹ ọ) nam thap hon duong
    // descender cua font, GDI cat theo rect nen thieu no la mat dau.
    const TextFormatFlags Flat =
        TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping | TextFormatFlags.SingleLine;

    void DrawWeekHeader(Graphics g)
    {
        string[] names = ["CN", "Hai", "Ba", "Tư", "Năm", "Sáu", "Bảy"];
        var font = Fnt(9.5f, FontStyle.Bold);
        for (int c = 0; c < 7; c++)
        {
            var box = Rectangle.FromLTRB(ColX(c), 0, ColX(c + 1), HeaderH);
            var color = c == 0 ? Theme.SunText : c == 6 ? Theme.SatText : Theme.Muted;
            TextRenderer.DrawText(g, names[c], font, box, color,
                Flat | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        using var pen = new Pen(Theme.Grid);
        g.DrawLine(pen, 0, HeaderH - 1, W, HeaderH - 1);
    }

    void DrawCellBg(Graphics g, Rectangle box, DateOnly date, int col, bool outside)
    {
        Color bg = outside ? Theme.OutsideBg
                 : col == 0 ? Theme.SunTint
                 : col == 6 ? Theme.SatTint
                 : Theme.CellBg;
        using var b = new SolidBrush(bg);
        g.FillRectangle(b, box);

        if (_hoverDay == date && _hoverEvent is null)
        {
            using var hb = new SolidBrush(Theme.Hover);
            g.FillRectangle(hb, box);
        }
    }

    void DrawDayNumber(Graphics g, Rectangle box, DateOnly date, int col, bool outside, DateOnly today)
    {
        string label = date.Day.ToString();
        var font = Fnt(10f, date == today ? FontStyle.Bold : FontStyle.Regular);

        if (date == today)
        {
            // vong tron accent quanh ngay hom nay; phai vuong va du rong cho 2 chu so
            var d = new Rectangle(box.Left + PadX - Px(3), box.Top + Px(4), Badge, Badge);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Theme.Accent);
            g.FillEllipse(b, d);
            g.SmoothingMode = SmoothingMode.Default;
            TextRenderer.DrawText(g, label, font, d, Theme.OnAccent,
                Flat | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        Color fg = outside ? Theme.Muted
                 : col == 0 ? Theme.SunText
                 : col == 6 ? Theme.SatText
                 : Theme.Text;
        var slot = new Rectangle(box.Left + PadX, box.Top + Px(4), Px(34), Badge);
        TextRenderer.DrawText(g, label, font, slot, fg,
            Flat | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    /// <summary>
    /// Ve su kien dang "chip": nen pastel theo nhan, chu dam mau cung ho — kieu the
    /// nhan cua Notion. De phan loai bang mat nhanh hon la mot dot nho.
    /// </summary>
    Rectangle ChipOf(Rectangle line) => new(line.Left, line.Top, line.Width, line.Height - Px(3));

    /// <summary>O trang thai ben trai chip: tron rong = chua xong, tron day + tick = xong.</summary>
    Rectangle StatusBox(Rectangle chip)
    {
        int s = StatusSize;
        return new Rectangle(chip.Left + Px(4), chip.Top + (chip.Height - s) / 2, s, s);
    }

    void DrawEvent(Graphics g, Rectangle line, Occurrence o)
    {
        bool hot = ReferenceEquals(_hoverEvent, o);
        var chip = ChipOf(line);

        Color fill = o.Done ? Theme.Hover : Theme.TagFill(o.Tag);
        if (hot) fill = Shade(fill, Theme.Dark ? +0.16f : -0.045f);
        Theme.FillRound(g, chip, Px(4), fill);

        DrawStatus(g, StatusBox(chip), o, hot);

        var font = Fnt(9f, o.Done ? FontStyle.Strikeout : FontStyle.Regular);
        var ink = o.Done ? Theme.Muted : Theme.TagColor(o.Tag);
        string text = o.At is null ? o.Title : $"{o.TimeLabel}  {o.Title}";
        int left = StatusBox(chip).Right + Px(5);
        var textBox = new Rectangle(left, chip.Top, Math.Max(Px(10), chip.Right - left - Px(5)), chip.Height);
        TextRenderer.DrawText(g, text, font, textBox, ink,
            Flat | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void DrawStatus(Graphics g, Rectangle mark, Occurrence o, bool hot)
    {
        var tone = o.Done ? Theme.Muted : Theme.TagColor(o.Tag);
        bool ringHot = hot && _hoverStatus;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (o.Done)
        {
            using var b = new SolidBrush(tone);
            g.FillEllipse(b, mark);
            Theme.Tick(g, mark, Theme.OnAccent, Math.Max(1.3f, StatusSize * 0.14f));
        }
        else
        {
            if (ringHot)
            {
                using var b = new SolidBrush(Color.FromArgb(55, tone));
                g.FillEllipse(b, mark);
            }
            using var pen = new Pen(tone, ringHot ? 1.8f : 1.3f);
            g.DrawEllipse(pen, mark.X, mark.Y, mark.Width - 1, mark.Height - 1);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    static Color Shade(Color c, float amount)
    {
        float f = 1f + amount;
        return Color.FromArgb(c.A,
            Math.Clamp((int)(c.R * f), 0, 255),
            Math.Clamp((int)(c.G * f), 0, 255),
            Math.Clamp((int)(c.B * f), 0, 255));
    }

    void DrawMore(Graphics g, Rectangle box, int more)
    {
        var slot = new Rectangle(box.Left + Px(6), box.Top, box.Width - Px(6), box.Height);
        TextRenderer.DrawText(g, $"+{more} việc nữa", Fnt(9f), slot, Theme.Muted,
            Flat | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    void DrawGrid(Graphics g)
    {
        using var pen = new Pen(Theme.Grid);
        for (int c = 1; c < 7; c++) g.DrawLine(pen, ColX(c), HeaderH, ColX(c), H);
        for (int r = 1; r < Rows; r++) g.DrawLine(pen, 0, RowY(r), W, RowY(r));
    }

    // ---------- chuot ----------

    (DateOnly? Day, Occurrence? Ev, int More, bool OnStatus) HitTest(Point p)
    {
        if (p.Y < HeaderH) return (null, null, 0, false);
        foreach (var (box, date, _) in Cells())
        {
            if (!box.Contains(p)) continue;
            _byDay.TryGetValue(date, out var evs);
            foreach (var (lbox, ev, more) in Lines(box, evs))
            {
                // vung bam rong ca o cho de tro, khong chi be ngang cua chu
                if (!new Rectangle(box.Left, lbox.Top, box.Width, lbox.Height).Contains(p)) continue;

                // noi rong vung bam cua o trang thai cho de bam bang chuot
                bool onStatus = ev is not null
                    && Rectangle.Inflate(StatusBox(ChipOf(lbox)), Px(4), Px(4)).Contains(p);
                return (date, ev, more, onStatus);
            }
            return (date, null, 0, false);
        }
        return (null, null, 0, false);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (day, ev, _, onStatus) = HitTest(e.Location);
        if (day != _hoverDay || !ReferenceEquals(ev, _hoverEvent) || onStatus != _hoverStatus)
        {
            _hoverDay = day;
            _hoverEvent = ev;
            _hoverStatus = onStatus;
            Cursor = day is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverDay = null;
        _hoverEvent = null;
        _hoverStatus = false;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        var (day, ev, more, onStatus) = HitTest(e.Location);
        if (day is null) return;

        if (ev is not null)
        {
            if (onStatus) StatusToggled?.Invoke(ev);
            else EventActivated?.Invoke(ev);
            return;
        }
        if (more > 0) { ShowDayList(day.Value); return; }
        DayActivated?.Invoke(day.Value);
    }

    /// <summary>Bam "+N viec nua" thi liet ke ca ngay ra menu, dung lai MenuRenderer.</summary>
    void ShowDayList(DateOnly day)
    {
        if (!_byDay.TryGetValue(day, out var evs) || evs.Count == 0) return;

        var menu = new ContextMenuStrip { Renderer = new MenuRenderer(), Font = Theme.Font(9.5f) };
        menu.Items.Add(new ToolStripMenuItem($"{day:dd/MM/yyyy}")
        {
            Enabled = false,
            Font = Theme.Font(9f, FontStyle.Bold),
        });
        menu.Items.Add(new ToolStripSeparator());

        foreach (var o in evs)
        {
            var captured = o;
            var item = new ToolStripMenuItem($"{(o.At is null ? "cả ngày" : o.TimeLabel)}   {o.Title}")
            {
                Checked = o.Done,
                Font = Theme.Font(9.5f, o.Done ? FontStyle.Strikeout : FontStyle.Regular),
                Tag = o.Done ? "done" : null,
            };
            item.Click += (_, _) => EventActivated?.Invoke(captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var add = new ToolStripMenuItem("Thêm việc ngày này…");
        add.Click += (_, _) => DayActivated?.Invoke(day);
        menu.Items.Add(add);

        menu.Show(this, PointToClient(Cursor.Position));
    }
}
