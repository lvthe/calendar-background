namespace Deskcal;

public sealed class TrayApp : ApplicationContext
{
    readonly Store _store;
    readonly Reminder _reminder;
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();
    MainWindow? _window;

    public TrayApp(Store store, bool openCalendar = false)
    {
        _store = store;
        Toast.Register();

        _icon = new NotifyIcon
        {
            Icon = TrayIcon.Make(),
            Text = "deskcal — double-click để mở lịch",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        Toast.Fallback = (t, b) => _icon.ShowBalloonTip(8000, t, b, ToolTipIcon.Info);

        _menu.Renderer = new MenuRenderer();
        _menu.DropShadowEnabled = true;

        // dung lai menu moi lan mo, khoi phai theo doi thay doi
        _menu.Opening += (_, _) => BuildMenu();
        _icon.MouseDoubleClick += (_, _) => OpenCalendar();

        _reminder = new Reminder(_store);
        _reminder.Fired += () => _window?.Reload();
        _reminder.Start();
        Welcome();
        if (openCalendar) OpenCalendar();
    }

    // ---------- cua so ----------

    MainWindow Window()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new MainWindow(_store);
            _window.Changed += () => { BuildMenu(); Wallpaper.Refresh(_store); };
        }
        return _window;
    }

    void OpenCalendar(DateOnly? jumpTo = null)
    {
        var w = Window();
        w.Reload(jumpTo);
        // gap truong hop cua so hien ra o trang thai minimize (toa do -32000): ep ve
        // Normal TRUOC khi Show, roi moi keo len truoc
        if (w.WindowState != FormWindowState.Normal) w.WindowState = FormWindowState.Normal;
        if (!w.Visible) w.Show();
        w.BringToFront();
        w.Activate();
    }

    // ---------- menu ----------

    void BuildMenu()
    {
        Theme.Refresh();
        _menu.Font = Theme.Font(9.5f);
        _menu.Items.Clear();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var all = _store.Range(today.AddDays(-30), today.AddDays(7));

        var overdue = all.Where(o => !o.Done && o.On < today).OrderByDescending(o => o.On).ToList();
        var todays = all.Where(o => o.On == today).ToList();
        var soon = all.Where(o => !o.Done && o.On > today).ToList();

        _menu.Items.Add(new ToolStripMenuItem("Mở lịch", null, (_, _) => OpenCalendar())
        {
            Font = Theme.Font(9.5f, FontStyle.Bold),
        });
        _menu.Items.Add(new ToolStripSeparator());

        if (overdue.Count > 0)
        {
            _menu.Items.Add(Header($"Quá hạn ({overdue.Count})"));
            foreach (var o in overdue.Take(8)) _menu.Items.Add(Row(o, withDate: true));
            _menu.Items.Add(new ToolStripSeparator());
        }

        _menu.Items.Add(Header($"Hôm nay — {today:dd/MM}"));
        if (todays.Count == 0) _menu.Items.Add(new ToolStripMenuItem("(trống)") { Enabled = false });
        else foreach (var o in todays) _menu.Items.Add(Row(o, withDate: false));

        if (soon.Count > 0)
        {
            var sub = new ToolStripMenuItem($"Sắp tới ({soon.Count})");
            foreach (var o in soon.Take(25)) sub.DropDownItems.Add(Row(o, withDate: true));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(sub);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Thêm việc…", null, (_, _) => QuickAdd()));
        _menu.Items.Add(new ToolStripMenuItem("Hiện lịch lên nền desktop", null, (_, _) => ToggleWallpaper())
        {
            Checked = Wallpaper.Enabled,
        });
        _menu.Items.Add(new ToolStripMenuItem("Chạy cùng Windows", null, (_, _) => Autostart.Toggle())
        {
            Checked = Autostart.Enabled,
        });
        _menu.Items.Add(new ToolStripMenuItem("Mở thư mục dữ liệu", null, (_, _) => OpenDataDir()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Thoát", null, (_, _) => Quit()));
    }

    static ToolStripMenuItem Header(string text) => new(text)
    {
        Enabled = false,
        Font = Theme.Font(9f, FontStyle.Bold),
    };

    ToolStripMenuItem Row(Occurrence o, bool withDate)
    {
        string label = (withDate ? $"{o.On:dd/MM}  " : "")
                     + (o.At is null ? "" : $"{o.TimeLabel}  ")
                     + o.Title;

        var it = new ToolStripMenuItem(label)
        {
            Checked = o.Done,
            Font = Theme.Font(9.5f, o.Done ? FontStyle.Strikeout : FontStyle.Regular),
            Tag = o.Done ? "done" : null,
            ToolTipText = $"{o.Tag} · click = xong / mở lại · Shift+click = mở lịch ngày đó",
        };
        it.Click += (_, _) =>
        {
            // click thuong de tick nhanh vi do la thao tac hay dung nhat;
            // muon sua thi Shift de nhay sang cua so lich
            if (Control.ModifierKeys.HasFlag(Keys.Shift)) OpenCalendar(o.On);
            else _store.SetDone(o.Id, o.On, !o.Done);
            _window?.Reload();
            Wallpaper.Refresh(_store);
        };
        return it;
    }

    // ---------- hanh dong ----------

    void QuickAdd()
    {
        using var f = new EventForm(null, DateOnly.FromDateTime(DateTime.Today), false);
        if (f.ShowDialog() != DialogResult.OK || f.Result is null) return;
        long id = _store.Add(f.Result);
        Log.Write($"them #{id} \"{f.Result.Title}\" {f.Result.Date:yyyy-MM-dd} " +
                  $"{f.Result.At?.ToString("HH\\:mm") ?? "(ca ngay)"} {f.Result.Rrule} lead={f.Result.LeadMinutes}");
        _reminder.Scan();   // dat gio da qua thi bao ngay, khoi doi het phut
        _window?.Reload();
    }

    void ToggleWallpaper()
    {
        if (Wallpaper.Enabled) Wallpaper.Disable();
        else Wallpaper.Enable(_store);
    }

    void OpenDataDir()
    {
        Directory.CreateDirectory(Paths.Dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Paths.Dir) { UseShellExecute = true });
    }

    /// <summary>
    /// Win11 nhet icon moi vao vung an, nen lan chay dau phai noi cho nguoi dung biet
    /// app dang song va icon o dau. Danh dau bang file co de chi bao dung mot lan.
    /// </summary>
    void Welcome()
    {
        try
        {
            var flag = Path.Combine(Paths.Dir, ".welcomed");
            if (File.Exists(flag)) return;
            Directory.CreateDirectory(Paths.Dir);
            File.WriteAllText(flag, DateTime.Now.ToString("O"));
            Toast.Show("deskcal đang chạy",
                "Icon ở khay hệ thống, góc dưới phải. Double-click để mở lịch.");
            Log.Write("ban toast chao lan dau");
        }
        catch (Exception e) { Log.Write($"welcome that bai: {e.Message}"); }
    }

    void Quit()
    {
        if (_window is not null && !_window.IsDisposed)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        _icon.Visible = false;
        _icon.Dispose();
        _store.Dispose();
        ExitThread();
    }
}
