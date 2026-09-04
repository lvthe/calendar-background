namespace Deskcal;

/// <summary>
/// Cua so lich thang. Bam X thi an xuong tray chu khong thoat app — notifier phai
/// tiep tuc chay, do moi la ly do app ton tai.
///
/// Header dung TableLayoutPanel + AutoSize, khoang cach theo Font.Height, nen tu
/// dung o moi DPI. Kich thuoc cua so ban dau tinh theo % man hinh dang chua no.
/// </summary>
public sealed class MainWindow : Form
{
    readonly Store _store;
    readonly CalendarView _view;
    readonly Label _title = new();

    /// <summary>Chi true khi nguoi dung chon Thoat tu menu tray.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Ban khi du lieu doi, de tray cap nhat theo.</summary>
    public event Action? Changed;

    public MainWindow(Store store)
    {
        _store = store;
        Theme.Refresh();

        Font = Theme.Font(10f);
        Text = "deskcal";
        Icon = TrayIcon.Make();
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.FormBg;
        ForeColor = Theme.Text;
        KeyPreview = true;

        // Kich thuoc theo don vi font, KHONG theo % man hinh: may co 2 man hinh khac
        // DPI thi phep tinh % tron pixel cua hai he toa do va cua so nhay ra ngoai
        // vien. u tinh tu font nen tu lon len o man hinh DPI cao.
        int u = Font.Height;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(u * 52, u * 34);
        MinimumSize = new Size(u * 34, u * 24);

        _view = new CalendarView(_store) { Dock = DockStyle.Fill };
        _view.DayActivated += AddOn;
        _view.EventActivated += Edit;
        _view.StatusToggled += Toggle;

        // WinForms dock theo thu tu nguoc trong Controls: Fill them truoc,
        // roi cac control Top them sau se xep tu duoi len tren
        Controls.Add(_view);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(BuildHeader());
        Sync();

        FormClosing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            Hide();
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Hide(); return; }
            if (e.Control && e.KeyCode == Keys.N) { AddOn(DefaultDate()); return; }
            if (e.KeyCode == Keys.Left) { Shift(-1); e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Shift(1); e.Handled = true; }
            if (e.KeyCode == Keys.T) Today();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
        Log.Write($"cua so: DeviceDpi={DeviceDpi}, ClientSize={ClientSize.Width}x{ClientSize.Height}");
    }

    // ---------- header ----------

    Control BuildHeader()
    {
        int u = Font.Height;

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(u, u / 2, u, u / 2),
            BackColor = Theme.FormBg,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // ‹
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // ›
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // tieu de, an het cho con lai
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Hom nay
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // + Them viec
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var body = Theme.Font(10.5f);
        // mui ve bang vector, khong dung ky tu font (‹ › nho va lech tam)
        var prev = Nav("", body, u);
        prev.Icon = IconKind.ChevronLeft;
        prev.Click += (_, _) => Shift(-1);

        var next = Nav("", body, u);
        next.Icon = IconKind.ChevronRight;
        next.Click += (_, _) => Shift(1);

        _title.Text = " ";
        _title.Font = Theme.Font(16.5f, FontStyle.Bold);
        _title.ForeColor = Theme.Text;
        _title.AutoSize = true;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.BackColor = Color.Transparent;
        _title.Margin = new Padding(u, 0, 0, 0);
        _title.Anchor = AnchorStyles.Left;

        var today = Nav("Hôm nay", body, u);
        today.Click += (_, _) => Today();

        var add = Nav("Thêm việc", body, u, BtnKind.Primary);
        add.Click += (_, _) => AddOn(DefaultDate());

        bar.Controls.Add(prev, 0, 0);
        bar.Controls.Add(next, 1, 0);
        bar.Controls.Add(_title, 2, 0);
        bar.Controls.Add(today, 3, 0);
        bar.Controls.Add(add, 4, 0);
        return bar;
    }

    static Btn Nav(string text, Font font, int u, BtnKind kind = BtnKind.Default) =>
        new(text, kind, font, u * 2 / 3, u / 3)
        {
            Margin = new Padding(0, 0, u / 3, 0),
            Anchor = AnchorStyles.None,
        };

    // ---------- dieu huong ----------

    void Shift(int months)
    {
        _view.Month = _view.Month.AddMonths(months);
        Sync();
    }

    void Today()
    {
        _view.Month = DateOnly.FromDateTime(DateTime.Today);
        Sync();
    }

    void Sync() => _title.Text = $"Tháng {_view.Month.Month}, {_view.Month.Year}";

    /// <summary>Hom nay neu dang xem thang nay, khong thi ngay 1 cua thang dang xem.</summary>
    DateOnly DefaultDate()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return today.Year == _view.Month.Year && today.Month == _view.Month.Month ? today : _view.Month;
    }

    public void Reload(DateOnly? jumpTo = null)
    {
        if (jumpTo is not null) { _view.Month = jumpTo.Value; Sync(); }
        else _view.Reload();
    }

    // ---------- them / sua ----------

    void AddOn(DateOnly date)
    {
        using var f = new EventForm(null, date, false);
        if (f.ShowDialog(this) != DialogResult.OK || f.Result is null) return;
        long id = _store.Add(f.Result);
        Log.Write($"them #{id} \"{f.Result.Title}\" {f.Result.Date:yyyy-MM-dd} " +
                  $"{f.Result.At?.ToString("HH\\:mm") ?? "(ca ngay)"} {f.Result.Rrule} lead={f.Result.LeadMinutes}");
        Done();
    }

    /// <summary>Bam o trang thai: doi xong / chua xong ngay tai lich, khong mo form.</summary>
    void Toggle(Occurrence o)
    {
        _store.SetDone(o.Id, o.Key, !o.Done);
        Log.Write((o.Done ? "mo lai #" : "xong #") + o.Id + " " + o.Title + " " + o.Key.ToString("yyyy-MM-dd"));
        Done();
    }

    void Edit(Occurrence o)
    {
        var row = _store.Get(o.Id);
        if (row is null) { Reload(); return; }

        using var f = new EventForm(row, o.On, o.Done);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        if (f.DeleteRequested)
        {
            _store.Delete(o.Id);
            Log.Write($"xoa #{o.Id} \"{row.Title}\"");
            Done();
            return;
        }

        if (f.Result is null) return;
        var on = _store.UpdateAndSetDone(f.Result, o.Key, f.DoneChecked);
        Log.Write($"sua #{o.Id} \"{f.Result.Title}\" {f.Result.Date:yyyy-MM-dd} lead={f.Result.LeadMinutes}"
                  + (on == o.Key ? "" : $" (tick doi sang {on:yyyy-MM-dd})"));
        Done();
    }

    void Done()
    {
        _view.Reload();
        Changed?.Invoke();
    }
}
