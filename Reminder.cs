namespace Deskcal;

/// <summary>
/// Vong quet 60 giay tren UI thread. Co tinh khong dung SystemEvents.PowerModeChanged:
/// event do ban tren thread rieng, keo theo phai dong bo truy cap SQLite. Timer cua
/// WinForms van chay tiep sau khi may thuc, nen cham nhat la mot luot (60s) — doi lay
/// viec khong co concurrency thi rat dang.
/// </summary>
public sealed class Reminder
{
    public static TimeOnly AllDayAt { get; set; } = new(9, 0);
    public static int GraceHours { get; set; } = 12;
    const int MaxPerTick = 5;

    readonly Store _store;
    readonly System.Windows.Forms.Timer _timer;

    /// <summary>Ban khi vua ban toast, de tray cap nhat lai menu.</summary>
    public event Action? Fired;

    public Reminder(Store store)
    {
        _store = store;
        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += (_, _) => Scan();
    }

    public void Start()
    {
        _timer.Start();
        Log.Write($"nhac: bat — quet 60s/lan, viec ca ngay bao {AllDayAt:HH\\:mm}, bo qua neu qua han > {GraceHours}h");
        Scan();   // quet ngay khi khoi dong = bao bu sau logon / sau khi may ngu lau
    }

    public int Scan()
    {
        try
        {
            var now = DateTime.Now;
            var due = _store.DueNow(now, AllDayAt, GraceHours);
            if (due.Count == 0) return 0;

            int sent = 0;
            foreach (var o in due)
            {
                if (sent >= MaxPerTick)
                {
                    // khong am tham cat: ghi ro con bao nhieu
                    Log.Write($"nhac: con {due.Count - sent} viec, se bao luot sau (gioi han {MaxPerTick}/luot)");
                    break;
                }
                Toast.Show(o.Title, Body(o, now));
                _store.MarkNotified(o.Id, o.On);
                Log.Write($"nhac: da bao #{o.Id} \"{o.Title}\" ({o.On:yyyy-MM-dd} {o.RangeLabel}, lead={o.LeadMinutes}p)");
                sent++;
            }
            if (sent > 0) Fired?.Invoke();
            return sent;
        }
        catch (Exception e)
        {
            Log.Write($"nhac: loi luot quet: {e}");
            return 0;
        }
    }

    static string Body(Occurrence o, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        // co "nhac truoc 1 ngay" nen viec ngay mai cung ban toast hom nay -> phai noi ro ngay
        string day = o.On == today ? "Hôm nay"
                   : o.On == today.AddDays(1) ? "Ngày mai"
                   : o.On == today.AddDays(-1) ? "Hôm qua"
                   : o.On.ToString("dd/MM");
        string time = o.At is null ? "" : o.RangeLabel + " — ";
        return $"{time}{day} · {o.Tag}";
    }
}
