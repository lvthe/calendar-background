namespace Deskcal;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool dpiOk = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Paths.MigrateLegacy();

        if (args.Contains("--self-test"))
        {
            int i = Array.IndexOf(args, "--self-test");
            return SelfTest.Run(i + 1 < args.Length
                ? args[i + 1]
                : Path.Combine(Path.GetTempPath(), "deskcal-selftest.txt"));
        }

        // them viec tu dong lenh, tien de gan hotkey hoac goi tu script khac:
        //   deskcal.exe --add "Hop team|mai|14:30|work|WEEKLY"
        // noi lai het phan sau --add: quen bo nhay thi shell cat theo dau cach,
        // neu chi lay args[1] thi tieu de bi mat chu ma khong bao gi
        if (args is ["--add", .. var rest] && rest.Length > 0)
            return AddFromCli(string.Join(' ', rest));

        if (args.Contains("--test-toast"))
        {
            Toast.Register();
            Toast.Show("deskcal test", "Nếu bạn thấy cái này thì notification chạy được.");
            Thread.Sleep(1500);   // cho WinRT kip day toast ra truoc khi process chet
            return 0;
        }

        // autostart + mo tay co the trung nhau; hai instance se ban toast doi
        using var only = new Mutex(true, @"Local\deskcal-single-instance", out bool first);
        if (!first)
        {
            MessageBox.Show("deskcal đang chạy rồi — xem icon dưới khay hệ thống.",
                "deskcal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            using var store = new Store(Paths.Db);
            Log.Write($"khoi dong, DB: {store.DbPath}");
            Log.Write($"dpi: SetHighDpiMode={dpiOk}, mode={Application.HighDpiMode}");
            Application.Run(new TrayApp(store, openCalendar: args.Contains("--open")));
            Log.Write("thoat");
            return 0;
        }
        catch (Exception e)
        {
            Log.Write($"chet: {e}");
            MessageBox.Show($"deskcal lỗi và phải thoát:\n\n{e.Message}\n\nChi tiết trong:\n{Paths.LogFile}",
                "deskcal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>title|ngay|gio|nhan|rrule — chi title la bat buoc, con lai co mac dinh.</summary>
    static int AddFromCli(string spec)
    {
        try
        {
            var f = spec.Split('|');
            string title = f[0].Trim();
            if (title.Length == 0) { Log.Write("--add: thieu tieu de"); return 2; }

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!Parse.TryDate(f.ElementAtOrDefault(1), today, out var date))
            {
                Log.Write($"--add: khong hieu ngay \"{f.ElementAtOrDefault(1)}\"");
                return 2;
            }
            if (!Parse.TryTime(f.ElementAtOrDefault(2), out var at))
            {
                Log.Write($"--add: khong hieu gio \"{f.ElementAtOrDefault(2)}\"");
                return 2;
            }

            string tag = f.ElementAtOrDefault(3) is { Length: > 0 } g && Store.Tags.Contains(g) ? g : "work";
            string rrule = f.ElementAtOrDefault(4) is { Length: > 0 } r && Store.Rrules.Contains(r) ? r : "NONE";

            using var store = new Store(Paths.Db);
            long id = store.Add(new TaskRow(0, title, date, at, null, tag, rrule, 0, null, null));
            Log.Write($"--add #{id} \"{title}\" {date:yyyy-MM-dd} " +
                      $"{at?.ToString("HH\\:mm") ?? "(ca ngay)"} {tag} {rrule}");
            return 0;
        }
        catch (Exception e)
        {
            Log.Write($"--add loi: {e.Message}");
            return 1;
        }
    }
}
