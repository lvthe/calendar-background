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
    string? Note,
    string? GoogleId = null,
    string? GoogleUpdated = null);

/// <summary>
/// Mot lan xuat hien cu the cua task (task lap se co nhieu Occurrence).
///
/// <para><b>On khac Key.</b> On la ngay lan lap nay THUC SU dien ra — dung de ve len
/// lich. Key la ngay lich lap SINH RA no, va la thu dinh danh lan lap do trong
/// completions / notified / overrides. Hai cai chi khac nhau khi lan lap bi doi ngay
/// rieng ("standup thu Ba tuan nay doi sang thu Tu"): doi xong thi On = thu Tu con Key
/// van la thu Ba. Khoa theo Key nen dau tick va dau da-bao khong bi lac khi doi ngay,
/// va doi ve cho cu thi van con nguyen.</para>
/// </summary>
public sealed record Occurrence(
    long Id,
    string Title,
    DateOnly On,
    TimeOnly? At,
    TimeOnly? End,
    string Tag,
    string Rrule,
    int LeadMinutes,
    bool Done,
    DateOnly Key)
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

            -- Ngoai le cua chuoi lap: bo han mot lan, hoac doi rieng lan do sang ngay/gio
            -- khac. occurs_on luon la ngay GOC do lich lap sinh ra — do la khoa dinh danh
            -- lan lap, khong doi theo new_date.
            CREATE TABLE IF NOT EXISTS overrides (
              task_id   INTEGER NOT NULL,
              occurs_on TEXT    NOT NULL,
              skipped   INTEGER NOT NULL DEFAULT 0,
              new_date  TEXT,                    -- null = giu ngay goc
              new_time  TEXT,
              new_end   TEXT,
              PRIMARY KEY (task_id, occurs_on)
            );
            CREATE INDEX IF NOT EXISTS idx_ovr_new ON overrides(new_date);
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

        // Lien ket voi Google. google_id la id su kien ben Google; google_updated la moc
        // thoi gian Google bao lan sua cuoi, dung de biet ben nao moi hon khi ca hai cung
        // doi. NULL ca hai = viec tao trong deskcal, chua len Google.
        if (!cols.Contains("google_id")) Raw("ALTER TABLE tasks ADD COLUMN google_id TEXT");
        if (!cols.Contains("google_updated")) Raw("ALTER TABLE tasks ADD COLUMN google_updated TEXT");
        Raw("CREATE UNIQUE INDEX IF NOT EXISTS idx_tasks_gid ON tasks(google_id) WHERE google_id IS NOT NULL");
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

    const string Cols = "id, title, due_date, due_time, end_time, tag, rrule, lead_minutes, location, note, google_id, google_updated";

    /// <summary>Ngoai le cua mot lan lap: bo han, hoac doi sang ngay/gio khac.</summary>
    public sealed record Override(bool Skipped, DateOnly? Date, TimeOnly? At, TimeOnly? End);

    /// <summary>
    /// Moi lan lap roi vao [from, to], sap xep theo ngay roi gio.
    ///
    /// Ngoai le lam chuyen nay kho hon ve: mot lan lap co the bi doi RA khoi cua so
    /// (sinh trong [from,to] nhung new_date nam ngoai), hoac doi VAO trong (sinh o ngoai
    /// nhung new_date roi vao trong). Chi quet lich lap trong [from,to] la sot ve thu
    /// hai, nen phai hoi rieng cac override co new_date roi vao cua so.
    /// </summary>
    public List<Occurrence> Range(DateOnly from, DateOnly to)
    {
        var done = LoadKeys("completions", from, to);
        var ovr = LoadOverrides(from, to);
        var items = new List<Occurrence>();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM tasks WHERE due_date <= $to";
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var t = ReadRow(r);

            Occurrence? Make(DateOnly key)
            {
                ovr.TryGetValue((t.Id, key), out var o);
                if (o is { Skipped: true }) return null;

                var on = o?.Date ?? key;
                if (on < from || on > to) return null;   // bi doi ra ngoai cua so
                return new Occurrence(t.Id, t.Title, on, o?.At ?? t.At, o?.End ?? t.End,
                                      t.Tag, t.Rrule, t.LeadMinutes,
                                      done.Contains((t.Id, key)), key);
            }

            foreach (var d in Occurrences(t.Date, t.Rrule, from, to))
                if (Make(d) is { } occ) items.Add(occ);

            // lan lap sinh ra NGOAI cua so nhung bi doi vao trong
            foreach (var ((id, key), o) in ovr)
            {
                if (id != t.Id || o.Skipped || o.Date is null) continue;
                if (key >= from && key <= to) continue;             // da xu ly o vong tren
                if (o.Date < from || o.Date > to) continue;
                // chi nhan neu ngay goc that su la mot lan lap cua chuoi
                if (!Occurrences(t.Date, t.Rrule, key, key).Any()) continue;
                if (Make(key) is { } occ) items.Add(occ);
            }
        }

        items.Sort(Compare);
        return items;
    }

    Dictionary<(long, DateOnly), Override> LoadOverrides(DateOnly from, DateOnly to)
    {
        var map = new Dictionary<(long, DateOnly), Override>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT task_id, occurs_on, skipped, new_date, new_time, new_end FROM overrides
            WHERE (occurs_on BETWEEN $f AND $t)
               OR (new_date IS NOT NULL AND new_date BETWEEN $f AND $t)";
        cmd.Parameters.AddWithValue("$f", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$t", to.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            map[(r.GetInt64(0), DateOnly.Parse(r.GetString(1)))] = new Override(
                r.GetInt32(2) != 0,
                r.IsDBNull(3) ? null : DateOnly.Parse(r.GetString(3)),
                r.IsDBNull(4) ? null : ParseTime(r.GetString(4)),
                r.IsDBNull(5) ? null : ParseTime(r.GetString(5)));
        }
        return map;
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
        r.IsDBNull(9) ? null : r.GetString(9),
        r.IsDBNull(10) ? null : r.GetString(10),
        r.IsDBNull(11) ? null : r.GetString(11));

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
            if (o.Done || notified.Contains((o.Id, o.Key))) continue;
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
            INSERT INTO tasks (title, due_date, due_time, end_time, tag, rrule, lead_minutes,
                               location, note, google_id, google_updated)
            VALUES ($ti, $d, $t, $e, $g, $r, $l, $loc, $n, $gid, $gup) RETURNING id";
        Bind(cmd, t);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(TaskRow t)
    {
        var old = Get(t.Id);
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE tasks SET title=$ti, due_date=$d, due_time=$t, end_time=$e, tag=$g,
                             rrule=$r, lead_minutes=$l, location=$loc, note=$n,
                             google_id=$gid, google_updated=$gup
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

        // Doi ngay goc hoac kieu lap thi lich lap sinh ra bo ngay khac han, nen moi
        // override cu deu tro vao nhung ngay khong con la lan lap nao — thanh rac khong
        // bao gio doc toi. Don luon.
        if (old is not null && (old.Date != t.Date || old.Rrule != t.Rrule))
            Key("DELETE FROM overrides WHERE task_id = $i", t.Id);
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
        cmd.Parameters.AddWithValue("$gid", Str(t.GoogleId));
        cmd.Parameters.AddWithValue("$gup", Str(t.GoogleUpdated));
    }

    /// <summary>Tim viec da lien ket voi mot su kien Google.</summary>
    public TaskRow? ByGoogleId(string googleId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM tasks WHERE google_id = $g";
        cmd.Parameters.AddWithValue("$g", googleId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    /// <summary>Moi google_id dang co trong DB — de doi chieu xem ben Google da xoa cai nao.</summary>
    public Dictionary<string, long> GoogleIds()
    {
        var map = new Dictionary<string, long>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT google_id, id FROM tasks WHERE google_id IS NOT NULL";
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetInt64(1);
        return map;
    }

    /// <summary>Tick xong / bo tick dung mot lan lap, khong dung ca chuoi.</summary>
    public void SetDone(long taskId, DateOnly on, bool done) => Key(
        done ? "INSERT OR IGNORE INTO completions (task_id, occurs_on) VALUES ($i, $o)"
             : "DELETE FROM completions WHERE task_id = $i AND occurs_on = $o", taskId, on);

    // ---------- ngoai le cua chuoi lap ----------

    /// <summary>
    /// Bo han mot lan lap ("tuan nay standup nghi"). occursOn la ngay GOC do lich lap
    /// sinh ra, khong phai ngay da doi.
    /// </summary>
    public void Skip(long taskId, DateOnly occursOn) => Upsert(taskId, occursOn, true, null, null, null);

    /// <summary>
    /// Doi rieng mot lan lap sang ngay / gio khac, ca chuoi giu nguyen. Truyen null cho
    /// date la giu ngay goc va chi doi gio.
    /// </summary>
    public void Move(long taskId, DateOnly occursOn, DateOnly? date, TimeOnly? at, TimeOnly? end) =>
        Upsert(taskId, occursOn, false, date, at, end);

    /// <summary>Bo ngoai le, tra lan lap ve dung lich chung.</summary>
    public void ClearOverride(long taskId, DateOnly occursOn) =>
        Key("DELETE FROM overrides WHERE task_id = $i AND occurs_on = $o", taskId, occursOn);

    public Override? GetOverride(long taskId, DateOnly occursOn)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"SELECT skipped, new_date, new_time, new_end FROM overrides
                            WHERE task_id = $i AND occurs_on = $o";
        cmd.Parameters.AddWithValue("$i", taskId);
        cmd.Parameters.AddWithValue("$o", occursOn.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Override(
            r.GetInt32(0) != 0,
            r.IsDBNull(1) ? null : DateOnly.Parse(r.GetString(1)),
            r.IsDBNull(2) ? null : ParseTime(r.GetString(2)),
            r.IsDBNull(3) ? null : ParseTime(r.GetString(3)));
    }

    void Upsert(long taskId, DateOnly occursOn, bool skipped, DateOnly? date, TimeOnly? at, TimeOnly? end)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO overrides (task_id, occurs_on, skipped, new_date, new_time, new_end)
            VALUES ($i, $o, $s, $d, $t, $e)
            ON CONFLICT(task_id, occurs_on) DO UPDATE SET
                skipped = $s, new_date = $d, new_time = $t, new_end = $e";
        cmd.Parameters.AddWithValue("$i", taskId);
        cmd.Parameters.AddWithValue("$o", occursOn.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$s", skipped ? 1 : 0);
        cmd.Parameters.AddWithValue("$d", date?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$t", Time(at));
        cmd.Parameters.AddWithValue("$e", Time(end));
        cmd.ExecuteNonQuery();
    }

    public void Delete(long taskId)
    {
        Key("DELETE FROM tasks WHERE id = $i", taskId);
        Key("DELETE FROM completions WHERE task_id = $i", taskId);
        Key("DELETE FROM notified WHERE task_id = $i", taskId);
        Key("DELETE FROM overrides WHERE task_id = $i", taskId);
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
