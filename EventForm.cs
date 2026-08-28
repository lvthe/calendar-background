namespace Deskcal;

/// <summary>
/// Form them / sua mot viec. Sua thi ap cho ca chuoi lap (tasks la 1 dong goc),
/// rieng o "Da xong" chi ap cho dung lan lap dang mo.
///
/// Toan bo o nhap la control tu ve trong Controls.cs. Layout dung TableLayoutPanel +
/// AutoSize, khoang cach theo Font.Height nen tu dung o moi DPI.
/// </summary>
public sealed class EventForm : Form
{
    static readonly (string Value, string Label)[] RruleChoices =
    [
        ("NONE", "Không lặp"),
        ("DAILY", "Mỗi ngày"),
        ("WEEKLY", "Mỗi tuần"),
        ("MONTHLY", "Mỗi tháng"),
        ("YEARLY", "Mỗi năm"),
    ];

    const int StepMinutes = 15;

    readonly long _id;
    readonly TextBox _title = new();
    readonly StepBox _date;
    readonly StepBox _from;
    readonly StepBox _to;
    readonly Check _allDay;
    readonly Drop _rrule;
    readonly Drop _lead;
    readonly Drop _tag;
    readonly Field _place;
    readonly Field _note;
    readonly Check _done;
    readonly Label _error = new();

    public TaskRow? Result { get; private set; }
    public bool DoneChecked => _done.Checked;
    public bool DeleteRequested { get; private set; }

    public EventForm(TaskRow? row, DateOnly date, bool done)
    {
        Theme.Refresh();
        _id = row?.Id ?? 0;
        bool editing = row is not null;

        var body = Theme.Font(10.5f);
        var label = Theme.Font(9.5f);
        Font = body;
        int u = body.Height;

        Text = editing ? "Sửa việc" : "Việc mới";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.FormBg;
        ForeColor = Theme.Text;

        int fieldW = u * 10;
        _date = new StepBox(body, fieldW) { Stepper = StepDate };
        _from = new StepBox(body, u * 8) { Placeholder = "--:--" };
        _to = new StepBox(body, u * 8) { Placeholder = "--:--" };
        _allDay = new Check("Cả ngày", body);
        _rrule = new Drop(body, fieldW);
        _lead = new Drop(body, fieldW);
        _tag = new Drop(body, fieldW);
        _place = new Field(body) { Placeholder = "Phòng họp, địa chỉ…" };
        _note = new Field(body, multiline: true);
        _done = new Check("", body);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(u + u / 2, u, u + u / 2, u),
            BackColor = Theme.FormBg,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, u * 24));

        // ---- tieu de: o lon khong vien, kieu Notion ----
        _title.Text = row?.Title ?? "";
        _title.PlaceholderText = "Tên việc";
        _title.Font = Theme.Font(15f);
        _title.BorderStyle = BorderStyle.None;
        _title.BackColor = Theme.FormBg;
        _title.ForeColor = Theme.Text;
        _title.Dock = DockStyle.Fill;
        _title.Margin = new Padding(0, 0, 0, u / 2);
        AddSpan(grid, _title);

        AddSpan(grid, Rule(u));

        // ---- ngay ----
        _date.Text = date.ToString("dd/MM/yyyy");
        AddRow(grid, label, "Ngày", Line(u, _date, Hint("dd/MM · +3 · Mai", label)));

        // ---- gio ----
        _from.Text = row?.At?.ToString("HH\\:mm") ?? "";
        _to.Text = row?.End?.ToString("HH\\:mm") ?? "";
        _from.Stepper = (t, d) => StepTime(t, d, "09:00");
        _to.Stepper = (t, d) => StepTime(t, d, "10:00");
        _from.Picker = b => Clock.Show(b, body);
        _to.Picker = b => Clock.Show(b, body);

        // roi o gio bat dau ma chua co gio ket thuc thi tu dien +1 tieng
        _from.Committed += (_, _) =>
        {
            if (_to.Text.Trim().Length > 0) return;
            if (Parse.TryTime(_from.Text, out var f) && f is not null)
                _to.Text = f.Value.AddHours(1).ToString("HH\\:mm");
        };

        _allDay.Checked = editing && row!.At is null;
        _allDay.CheckedChanged += (_, _) =>
        {
            _from.Enabled = _to.Enabled = !_allDay.Checked;
            if (_allDay.Checked) { _from.Text = ""; _to.Text = ""; }
        };
        _from.Enabled = _to.Enabled = !_allDay.Checked;
        AddRow(grid, label, "Giờ", Line(u, _from, Hint("→", label), _to, _allDay));

        // ---- lap lai ----
        _rrule.Items = RruleChoices.Select(c => c.Label).ToArray();
        _rrule.SelectedIndex = Math.Max(0, Array.FindIndex(RruleChoices, c => c.Value == (row?.Rrule ?? "NONE")));
        AddRow(grid, label, "Lặp lại", _rrule);

        // ---- nhac truoc ----
        _lead.Items = Store.Leads.Select(l => l.Label).ToArray();
        _lead.SelectedIndex = Math.Max(0, Array.FindIndex(Store.Leads, l => l.Minutes == (row?.LeadMinutes ?? 0)));
        AddRow(grid, label, "Nhắc trước", _lead);

        // ---- nhan: kem dot mau ----
        _tag.Items = Store.Tags;
        _tag.Dot = i => Theme.TagColor(Store.Tags[i]);
        _tag.SelectedIndex = Math.Max(0, Array.IndexOf(Store.Tags, row?.Tag ?? "work"));
        AddRow(grid, label, "Nhãn", _tag);

        // ---- noi ----
        _place.Text = row?.Location ?? "";
        _place.Dock = DockStyle.Fill;
        AddRow(grid, label, "Nơi", _place);

        // ---- ghi chu ----
        _note.Text = row?.Note ?? "";
        _note.Dock = DockStyle.Fill;
        _note.Height = u * 5;
        AddRow(grid, label, "Ghi chú", _note);

        // ---- da xong (chi khi sua) ----
        if (editing)
        {
            _done.Text = $"Đã xong (chỉ ngày {date:dd/MM})";
            _done.Checked = done;
            AddRow(grid, label, "", _done);
        }

        if (row?.Rrule is not null and not "NONE")
            AddRow(grid, label, "", Hint("Sửa sẽ áp cho cả chuỗi lặp.", label));

        // ---- loi ----
        _error.ForeColor = Theme.Danger;
        _error.Font = label;
        _error.AutoSize = false;
        _error.Anchor = AnchorStyles.Left;
        _error.BackColor = Color.Transparent;
        // chua san chieu cao de khi hien loi thi hang nut khong bi day xuong
        _error.Size = new Size(u * 22, label.Height + 2);
        _error.Margin = new Padding(0, u / 3, 0, 0);
        AddSpan(grid, _error);

        AddSpan(grid, Rule(u));

        // ---- nut ----
        var save = new Btn(editing ? "Lưu" : "Thêm", BtnKind.Primary, body, u, u / 2)
        {
            DialogResult = DialogResult.OK,
        };
        var cancel = new Btn("Huỷ", BtnKind.Default, body, u, u / 2)
        {
            DialogResult = DialogResult.Cancel,
        };

        var rightSide = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        cancel.Margin = new Padding(0, 0, 0, 0);
        save.Margin = new Padding(u / 2, 0, 0, 0);
        rightSide.Controls.Add(cancel);
        rightSide.Controls.Add(save);

        // Panel + AutoSize khong dung duoc o day: control con Dock khong dong gop vao
        // PreferredSize nen panel co ve 0 va hai nut bien mat. TableLayoutPanel thi co.
        var bar = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (editing)
        {
            var del = new Btn("Xoá", BtnKind.Danger, body, u, u / 2) { Anchor = AnchorStyles.Left };
            del.Click += (_, _) => ConfirmDelete();
            bar.Controls.Add(del, 0, 0);
        }
        bar.Controls.Add(rightSide, 1, 0);
        AddSpan(grid, bar);

        Controls.Add(grid);
        AcceptButton = save;
        CancelButton = cancel;

        FormClosing += OnClosing;
        ActiveControl = _title;
    }

    // ---------- buoc tang giam ----------

    static string StepDate(string text, int dir)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!Parse.TryDate(text, today, out var d)) d = today;
        return d.AddDays(dir).ToString("dd/MM/yyyy");
    }

    /// <summary>O trong thi nhay ve gio goi y; co roi thi cong tru 15 phut.</summary>
    static string StepTime(string text, int dir, string seed)
    {
        if (!Parse.TryTime(text, out var at) || at is null) return seed;
        return at.Value.AddMinutes(dir * StepMinutes).ToString("HH\\:mm");
    }

    string RruleValue => RruleChoices[Math.Clamp(_rrule.SelectedIndex, 0, RruleChoices.Length - 1)].Value;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(Handle);
    }

    void ConfirmDelete()
    {
        var ok = MessageBox.Show(
            $"Xoá \"{_title.Text.Trim()}\"" + (RruleValue == "NONE" ? "" : " và mọi lần lặp") + "?",
            "deskcal", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (ok != DialogResult.Yes) return;
        DeleteRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK || DeleteRequested) return;

        string title = _title.Text.Trim();
        if (title.Length == 0) { Reject("Chưa nhập tên việc.", _title, e); return; }

        if (!Parse.TryDate(_date.Text, DateOnly.FromDateTime(DateTime.Today), out var d))
        {
            Reject("Không hiểu ngày. Thử: Mai, +3, 15/09.", _date, e);
            return;
        }

        TimeOnly? from = null, to = null;
        if (!_allDay.Checked)
        {
            if (!Parse.TryTime(_from.Text, out from)) { Reject("Giờ bắt đầu không hiểu. Thử 14:30.", _from, e); return; }
            if (!Parse.TryTime(_to.Text, out to)) { Reject("Giờ kết thúc không hiểu. Thử 15:30.", _to, e); return; }
            if (from is null && to is not null) { Reject("Có giờ kết thúc mà thiếu giờ bắt đầu.", _from, e); return; }
            if (from is not null && to is not null && to < from) { Reject("Giờ kết thúc trước giờ bắt đầu.", _to, e); return; }
        }

        Result = new TaskRow(_id, title, d, from, to,
            Store.Tags[Math.Clamp(_tag.SelectedIndex, 0, Store.Tags.Length - 1)],
            RruleValue,
            Store.Leads[Math.Clamp(_lead.SelectedIndex, 0, Store.Leads.Length - 1)].Minutes,
            _place.Text, _note.Text);
    }

    void Reject(string msg, Control focus, FormClosingEventArgs e)
    {
        e.Cancel = true;
        _error.Text = msg;
        focus.Focus();
        if (focus is TextBoxBase tb) tb.SelectAll();
    }

    // ---------- dung layout ----------

    static Panel Rule(int u) => new()
    {
        Height = 1,
        Dock = DockStyle.Fill,
        BackColor = Theme.Border,
        Margin = new Padding(0, u / 2, 0, u / 2),
    };

    void AddRow(TableLayoutPanel grid, Font labelFont, string text, Control field)
    {
        int r = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var cap = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = field is Field { Box.Multiline: true }
                ? AnchorStyles.Top | AnchorStyles.Left
                : AnchorStyles.Left,
            ForeColor = Theme.Muted,
            Font = labelFont,
            BackColor = Color.Transparent,
            Margin = new Padding(0, field is Field { Box.Multiline: true } ? Font.Height / 2 : 0, Font.Height, 0),
        };
        grid.Controls.Add(cap, 0, r);

        field.Margin = new Padding(0, Font.Height / 4, 0, Font.Height / 4);
        if (field.Dock == DockStyle.None) field.Anchor = AnchorStyles.Left;
        grid.Controls.Add(field, 1, r);
    }

    void AddSpan(TableLayoutPanel grid, Control c)
    {
        int r = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(c, 0, r);
        grid.SetColumnSpan(c, 2);
    }

    /// <summary>Xep nhieu control tren cung mot dong, cach nhau theo don vi font.</summary>
    static FlowLayoutPanel Line(int u, params Control[] items)
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        foreach (var c in items)
        {
            c.Margin = new Padding(0, 0, u / 2, 0);
            c.Anchor = AnchorStyles.Left;
            p.Controls.Add(c);
        }
        return p;
    }

    static Label Hint(string text, Font font) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Faint,
        Font = font,
        BackColor = Color.Transparent,
    };
}
