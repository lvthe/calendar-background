namespace Deskcal;

/// <summary>
/// Lop nen cho cac control tu ve. WinForms khong ho tro nen trong suot that, nen moi
/// control tu to lai mau nen cua cha truoc khi ve hinh bo goc — nho vay bon goc khong
/// bi vien den.
/// </summary>
public abstract class Painted : Control
{
    protected Painted()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected Color ParentBg()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.FormBg;
    }

    protected void Backdrop(Graphics g)
    {
        using var b = new SolidBrush(ParentBg());
        g.FillRectangle(b, ClientRectangle);
    }

    protected static Color Shade(Color c, float amount)
    {
        float f = 1f + amount;
        return Color.FromArgb(c.A,
            Math.Clamp((int)(c.R * f), 0, 255),
            Math.Clamp((int)(c.G * f), 0, 255),
            Math.Clamp((int)(c.B * f), 0, 255));
    }
}

public enum BtnKind { Primary, Default, Subtle, Danger }

/// <summary>Icon ve bang vector, khong dung glyph font.</summary>
public enum IconKind { None, ChevronLeft, ChevronRight, Clock }

/// <summary>Nut bo goc, tu ve. IButtonControl de Form dat lam AcceptButton/CancelButton.</summary>
public sealed class Btn : Painted, IButtonControl
{
    public BtnKind Kind { get; set; }
    public int Radius { get; set; } = 6;
    public DialogResult DialogResult { get; set; }

    /// <summary>Khac None thi ve icon vector giua nut thay cho chu.</summary>
    public IconKind Icon { get; set; }

    bool _hover, _press;

    public Btn(string text, BtnKind kind, Font font, int padX, int padY)
    {
        Kind = kind;
        Font = font;
        Text = text;
        Padding = new Padding(padX, padY, padX, padY);
        Cursor = Cursors.Hand;
        AutoSize = true;
        TabStop = false;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        if (Icon != IconKind.None)
        {
            int side = Font.Height + Padding.Vertical;
            return new Size(side, side);
        }
        var s = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), Theme.Flat);
        return new Size(s.Width + Padding.Horizontal, s.Height + Padding.Vertical);
    }

    public void NotifyDefault(bool value) { }
    public void PerformClick() => OnClick(EventArgs.Empty);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } f) f.DialogResult = DialogResult;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _press = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _press = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _press = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Backdrop(e.Graphics);
        var r = new Rectangle(0, 0, Width, Height);

        Color fill, line, ink;
        switch (Kind)
        {
            case BtnKind.Primary:
                fill = _press ? Shade(Theme.Accent, -0.14f) : _hover ? Shade(Theme.Accent, -0.07f) : Theme.Accent;
                line = fill;
                ink = Theme.OnAccent;
                break;
            case BtnKind.Subtle:
                fill = _press ? Theme.Press : _hover ? Theme.Hover : Color.Empty;
                line = Color.Empty;
                ink = Theme.Text;
                break;
            case BtnKind.Danger:
                fill = _press ? Theme.Press : _hover ? Theme.Hover : Theme.FieldBg;
                line = Theme.Border;
                ink = Theme.Danger;
                break;
            default:
                fill = _press ? Theme.Press : _hover ? Theme.Hover : Theme.FieldBg;
                line = Theme.Border;
                ink = Theme.Text;
                break;
        }

        if (fill != Color.Empty) Theme.FillRound(e.Graphics, r, Radius, fill);
        if (line != Color.Empty) Theme.StrokeRound(e.Graphics, r, Radius, line);

        switch (Icon)
        {
            case IconKind.ChevronLeft: Theme.ChevronH(e.Graphics, r, ink, -1); return;
            case IconKind.ChevronRight: Theme.ChevronH(e.Graphics, r, ink, +1); return;
            case IconKind.Clock: Theme.ClockGlyph(e.Graphics, r, ink); return;
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, r, ink,
            Theme.Flat | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>O nhap bo goc: khung do control nay ve, ben trong la TextBox khong vien.</summary>
public sealed class Field : Painted
{
    public TextBox Box { get; } = new();
    public int Radius { get; set; } = 6;

    readonly int _padX, _padY;
    bool _focus;

    public Field(Font font, bool multiline = false)
    {
        Box.BorderStyle = BorderStyle.None;
        Box.Font = font;
        Box.Multiline = multiline;
        Box.BackColor = Theme.FieldBg;
        Box.ForeColor = Theme.Text;
        Box.Enter += (_, _) => { _focus = true; Invalidate(); };
        Box.Leave += (_, _) => { _focus = false; Invalidate(); };
        if (multiline) Box.ScrollBars = ScrollBars.Vertical;
        Controls.Add(Box);

        _padX = Math.Max(6, font.Height / 2);
        _padY = Math.Max(4, font.Height / 3);

        // Control tu ve co DefaultSize la 0x0. Khong dat chieu cao o day thi o co ve
        // vach mong 1px va coi nhu bien mat.
        Height = Box.PreferredHeight + _padY * 2;
        Width = font.Height * 12;
    }

    public override string Text
    {
        get => Box.Text;
        set => Box.Text = value;
    }

    public string? Placeholder
    {
        get => Box.PlaceholderText;
        set => Box.PlaceholderText = value ?? "";
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(proposed.Width > 0 ? proposed.Width : Width, Box.PreferredHeight + _padY * 2);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Box.SetBounds(_padX, _padY, Math.Max(10, Width - _padX * 2), Math.Max(10, Height - _padY * 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Backdrop(e.Graphics);
        var r = new Rectangle(0, 0, Width, Height);
        Theme.FillRound(e.Graphics, r, Radius, Theme.FieldBg);
        Theme.StrokeRound(e.Graphics, r, Radius, _focus ? Theme.Accent : Theme.Border, _focus ? 1.4f : 1f);
    }
}

/// <summary>
/// O nhap kem hai mui ▲▼ tu ve. Thay cho ComboBox chi co vai gia tri dinh san: chon
/// duoc bat ky gia tri nao, van go tay duoc, va cuon chuot cung an.
/// </summary>
public sealed class StepBox : Painted
{
    readonly TextBox _box = new();
    readonly int _padY, _arrowW;
    bool _focus;
    int _hot;   // 0 khong, +1 mui tren, -1 mui duoi, 2 icon dong ho
    Action<StepBox>? _picker;

    /// <summary>(text hien tai, +1 hoac -1) =&gt; text moi.</summary>
    public Func<string, int, string>? Stepper { get; set; }

    /// <summary>Ban khi roi khoi o — dung de tu dien gio ket thuc.</summary>
    public event EventHandler? Committed;

    public int Radius { get; set; } = 6;

    /// <summary>Dat vao thi hien icon dong ho; bam icon se goi ham nay.</summary>
    public Action<StepBox>? Picker
    {
        get => _picker;
        set { _picker = value; Relayout(); Invalidate(); }
    }

    /// <summary>Ban Committed bang tay — dung khi gia tri duoc dat tu ngoai (vd dong ho).</summary>
    public void Commit() => Committed?.Invoke(this, EventArgs.Empty);

    public StepBox(Font font, int width)
    {
        _box.BorderStyle = BorderStyle.None;
        _box.Font = font;
        _box.BackColor = Theme.FieldBg;
        _box.ForeColor = Theme.Text;
        _box.Enter += (_, _) => { _focus = true; Invalidate(); };
        _box.Leave += (_, _) => { _focus = false; Invalidate(); Committed?.Invoke(this, EventArgs.Empty); };
        _box.KeyDown += OnKey;
        _box.MouseWheel += (_, e) => Bump(Math.Sign(e.Delta));
        Controls.Add(_box);

        _padY = Math.Max(4, font.Height / 3);
        _arrowW = Math.Max(16, font.Height);
        Width = width;
        Height = _box.PreferredHeight + _padY * 2;
    }

    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    public string? Placeholder
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value ?? "";
    }

    public override Size GetPreferredSize(Size proposed) => new(Width, _box.PreferredHeight + _padY * 2);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Relayout();
    }

    void Relayout()
    {
        int padX = Math.Max(6, _box.Font.Height / 2);
        int right = _arrowW + 6 + (_picker is null ? 0 : _arrowW + 2);
        _box.SetBounds(padX, _padY, Math.Max(10, Width - padX - right), Height - _padY * 2);
    }

    int ZoneH => (Height - 4) / 2;
    int Divider => Width - _arrowW - 5;
    Rectangle UpZone => new(Width - _arrowW - 3, 2, _arrowW, ZoneH);
    Rectangle DownZone => new(Width - _arrowW - 3, 2 + ZoneH, _arrowW, ZoneH);
    Rectangle ClockZone => new(Divider - _arrowW - 2, 2, _arrowW, Height - 4);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hot = UpZone.Contains(e.Location) ? 1
                : DownZone.Contains(e.Location) ? -1
                : _picker is not null && ClockZone.Contains(e.Location) ? 2
                : 0;
        if (hot == _hot) return;
        _hot = hot;
        Cursor = hot == 0 ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = 0;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_hot == 2) _picker?.Invoke(this);
        else if (_hot != 0) Bump(_hot);
        else _box.Focus();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Bump(Math.Sign(e.Delta));
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Up or Keys.Down)) return;
        Bump(e.KeyCode == Keys.Up ? +1 : -1);
        e.Handled = e.SuppressKeyPress = true;
    }

    void Bump(int dir)
    {
        if (Stepper is null || !Enabled) return;
        _box.Text = Stepper(_box.Text, dir);
        _box.SelectionStart = _box.TextLength;
        _box.Focus();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _box.BackColor = Enabled ? Theme.FieldBg : Theme.Hover;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Backdrop(e.Graphics);
        var g = e.Graphics;
        var r = new Rectangle(0, 0, Width, Height);
        Theme.FillRound(g, r, Radius, Enabled ? Theme.FieldBg : Theme.Hover);

        if (_hot == 1) Theme.FillRound(g, UpZone, 4, Theme.Hover);
        else if (_hot == -1) Theme.FillRound(g, DownZone, 4, Theme.Hover);
        else if (_hot == 2) Theme.FillRound(g, ClockZone, 4, Theme.Hover);

        var ink = Enabled ? Theme.Muted : Theme.Faint;
        Theme.Chevron(g, UpZone, _hot == 1 ? Theme.Text : ink, -1);
        Theme.Chevron(g, DownZone, _hot == -1 ? Theme.Text : ink, +1);

        if (_picker is not null)
            Theme.ClockGlyph(g, ClockZone, _hot == 2 ? Theme.Accent : ink);

        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, Divider, 4, Divider, Height - 5);

        Theme.StrokeRound(g, r, Radius, _focus ? Theme.Accent : Theme.Border, _focus ? 1.4f : 1f);
    }
}

/// <summary>
/// Dropdown tu ve. Thay ComboBox vi dropdown cua ComboBox do he thong ve, khong nhan
/// mau nen — o dark mode no la mot o sang troi giua form toi.
/// </summary>
public sealed class Drop : Painted
{
    public string[] Items { get; set; } = [];
    public Func<int, Color?>? Dot { get; set; }
    public int Radius { get; set; } = 6;

    int _index;
    bool _hover, _open;

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _index;
        set
        {
            int v = Math.Clamp(value, 0, Math.Max(0, Items.Length - 1));
            if (v == _index) return;
            _index = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedItem => Items.Length == 0 ? "" : Items[Math.Clamp(_index, 0, Items.Length - 1)];

    readonly int _padX, _padY, _arrowW;

    public Drop(Font font, int width)
    {
        Font = font;
        Cursor = Cursors.Hand;
        TabStop = false;
        _padX = Math.Max(6, font.Height / 2);
        _padY = Math.Max(4, font.Height / 3);
        _arrowW = Math.Max(16, font.Height);
        Width = width;
        Height = font.Height + _padY * 2;
    }

    public override Size GetPreferredSize(Size proposed) => new(Width, Font.Height + _padY * 2);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Items.Length > 0) Open();
    }

    void Open()
    {
        // font popup to hon o mot chut cho de doc
        var list = new DropList(Items, _index, Theme.Font(Font.SizeInPoints + 0.8f), Width, Dot);
        var host = new ToolStripControlHost(list)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = list.Size,
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
        list.Picked += i =>
        {
            SelectedIndex = i;
            dd.Close();
        };
        dd.Closed += (_, _) => { _open = false; Invalidate(); };
        _open = true;
        Invalidate();
        dd.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Backdrop(e.Graphics);
        var g = e.Graphics;
        var r = new Rectangle(0, 0, Width, Height);
        Theme.FillRound(g, r, Radius, _hover || _open ? Theme.Hover : Theme.FieldBg);
        Theme.StrokeRound(g, r, Radius, _open ? Theme.Accent : Theme.Border, _open ? 1.4f : 1f);

        int x = _padX;
        if (Dot?.Invoke(_index) is { } dot)
        {
            int d = Math.Max(7, Font.Height / 2);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(dot);
            g.FillEllipse(b, x, (Height - d) / 2, d, d);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            x += d + 6;
        }

        var textBox = new Rectangle(x, 0, Width - x - _arrowW - 2, Height);
        TextRenderer.DrawText(g, SelectedItem, Font, textBox, Theme.Text,
            Theme.Flat | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        Theme.Chevron(g, new Rectangle(Width - _arrowW - 2, 0, _arrowW, Height), Theme.Muted, +1);
    }
}

/// <summary>Danh sach trong popup cua Drop.</summary>
sealed class DropList : Painted
{
    readonly string[] _items;
    readonly Func<int, Color?>? _dot;
    readonly int _rowH, _pad;
    int _selected, _hover = -1;

    public event Action<int>? Picked;

    public DropList(string[] items, int selected, Font font, int minWidth, Func<int, Color?>? dot)
    {
        _items = items;
        _selected = selected;
        _dot = dot;
        Font = font;
        _rowH = font.Height + Math.Max(8, font.Height / 2);
        _pad = Math.Max(4, font.Height / 4);

        int w = minWidth;
        foreach (var t in items)
            w = Math.Max(w, TextRenderer.MeasureText(t, font, new Size(int.MaxValue, int.MaxValue), Theme.Flat).Width
                            + font.Height * 3);
        Size = new Size(w, _items.Length * _rowH + _pad * 2);
        BackColor = Theme.MenuBg;
    }

    Rectangle RowAt(int i) => new(_pad, _pad + i * _rowH, Width - _pad * 2, _rowH);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = -1;
        for (int i = 0; i < _items.Length; i++)
            if (RowAt(i).Contains(e.Location)) { h = i; break; }
        if (h == _hover) return;
        _hover = h;
        Cursor = h < 0 ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_hover >= 0) { _selected = _hover; Picked?.Invoke(_hover); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var b = new SolidBrush(Theme.MenuBg)) g.FillRectangle(b, ClientRectangle);

        for (int i = 0; i < _items.Length; i++)
        {
            var row = RowAt(i);
            if (i == _hover) Theme.FillRound(g, row, 5, Theme.Hover);

            int x = row.Left + Math.Max(6, Font.Height / 2);
            if (_dot?.Invoke(i) is { } dot)
            {
                int d = Math.Max(7, Font.Height / 2);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var db = new SolidBrush(dot);
                g.FillEllipse(db, x, row.Top + (row.Height - d) / 2, d, d);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                x += d + 6;
            }

            int tickW = Font.Height;
            TextRenderer.DrawText(g, _items[i], Font,
                new Rectangle(x, row.Top, row.Width - (x - row.Left) - tickW - 4, row.Height),
                Theme.Text,
                Theme.Flat | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (i == _selected)
                Theme.Tick(g, new Rectangle(row.Right - tickW - 2, row.Top + row.Height / 4, tickW, row.Height / 2),
                           Theme.Accent, 1.7f);
        }
    }
}

/// <summary>Checkbox tu ve: o bo goc, tick ve bang net, co nhan chu ben canh.</summary>
public sealed class Check : Painted
{
    bool _checked, _hover;
    readonly int _boxSize, _gap;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Check(string text, Font font)
    {
        Font = font;
        Text = text;
        Cursor = Cursors.Hand;
        AutoSize = true;
        TabStop = false;
        _boxSize = Math.Max(14, font.Height * 3 / 4);
        _gap = Math.Max(6, font.Height / 3);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var s = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), Theme.Flat);
        return new Size(_boxSize + _gap + s.Width + 2, Math.Max(_boxSize, s.Height) + 4);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Backdrop(e.Graphics);
        var g = e.Graphics;
        var box = new Rectangle(0, (Height - _boxSize) / 2, _boxSize, _boxSize);

        if (_checked)
        {
            Theme.FillRound(g, box, 4, Theme.Accent);
            Theme.Tick(g, box, Theme.OnAccent, 1.8f);
        }
        else
        {
            Theme.FillRound(g, box, 4, _hover ? Theme.Hover : Theme.FieldBg);
            Theme.StrokeRound(g, box, 4, _hover ? Theme.Muted : Theme.Border);
        }

        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(_boxSize + _gap, 0, Width - _boxSize - _gap, Height), Theme.Text,
            Theme.Flat | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
