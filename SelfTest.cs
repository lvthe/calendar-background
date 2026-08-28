using System.Text;

namespace Deskcal;

/// <summary>
/// Kiem tra phan logic de sai nhat: lich lap, dieu kien nhac, nhac truoc, parse ngay/gio.
/// Chay bang `deskcal.exe --self-test out.txt`. App khong co console nen ghi ra file.
/// </summary>
static class SelfTest
{
    static TaskRow Row(string title, DateOnly date, TimeOnly? at, string rrule,
                       int lead = 0, TimeOnly? end = null, string tag = "work") =>
        new(0, title, date, at, end, tag, rrule, lead, null, null);

    /// <summary>Cac o nhap nhieu dong trong cay control — de do chieu cao that sau layout.</summary>
    static IEnumerable<Field> Multiline(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Field { Box.Multiline: true } f) yield return f;
            foreach (var d in Multiline(c)) yield return d;
        }
    }

    public static int Run(string outFile)
    {
        var sb = new StringBuilder();
        int fails = 0;

        void Ok(string name, bool cond, string extra = "")
        {
            if (!cond) fails++;
            sb.AppendLine($"{(cond ? "PASS" : "FAIL")}  {name}{(extra.Length > 0 ? "  ->  " + extra : "")}");
        }

        var today = new DateOnly(2026, 8, 28);
        var allDayAt = new TimeOnly(9, 0);

        // ---------- lich lap ----------

        // ban Node lap tung buoc tu ngay goc va dung o 800 vong, nen viec DAILY tao
        // hon ~2.2 nam truoc la mat hut, khong hien va khong nhac
        var daily = Store.Occurrences(today.AddDays(-5 * 365), "DAILY", today, today).ToList();
        Ok("DAILY goc cach 5 nam van ra hom nay", daily.Count == 1 && daily[0] == today, $"{daily.Count} ket qua");

        // ban Node dung setMonth: goc 31/1 + 1 thang = 03/03, roi truot dan mai
        var m = Store.Occurrences(new DateOnly(2026, 1, 31), "MONTHLY",
                                  new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)).ToList();
        var wantM = new[]
        {
            new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30),
        };
        Ok("MONTHLY goc 31/1 khong truot dan", m.SequenceEqual(wantM), string.Join(" ", m));

        var wBase = new DateOnly(2026, 8, 3);
        var w = Store.Occurrences(wBase, "WEEKLY", new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 3)).ToList();
        Ok("WEEKLY giu dung thu, khong lech",
            w.Count == 2 && w.All(d => d.DayOfWeek == wBase.DayOfWeek), string.Join(" ", w));

        Ok("NONE ngoai khoang thi khong ra",
            !Store.Occurrences(new DateOnly(2026, 1, 1), "NONE", today, today).Any());

        Ok("khong ra ngay truoc ngay goc",
            !Store.Occurrences(today, "DAILY", today.AddDays(-10), today).Any(d => d < today));

        // ---------- parse ngay / gio ----------

        void Date(string input, DateOnly? want)
        {
            bool ok = Parse.TryDate(input, today, out var got);
            Ok($"ngay \"{input}\"",
                want is null ? !ok : ok && got == want.Value,
                ok ? got.ToString("yyyy-MM-dd") : "khong hieu");
        }

        Date("Hôm nay", today);
        Date("mai", today.AddDays(1));
        Date("Tuần sau", today.AddDays(7));
        Date("+3", today.AddDays(3));
        Date("15/09", new DateOnly(2026, 9, 15));
        Date("01/01", new DateOnly(2027, 1, 1));          // da qua trong nam nay -> hieu la sang nam
        Date("2026-12-25", new DateOnly(2026, 12, 25));
        Date("bua nao do", null);

        void Time(string input, TimeOnly? want, bool valid = true)
        {
            bool ok = Parse.TryTime(input, out var got);
            Ok($"gio \"{input}\"", ok == valid && got == want,
                ok ? got?.ToString("HH\\:mm") ?? "ca ngay" : "khong hieu");
        }

        Time("", null);
        Time(Parse.AllDay, null);
        Time("14:30", new TimeOnly(14, 30));
        Time("14h30", new TimeOnly(14, 30));
        Time("9", new TimeOnly(9, 0));
        Time("9h", new TimeOnly(9, 0));
        Time("1430", new TimeOnly(14, 30));
        Time("25:00", null, valid: false);
        Time("14:70", null, valid: false);
        Time("abc", null, valid: false);

        // ---------- DB ----------

        var dir = Path.Combine(Path.GetTempPath(), "deskcal-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var s = new Store(Path.Combine(dir, "t.db")))
            {
                // day la bug #3 cua ban Node: tick 1 lan lap lam ban ngay khac
                long rep = s.Add(Row("Bao cao ngay", today.AddDays(-3), new TimeOnly(14, 30), "DAILY"));
                s.SetDone(rep, today, true);
                var range = s.Range(today.AddDays(-3), today);
                Ok("tick 1 lan lap khong dung sang lan khac",
                    range.Count(o => o.Done) == 1 && range.First(o => o.Done).On == today,
                    $"{range.Count} lan lap, {range.Count(o => o.Done)} da xong");

                s.SetDone(rep, today, false);
                Ok("bo tick duoc", s.Range(today, today).All(o => !o.Done));

                var now = today.ToDateTime(new TimeOnly(15, 0));
                Ok("den gio thi vao danh sach nhac",
                    s.DueNow(now, allDayAt, 12).Any(o => o.Id == rep && o.On == today));

                s.MarkNotified(rep, today);
                Ok("da bao roi thi khong bao lai",
                    !s.DueNow(now, allDayAt, 12).Any(o => o.Id == rep && o.On == today));

                Ok("qua han hon grace thi im",
                    !s.DueNow(today.AddDays(1).ToDateTime(new TimeOnly(20, 0)), allDayAt, 12)
                       .Any(o => o.Id == rep && o.On == today));

                long allday = s.Add(Row("Viec ca ngay", today, null, "NONE", tag: "personal"));
                Ok("ca ngay: truoc 09:00 chua bao",
                    !s.DueNow(today.ToDateTime(new TimeOnly(8, 30)), allDayAt, 12).Any(o => o.Id == allday));
                Ok("ca ngay: sau 09:00 thi bao",
                    s.DueNow(today.ToDateTime(new TimeOnly(9, 30)), allDayAt, 12).Any(o => o.Id == allday));

                s.SetDone(allday, today, true);
                Ok("tick xong thi khong nhac",
                    !s.DueNow(today.ToDateTime(new TimeOnly(9, 30)), allDayAt, 12).Any(o => o.Id == allday));

                // ---------- nhac truoc ----------

                long lead30 = s.Add(Row("Hop", today, new TimeOnly(15, 0), "NONE", lead: 30));
                Ok("nhac truoc 30p: 14:20 chua bao",
                    !s.DueNow(today.ToDateTime(new TimeOnly(14, 20)), allDayAt, 12).Any(o => o.Id == lead30));
                Ok("nhac truoc 30p: 14:35 da bao",
                    s.DueNow(today.ToDateTime(new TimeOnly(14, 35)), allDayAt, 12).Any(o => o.Id == lead30));

                long lead1d = s.Add(Row("Deadline", today.AddDays(1), new TimeOnly(10, 0), "NONE",
                                        lead: 1440, tag: "deadline"));
                Ok("nhac truoc 1 ngay: hom nay 10:05 da bao viec ngay mai",
                    s.DueNow(today.ToDateTime(new TimeOnly(10, 5)), allDayAt, 12).Any(o => o.Id == lead1d));
                Ok("nhac truoc 1 ngay: hom nay 09:00 chua bao",
                    !s.DueNow(today.ToDateTime(new TimeOnly(9, 0)), allDayAt, 12).Any(o => o.Id == lead1d));

                // May tat suot cua so nhac-truoc: tinh han tu FireAt thi viec nay qua
                // han (24.5h) truoc ca khi den gio, va mat hut luon. Han phai tinh tu
                // luc viec dien ra.
                long missed = s.Add(Row("Bay som", today.AddDays(1), new TimeOnly(6, 0), "NONE", lead: 1440));
                Ok("lead 1 ngay bi lo van bao luc den gio",
                    s.DueNow(today.AddDays(1).ToDateTime(new TimeOnly(6, 30)), allDayAt, 12)
                       .Any(o => o.Id == missed));
                Ok("lead 1 ngay: qua gio viec hon grace thi moi im",
                    !s.DueNow(today.AddDays(1).ToDateTime(new TimeOnly(23, 0)), allDayAt, 12)
                       .Any(o => o.Id == missed));

                // ---------- gio ket thuc + sua ----------

                long ranged = s.Add(Row("Hop dai", today, new TimeOnly(9, 0), "NONE", end: new TimeOnly(10, 30)));
                var got = s.Range(today, today).First(o => o.Id == ranged);
                Ok("gio bat dau va ket thuc luu doc dung",
                    got.At == new TimeOnly(9, 0) && got.End == new TimeOnly(10, 30), got.RangeLabel);

                s.MarkNotified(ranged, today);
                var row = s.Get(ranged)!;
                s.Update(row with { At = new TimeOnly(16, 0) });
                Ok("sua gio thi xoa dau da-bao de nhac lai",
                    s.DueNow(today.ToDateTime(new TimeOnly(16, 5)), allDayAt, 12).Any(o => o.Id == ranged));

                s.Update(s.Get(ranged)! with { Title = "Doi ten", Location = "Phong 3", Note = "ghi chu" });
                var after = s.Get(ranged)!;
                Ok("sua tieu de / noi / ghi chu",
                    after.Title == "Doi ten" && after.Location == "Phong 3" && after.Note == "ghi chu");

                // ---------- doi ngay roi tick xong ----------

                long shifted = s.Add(Row("Doi lich", today, new TimeOnly(8, 0), "NONE"));
                var landed = s.UpdateAndSetDone(s.Get(shifted)! with { Date = today.AddDays(3) }, today, true);
                Ok("doi ngay roi tick xong thi dau tick di theo sang ngay moi",
                    landed == today.AddDays(3)
                    && s.Range(today.AddDays(3), today.AddDays(3)).Any(o => o.Id == shifted && o.Done)
                    && !s.Range(today, today).Any(o => o.Id == shifted),
                    landed.ToString("yyyy-MM-dd"));

                long kept = s.Add(Row("Giu nguyen ngay", today, new TimeOnly(11, 0), "NONE"));
                var same = s.UpdateAndSetDone(s.Get(kept)! with { Title = "Chi doi ten" }, today, true);
                Ok("khong doi ngay thi tick van o dung lan lap dang xem",
                    same == today && s.Range(today, today).Any(o => o.Id == kept && o.Done),
                    same.ToString("yyyy-MM-dd"));

                long series = s.Add(Row("Chuoi tuan", today, new TimeOnly(8, 0), "WEEKLY"));
                var slid = s.UpdateAndSetDone(s.Get(series)! with { Date = today.AddDays(1) },
                                              today.AddDays(7), true);
                Ok("doi goc chuoi lap: lan dang xem truot dung so ngay",
                    slid == today.AddDays(8)
                    && s.Range(slid, slid).Any(o => o.Id == series && o.Done),
                    slid.ToString("yyyy-MM-dd"));

                // ---------- xoa ----------

                int before = s.Count("tasks");
                s.Delete(rep);
                Ok("xoa task thi don ca completions va notified",
                    s.Count("tasks") == before - 1
                    && !s.Range(today.AddDays(-3), today).Any(o => o.Id == rep),
                    $"tasks {before} -> {s.Count("tasks")}");

                string nasty = "Gọi \"khách\" & <b>$x</b>";
                long odd = s.Add(Row(nasty, today, new TimeOnly(10, 0), "NONE", tag: "ops"));
                Ok("tieu de ky tu la luu doc nguyen ven",
                    s.Range(today, today).Any(o => o.Id == odd && o.Title == nasty));
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* WAL con giu file thi bo qua */ }
        }

        // ---------- form ----------
        // layout viet tay, de nem exception nhat; kiem tra ctor ca hai che do
        try
        {
            using var add = new EventForm(null, today, false);
            using var edit = new EventForm(Row("X", today, new TimeOnly(8, 0), "DAILY", lead: 10) with { Id = 7 },
                                           today, true);
            Ok("EventForm dung duoc ca che do them va sua",
                add.Result is null && !add.DeleteRequested && edit.DoneChecked);

            // Chieu cao o ghi chu phai do layout that quyet dinh, nen phai ep tao
            // handle roi PerformLayout — khong thi doc ra so trong ctor, vo nghia.
            add.CreateControl();
            add.PerformLayout();
            var note = Multiline(add).FirstOrDefault();

            // Field.GetPreferredSize chi tra chieu cao MOT dong, nhung Field khong bat
            // AutoSize nen TableLayoutPanel lay dung Height da dat — o ghi chu cao that.
            // Ai do bat AutoSize len sau nay la no co ve 1 dong ngay, nen chot lai bang
            // test thay vi tin vao doc code.
            Ok("o ghi chu cao nhieu dong, khong bi layout bop ve 1 dong",
                note is not null && note.Height >= note.Box.Font.Height * 3,
                note is null ? "khong thay o multiline"
                             : $"cao {note.Height}px, 1 dong = {note.Box.Font.Height}px");

            Ok("o ghi chu nhan Enter de xuong dong, khong bam nut Luu",
                note is not null && note.Box.AcceptsReturn);
        }
        catch (Exception e) { Ok("EventForm dung duoc ca che do them va sua", false, e.Message); }

        sb.AppendLine();
        sb.AppendLine(fails == 0 ? "Tat ca PASS" : $"{fails} test FAIL");
        File.WriteAllText(outFile, sb.ToString());
        return fails == 0 ? 0 : 1;
    }
}
