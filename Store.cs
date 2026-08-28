using Microsoft.Data.Sqlite;

namespace Deskcal;

/// <summary>Task goc trong bang tasks — dung khi them va khi sua.</summary>
public sealed record TaskRow(
    long Id,
    string Title,
    DateOnly Date,
    TimeOnly? At,
    TimeOnly? End,
    string Tag,
    string Rrule,
    int LeadMinutes,
    string? Location,
    string? Note);

/// <summary>Mot lan xuat hien cu the cua task (task lap se co nhieu Occurrence).</summary>
public sealed record Occurrence(
    long Id,
    string Title,
    DateOnly On,
    TimeOnly? At,
    TimeOnly? End,
    string Tag,
    string Rrule,
    int LeadMinutes,
    bool Done)
{
    /// <summary>Thoi diem viec dien ra.</summary>
    public DateTime When(TimeOnly allDayAt) => On.ToDateTime(At ?? allDayAt);

    /// <summary>Thoi diem ban toast = luc dien ra tru di so phut nhac truoc.</summary>
    public DateTime FireAt(TimeOnly allDayAt) => When(allDayAt).AddMinutes(-LeadMinutes);

    public string TimeLabel => At?.ToString("HH\\:mm") ?? "";

    public string RangeLabel => At is null
        ? "cả ngày"
        : End is null ? TimeLabel : $"{TimeLabel}–{End.Value:HH\\:mm}";
}

public sealed class Store : IDisposable
{
    public static readonly string[] Tags = ["work", "ops", "deadline", "personal"];
    public static readonly string[] Rrules = ["NONE", "DAILY", "WEEKLY", "MONTHLY", "YEARLY"];

    /// <summary>Nhac truoc bao lau. Gioi han 1 ngay de cua so quet khong phai keo dai.</summary>
    public static readonly (int Minutes, string Label)[] Leads =
    [
        (0, "Đúng giờ"),
        (5, "5 phút trước"),
        (10, "10 phút trước"),
        (15, "15 phút trước"),
        (30, "30 phút trước"),
        (60, "1 giờ trước"),
        (120, "2 giờ trước"),
        (1440, "1 ngày trước"),
    ];

    const int MaxLeadDays = 1;

    readonly SqliteConnection _db;
    public string DbPath { get; }

    public Store(string dbPath)
    {
        DbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        Raw("PRAGMA journal_mode = WAL");
        Raw(@"
            CREATE TABLE IF NOT EXISTS tasks (
              id         INTEGER PRIMARY KEY AUTOINCREMENT,
              title      TEXT    NOT NULL,
              due_date   TEXT    NOT NULL,          -- YYYY-MM-DD, ngay goc
              due_time   TEXT,                      -- HH:MM, null = viec ca ngay
              tag        TEXT    NOT NULL DEFAULT 'work',
              rrule      TEXT    NOT NULL DEFAULT 'NONE',
              note       TEXT,
              created_at TEXT    NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_tasks_due ON tasks(due_date);

            -- tick xong theo tung lan lap, khong nhet vao mot cot done duy nhat
            CREATE TABLE IF NOT EXISTS completions (
              task_id   INTEGER NOT NULL,
              occurs_on TEXT    NOT NULL,
              at        TEXT    NOT NULL DEFAULT (datetime('now')),
              PRIMARY KEY (task_id, occurs_on)
            );

            -- da ban toast cho lan lap nao, de khong ban trung
            CREATE TABLE IF NOT EXISTS notified (
              task_id   INTEGER NOT NULL,
              occurs_on TEXT    NOT NULL,
              at        TEXT    NOT NULL DEFAULT (datetime('now')),
              PRIMARY KEY (task_id, occurs_on)
            );
        ");
        Migrate();
    }

    /// <summary>Them cot moi vao DB da ton tai, khong dap bang nen khong mat du lieu.</summary>
    void Migrate()
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM pragma_table_info('tasks')";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cols.Add(r.GetString(0));
        }
        if (!cols.Contains("end_time")) Raw("ALTER TABLE tasks ADD COLUMN end_time TEXT");
        if (!cols.Contains("lead_minutes")) Raw("ALTER TABLE tasks ADD COLUMN lead_minutes INTEGER NOT NULL DEFAULT 0");
        if (!cols.Contains("location")) Raw("ALTER TABLE tasks ADD COLUMN location TEXT");
    }

    // ---------- lich lap ----------

    static DateOnly Nth(DateOnly b, string rrule, int k) => rrule switch
    {
        "DAILY" => b.AddDays(k),
        "WEEKLY" => b.AddDays(7 * k),
        "MONTHLY" => b.AddMonths(k),
        "YEARLY" => b.AddYears(k),
        _ => b,
    };

    /// <summary>
    /// Cac ngay lap cua task roi vao [from, to]. Nhay thang toi lan dau tien >= from
    /// thay vi lap tung buoc tu ngay goc, nen viec DAILY tao tu 5 nam truoc van dung.
    /// Moi lan lap tinh tu ngay goc (Nth) nen MONTHLY khong truot dan khi gap thang
    /// ngan: goc 31/1 cho ra 28/2 roi 31/3, chu khong phai 28/3.
    /// </summary>
    public static IEnumerable<DateOnly> Occurrences(DateOnly baseDate, string rrule, DateOnly from, DateOnly to)
    {
        if (rrule is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY"))
        {
            if (baseDate >= from && baseDate <= to) yield return baseDate;
            yield break;
        }

        int k = rrule switch
        {
            "DAILY" => from.DayNumber - baseDate.DayNumber,
            "WEEKLY" => (from.DayNumber - baseDate.DayNumber) / 7,
            "MONTHLY" => (from.Year - baseDate.Year) * 12 + from.Month - baseDate.Month - 1,
            _ => from.Year - baseDate.Year - 1,
        };
        if (k < 0) k = 0;
        while (Nth(baseDate, rrule, k) < from) k++;   // chinh bu, toi da vai buoc

        for (var d = Nth(baseDate, rrule, k); d <= to; d = Nth(baseDate, rrule, ++k))
        {
            if (d >= baseDate) yield return d;
        }
    }

    // ---------- doc ----------

    const string Cols = "id, title, due_date, due_time, end_time, tag, rrule, lead_minutes, location, note";

    /// <summary>Moi lan lap roi vao [from, to], sap xep theo ngay roi gio.</summary>
    public List<Occurrence> Range(DateOnly from, DateOnly to)
    {
        var done = LoadKeys("completions", from, to);
        var items = new List<Occurrence>();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM tasks WHERE due_date <= $to";
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var t = ReadRow(r);
            foreach (var d in Occurrences(t.Date, t.Rrule, from, to))
                items.Add(new Occurrence(t.Id, t.Title, d, t.At, t.End, t.Tag, t.Rrule,
                                         t.LeadMinutes, done.Contains((t.Id, d))));
        }

        items.Sort(Compare);
        return items;
    }

    static int Compare(Occurrence a, Occurrence b) => a.On != b.On
        ? a.On.CompareTo(b.On)
        : (a.At ?? TimeOnly.MinValue).CompareTo(b.At ?? TimeOnly.MinValue);

    public TaskRow? Get(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM tasks WHERE id = $i";
        cmd.Parameters.AddWithValue("$i", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    static TaskRow ReadRow(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        DateOnly.Parse(r.GetString(2)),
        r.IsDBNull(3) ? null : ParseTime(r.GetString(3)),
        r.IsDBNull(4) ? null : ParseTime(r.GetString(4)),
        r.GetString(5),
        r.GetString(6),
        r.IsDBNull(7) ? 0 : r.GetInt32(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9));

    /// <summary>
    /// Lan lap den luc phai ban toast, chua tick xong, chua tung ban.
    /// Cua so quet keo ve truoc de bat viec qua han, va keo ve sau MaxLeadDays de bat
    /// viec ngay mai co dat "nhac truoc 1 ngay".
    /// </summary>
    public List<Occurrence> DueNow(DateTime now, TimeOnly allDayAt, int graceHours)
    {
        var from = DateOnly.FromDateTime(now.AddHours(-(graceHours + 24)));
        var to = DateOnly.FromDateTime(now).AddDays(MaxLeadDays + 1);
        var notified = LoadKeys("notified", from, to);

        var due = new List<Occurrence>();
        foreach (var o in Range(from, to))
        {
            if (o.Done || notified.Contains((o.Id, o.On))) continue;
            if (now < o.FireAt(allDayAt)) continue;   // chua den luc bao

            // Han do tinh tu luc VIEC dien ra, khong phai tu luc dang le bao. Neu
            // tinh tu FireAt thi viec dat "nhac truoc 1 ngay" ma may tat suot cua so
            // 12h do se qua han truoc ca khi den gio, va mat hut luon — khong bao gi.
            if (now - o.When(allDayAt) > TimeSpan.FromHours(graceHours)) continue;
            due.Add(o);
        }
        due.Sort((a, b) => a.FireAt(allDayAt).CompareTo(b.FireAt(allDayAt)));
        return due;
    }

    HashSet<(long, DateOnly)> LoadKeys(string table, DateOnly from, DateOnly to)
    {
        var set = new HashSet<(long, DateOnly)>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT task_id, occurs_on FROM {table} WHERE occurs_on BETWEEN $f AND $t";
        cmd.Parameters.AddWithValue("$f", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$t", to.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add((r.GetInt64(0), DateOnly.Parse(r.GetString(1))));
        return set;
    }

    static TimeOnly? ParseTime(string s) =>
        TimeOnly.TryParse(s.Length > 5 ? s[..5] : s, out var t) ? t : null;

    static object Time(TimeOnly? t) => t?.ToString("HH\\:mm") ?? (object)DBNull.Value;
    static object Str(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();

    // ---------- ghi ----------

    public long Add(TaskRow t)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tasks (title, due_date, due_time, end_time, tag, rrule, lead_minutes, location, note)
            VALUES ($ti, $d, $t, $e, $g, $r, $l, $loc, $n) RETURNING id";
        Bind(cmd, t);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(TaskRow t)
    {
        var old = Get(t.Id);
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE tasks SET title=$ti, due_date=$d, due_time=$t, end_time=$e, tag=$g,
                             rrule=$r, lead_minutes=$l, location=$loc, note=$n
            WHERE id=$i";
        Bind(cmd, t);
        cmd.Parameters.AddWithValue("$i", t.Id);
        cmd.ExecuteNonQuery();

        // doi thoi diem hoac kieu lap thi cho phep nhac lai
        if (old is null || old.Date != t.Date || old.At != t.At
            || old.Rrule != t.Rrule || old.LeadMinutes != t.LeadMinutes)
        {
            Forget(t.Id);
        }
    }

    /// <summary>
    /// Sua task roi tick xong cho dung lan lap nguoi dung dang mo. Tra ve ngay ma
    /// dau tick thuc su roi vao.
    ///
    /// Doi ngay goc thi CA CHUOI truot theo, nen lan lap dang xem cung truot bang tung
    /// ay ngay. Neu cu tick vao ngay cu thi dau tick roi vao mot ngay khong con la lan
    /// lap nao nua: nguoi dung thay tick bay mat, con completions thi con lai mot dong
    /// mo coi khong bao gio doc toi.
    /// </summary>
    public DateOnly UpdateAndSetDone(TaskRow t, DateOnly viewing, bool done)
    {
        var old = Get(t.Id);
        Update(t);

        var moved = old is null ? viewing : viewing.AddDays(t.Date.DayNumber - old.Date.DayNumber);
        if (moved != viewing) SetDone(t.Id, viewing, false);   // don dau tick o ngay cu
        SetDone(t.Id, moved, done);
        return moved;
    }

    static void Bind(SqliteCommand cmd, TaskRow t)
    {
        cmd.Parameters.AddWithValue("$ti", t.Title.Trim());
        cmd.Parameters.AddWithValue("$d", t.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$t", Time(t.At));
        cmd.Parameters.AddWithValue("$e", Time(t.End));
        cmd.Parameters.AddWithValue("$g", t.Tag);
        cmd.Parameters.AddWithValue("$r", t.Rrule);
        cmd.Parameters.AddWithValue("$l", t.LeadMinutes);
        cmd.Parameters.AddWithValue("$loc", Str(t.Location));
        cmd.Parameters.AddWithValue("$n", Str(t.Note));
    }

    /// <summary>Tick xong / bo tick dung mot lan lap, khong dung ca chuoi.</summary>
    public void SetDone(long taskId, DateOnly on, bool done) => Key(
        done ? "INSERT OR IGNORE INTO completions (task_id, occurs_on) VALUES ($i, $o)"
             : "DELETE FROM completions WHERE task_id = $i AND occurs_on = $o", taskId, on);

    public void Delete(long taskId)
    {
        Key("DELETE FROM tasks WHERE id = $i", taskId);
        Key("DELETE FROM completions WHERE task_id = $i", taskId);
        Key("DELETE FROM notified WHERE task_id = $i", taskId);
    }

    public void MarkNotified(long taskId, DateOnly on) =>
        Key("INSERT OR IGNORE INTO notified (task_id, occurs_on) VALUES ($i, $o)", taskId, on);

    /// <summary>Quen dau da-ban, de task nhac lai sau khi doi thoi diem.</summary>
    public void Forget(long taskId) => Key("DELETE FROM notified WHERE task_id = $i", taskId);

    public int Count(string table)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    void Key(string sql, long id, DateOnly? on = null)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$i", id);
        if (on is not null) cmd.Parameters.AddWithValue("$o", on.Value.ToString("yyyy-MM-dd"));
        cmd.ExecuteNonQuery();
    }

    void Raw(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
