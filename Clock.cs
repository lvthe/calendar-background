using System.Drawing.Drawing2D;

namespace Deskcal;

/// <summary>
/// Mat dong ho de chon gio. Hai buoc: bam vong so de chon gio (vanh ngoai 1-12,
/// vanh trong 13-23 va 00), roi bam tiep de chon phut (buoc 5 phut). Muon phut le
/// thi go tay vao o hoac dung ▲▼.
/// </summary>
public sealed class ClockPopup : Painted
{
    readonly int _u, _pad, _headerH, _dia;
    int _hour, _minute;
    bool _pickMinute;
    int _hotIdx = -1;
    bool _hotOuter;

    public event Action<TimeOnly>? Picked;

    public ClockPopup(Font font, TimeOnly initial)
    {
        Font = font;
        _hour = initial.Hour;
        _minute = initial.Minute;

        _u = font.Height;
        _pad = _u;
        _headerH = _u * 5 / 2;
        _dia = _u * 15;
        Size = new Size(_dia + _pad * 2, _headerH + _dia + _pad);
        BackColor = Theme.MenuBg;
    }

    float OuterR => _dia / 2f - _u * 0.95f;
    // hai vanh phai cach nhau hon 2 lan ban kinh so, khong thi so chong len nhau
    float InnerR => OuterR - _u * 2.4f;
    float NumR => _u * 0.78f;
    PointF Center => new(_pad + _dia / 2f, _headerH + _dia / 2f);

    static PointF At(PointF c, float radius, double deg)
    {
        double rad = (deg - 90) * Math.PI / 180;
        return new PointF(c.X + (float)(radius * Math.Cos(rad)), c.Y + (float)(radius * Math.Sin(rad)));
    }

    /// <summary>Goc va vanh ung voi gio dang chon.</summary>
    (double Deg, float R) HourSpot(int h) => h switch
    {
        0 => (0, InnerR),
        12 => (0, OuterR),
        >= 1 and <= 11 => (h * 30, OuterR),
        _ => ((h - 12) * 30, InnerR),
    };

    // ---------- ve ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var b = new SolidBrush(Theme.MenuBg)) g.FillRectangle(b, ClientRectangle);

        DrawHeader(g);

        var c = Center;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var face = new SolidBrush(Theme.Hover))
            g.FillEllipse(face, _pad, _headerH, _dia, _dia);
        g.SmoothingMode = SmoothingMode.Default;

        if (_pickMinute) DrawMinutes(g, c);
        else DrawHours(g, c);
    }

    void DrawHeader(Graphics g)
    {
        var big = Theme.Font(Font.SizeInPoints + 8f, FontStyle.Bold);
        string hh = _hour.ToString("00"), mm = _minute.ToString("00");

        var hSize = TextRenderer.MeasureText(hh, big, new Size(int.MaxValue, int.MaxValue), Theme.Flat);
        var sSize = TextRenderer.MeasureText(":", big, new Size(int.MaxValue, int.MaxValue), Theme.Flat);
        var mSize = TextRenderer.MeasureText(mm, big, new Size(int.MaxValue, int.MaxValue), Theme.Flat);

        int total = hSize.Width + sSize.Width + mSize.Width;
        int x = (Width - total) / 2;
        int y = (_headerH - hSize.Height) / 2;

        _hourHit = new Rectangle(x, y, hSize.Width, hSize.Height);
        TextRenderer.DrawText(g, hh, big, _hourHit, _pickMinute ? Theme.Muted : Theme.Accent, Theme.Flat);
        x += hSize.Width;

        TextRenderer.DrawText(g, ":", big, new Rectangle(x, y, sSize.Width, sSize.Height), Theme.Muted, Theme.Flat);
        x += sSize.Width;

        _minHit = new Rectangle(x, y, mSize.Width, mSize.Height);
        TextRenderer.DrawText(g, mm, big, _minHit, _pickMinute ? Theme.Accent : Theme.Muted, Theme.Flat);
    }

    Rectangle _hourHit, _minHit;

    void DrawHours(Graphics g, PointF c)
    {
        var (selDeg, selR) = HourSpot(_hour);
        Hand(g, c, selR, selDeg);

        for (int i = 0; i < 12; i++)
        {
            int outer = i == 0 ? 12 : i;
            int inner = i == 0 ? 0 : i + 12;
            Number(g, At(c, OuterR, i * 30), outer.ToString(), Font,
                   outer == _hour, _hotIdx == i && _hotOuter);
            Number(g, At(c, InnerR, i * 30), inner.ToString("00"), Theme.Font(Font.SizeInPoints - 2f),
                   inner == _hour, _hotIdx == i && !_hotOuter);
        }
    }

    void DrawMinutes(Graphics g, PointF c)
    {
        Hand(g, c, OuterR, _minute * 6);

        for (int i = 0; i < 12; i++)
        {
            int m = i * 5;
            Number(g, At(c, OuterR, i * 30), m.ToString("00"), Font, m == _minute, _hotIdx == i);
        }
    }

    void Hand(Graphics g, PointF c, float r, double deg)
    {
        var tip = At(c, r, deg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Theme.Accent, 1.6f))
            g.DrawLine(pen, c, tip);
        using (var b = new SolidBrush(Theme.Accent))
            g.FillEllipse(b, c.X - 3, c.Y - 3, 6, 6);
        g.SmoothingMode = SmoothingMode.Default;
    }

    void Number(Graphics g, PointF at, string text, Font font, bool selected, bool hot)
    {
        var box = new Rectangle((int)(at.X - NumR), (int)(at.Y - NumR), (int)(NumR * 2), (int)(NumR * 2));
        if (selected)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Theme.Accent);
            g.FillEllipse(b, box);
            g.SmoothingMode = SmoothingMode.Default;
        }
        else if (hot)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Theme.Press);
            g.FillEllipse(b, box);
            g.SmoothingMode = SmoothingMode.Default;
        }

        // So tren mat dong ho rat nho. TextRenderer (GDI) ve bang ClearType subpixel,
        // o kich thuoc nay vien mau lan het net nen tung so hien ra mot mau khac nhau.
        // GDI+ voi AntiAliasGridFit la antialias xam, khong co vien mau.
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var brush = new SolidBrush(selected ? Theme.OnAccent : Theme.Text))
        using (var fmt = new StringFormat
               { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString(text, font, brush, new RectangleF(box.X, box.Y, box.Width, box.Height), fmt);
        }
        g.TextRenderingHint = hint;
    }

    // ---------- chuot ----------

    /// <summary>Tra ve chi so 0..11 quanh mat dong ho, va co phai vanh ngoai khong.</summary>
    (int Idx, bool Outer)? Locate(Point p)
    {
        var c = Center;
        float dx = p.X - c.X, dy = p.Y - c.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > OuterR + NumR) return null;

        double deg = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
        int idx = (int)Math.Round(deg / 30) % 12;
        bool outer = _pickMinute || dist >= (OuterR + InnerR) / 2;
        return (idx, outer);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = Locate(e.Location);
        int idx = hit?.Idx ?? -1;
        bool outer = hit?.Outer ?? false;
        if (idx == _hotIdx && outer == _hotOuter) return;
        _hotIdx = idx;
        _hotOuter = outer;
        Cursor = idx < 0 ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hotIdx = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // bam vao so gio tren header thi quay lai buoc chon gio
        if (_hourHit.Contains(e.Location)) { _pickMinute = false; Invalidate(); return; }
        if (_minHit.Contains(e.Location)) { _pickMinute = true; Invalidate(); return; }

        if (Locate(e.Location) is not { } hit) return;

        if (_pickMinute)
        {
            _minute = hit.Idx * 5;
            Invalidate();
            Picked?.Invoke(new TimeOnly(_hour, _minute));
            return;
        }

        _hour = hit.Outer
            ? (hit.Idx == 0 ? 12 : hit.Idx)
            : (hit.Idx == 0 ? 0 : hit.Idx + 12);
        _pickMinute = true;
        Invalidate();
    }
}

public static class Clock
{
    /// <summary>Mo mat dong ho ngay duoi o gio, chon xong thi ghi lai vao o.</summary>
    public static void Show(StepBox owner, Font font)
    {
        Parse.TryTime(owner.Text, out var at);
        var pop = new ClockPopup(font, at ?? new TimeOnly(9, 0));

        var host = new ToolStripControlHost(pop)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = pop.Size,
        };
        var dd = new ToolStripDropDown
        {
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            AutoSize = true,
            DropShadowEnabled = true,
            BackColor = Theme.MenuBg,
            Renderer = new MenuRenderer(),
        };
        dd.Items.Add(host);

        pop.Picked += t =>
        {
            owner.Text = t.ToString("HH\\:mm");
            dd.Close();
            owner.Commit();   // de o gio ket thuc tu dien +1 tieng
        };

        dd.Show(owner, new Point(0, owner.Height + 2));
    }
}
