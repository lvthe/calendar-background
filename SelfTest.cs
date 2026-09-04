using System.Runtime.InteropServices;
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

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

                // ---------- ngoai le cua chuoi lap ----------
                // Google dung EXDATE va su kien ngoai le lien tuc ("tuan nay standup
                // nghi", "buoi thu Ba doi sang thu Tu"), nen phan nay phai chac truoc
                // khi dong bo bat cu thu gi.

                var mon = new DateOnly(2026, 9, 7);          // thu Hai
                long standup = s.Add(Row("Standup", mon, new TimeOnly(9, 0), "WEEKLY"));
                var w4 = () => s.Range(mon, mon.AddDays(27));

                Ok("chuoi tuan ban dau du 4 lan", w4().Count(o => o.Id == standup) == 4,
                    string.Join(" ", w4().Where(o => o.Id == standup).Select(o => o.On.ToString("dd/MM"))));

                // bo han mot lan
                s.Skip(standup, mon.AddDays(7));
                Ok("bo mot lan lap thi chi mat dung lan do",
                    w4().Count(o => o.Id == standup) == 3
                    && !w4().Any(o => o.Id == standup && o.On == mon.AddDays(7)),
                    string.Join(" ", w4().Where(o => o.Id == standup).Select(o => o.On.ToString("dd/MM"))));

                s.ClearOverride(standup, mon.AddDays(7));
                Ok("bo ngoai le thi lan lap quay lai", w4().Count(o => o.Id == standup) == 4);

                // doi rieng mot lan sang ngay khac, ca chuoi giu nguyen
                var third = mon.AddDays(14);
                s.Move(standup, third, third.AddDays(2), new TimeOnly(15, 0), null);
                var moved = w4().FirstOrDefault(o => o.Id == standup && o.On == third.AddDays(2));
                Ok("doi rieng mot lan: hien o ngay moi voi gio moi",
                    moved is not null && moved.At == new TimeOnly(15, 0),
                    moved is null ? "khong thay" : $"{moved.On:dd/MM} {moved.TimeLabel}");
                Ok("doi rieng mot lan: KHONG keo ca chuoi theo",
                    w4().Count(o => o.Id == standup) == 4
                    && w4().Any(o => o.Id == standup && o.On == mon.AddDays(21) && o.At == new TimeOnly(9, 0)));

                // Key phai giu ngay GOC, khong theo ngay moi — day la cai giu cho dau
                // tick va dau da-bao khong bi lac khi doi ngay
                Ok("lan lap bi doi van dinh danh theo ngay goc",
                    moved is not null && moved.Key == third, moved?.Key.ToString("dd/MM") ?? "-");

                s.SetDone(standup, moved!.Key, true);
                Ok("tick lan lap bi doi thi dung no, khong lan sang lan khac",
                    w4().Count(o => o.Id == standup && o.Done) == 1
                    && w4().First(o => o.Id == standup && o.Done).On == third.AddDays(2));
                s.SetDone(standup, moved.Key, false);

                // doi ra ngoai cua so roi doi vao trong
                var win = () => s.Range(third, third.AddDays(1));
                Ok("doi ra ngoai cua so thi khong con trong cua so do",
                    !win().Any(o => o.Id == standup), $"{win().Count(o => o.Id == standup)} lan");

                s.Move(standup, mon.AddDays(21), third, null, null);
                Ok("doi tu ngoai VAO trong cua so thi phai hien ra",
                    win().Any(o => o.Id == standup && o.On == third && o.Key == mon.AddDays(21)),
                    string.Join(" ", win().Where(o => o.Id == standup).Select(o => $"{o.On:dd/MM}(key {o.Key:dd/MM})")));

                // nhac viec phai theo ngoai le
                s.Skip(standup, mon.AddDays(7));
                Ok("lan lap da bo thi khong nhac",
                    !s.DueNow(mon.AddDays(7).ToDateTime(new TimeOnly(9, 30)), allDayAt, 12)
                       .Any(o => o.Id == standup));
                Ok("lan lap doi gio thi nhac theo gio moi",
                    s.DueNow(third.AddDays(2).ToDateTime(new TimeOnly(15, 10)), allDayAt, 12)
                       .Any(o => o.Id == standup)
                    && !s.DueNow(third.AddDays(2).ToDateTime(new TimeOnly(9, 30)), allDayAt, 12)
                       .Any(o => o.Id == standup));

                // doi ngay goc cua chuoi thi moi override cu tro vao ngay khong con ton tai
                s.Update(s.Get(standup)! with { Date = mon.AddDays(1) });
                Ok("doi ngay goc chuoi thi don sach override cu",
                    s.GetOverride(standup, mon.AddDays(7)) is null
                    && s.GetOverride(standup, third) is null);

                s.Skip(standup, mon.AddDays(8));
                s.Delete(standup);
                Ok("xoa task thi don ca override", s.GetOverride(standup, mon.AddDays(8)) is null);

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

        // ---------- OAuth Google ----------

        // Test vector cua RFC 7636 phu luc B. Sai PKCE thi Google tu choi voi thong bao
        // chung chung, rat kho lan ra — nen chot bang vector chuan.
        const string rfcVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string rfcChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        Ok("PKCE challenge khop test vector RFC 7636",
            GoogleAuth.Challenge(rfcVerifier) == rfcChallenge, GoogleAuth.Challenge(rfcVerifier));

        var verifier = GoogleAuth.NewVerifier();
        Ok("verifier dai trong khoang RFC cho phep", verifier.Length is >= 43 and <= 128, $"{verifier.Length} ky tu");
        Ok("base64url khong con padding hay ky tu can escape",
            !verifier.Contains('=') && !verifier.Contains('+') && !verifier.Contains('/'), verifier);

        var au = GoogleAuth.AuthUrl("cid.apps.googleusercontent.com", "http://127.0.0.1:5000/",
                                    GoogleAuth.Challenge(verifier), "st4te");
        // Thieu access_type=offline hoac prompt=consent thi Google khong tra refresh_token,
        // va app chet sau dung mot tieng ma khong hieu tai sao.
        Ok("auth url xin refresh token", au.Contains("access_type=offline") && au.Contains("prompt=consent"));
        Ok("auth url dung PKCE S256", au.Contains("code_challenge_method=S256") && au.Contains("code_challenge="));
        Ok("auth url co state chong CSRF", au.Contains("state=st4te"));
        Ok("auth url xin dung hai scope can thiet",
            au.Contains(Uri.EscapeDataString("calendar.events")) && au.Contains(Uri.EscapeDataString("calendar.readonly")));
        Ok("auth url khong xin scope calendar day du",
            !au.Contains(Uri.EscapeDataString("auth/calendar ")) && !au.EndsWith("auth/calendar"));

        // Nhan file Google cho tai ve nguyen xi — bat nguoi dung sua tay la co cho sai.
        var native = GoogleConfig.Parse(
            """{"installed":{"client_id":"abc.apps.googleusercontent.com","client_secret":"GOCSPX-x","redirect_uris":["http://localhost"]}}""");
        Ok("doc duoc file Google cho tai ve nguyen xi",
            native is { ClientId: "abc.apps.googleusercontent.com", ClientSecret: "GOCSPX-x" });

        Ok("doc duoc ca dang phang go tay",
            GoogleConfig.Parse("""{"clientId":"a","clientSecret":"b"}""") is { ClientId: "a", ClientSecret: "b" });

        // Tao nham client loai Web thi luong loopback se bi Google tu choi. Bat o day
        // chu de chet luc dang nhap thi rat kho doan la do sai loai client.
        Ok("tu choi client loai Web va noi ro",
            GoogleConfig.Parse("""{"web":{"client_id":"a","client_secret":"b"}}""") is null);

        Ok("thieu truong thi tra null chu khong nem",
            GoogleConfig.Parse("""{"installed":{"client_id":"a"}}""") is null);

        Ok("token sap het han thi coi la can lam moi",
            new GoogleToken { ExpiresAt = DateTime.UtcNow.AddMinutes(1) }.Stale
            && !new GoogleToken { ExpiresAt = DateTime.UtcNow.AddMinutes(30) }.Stale);

        // ---------- bo cuc lop lich tren desktop ----------

        // Panel phai nam gon trong man va lech ve ben PHAI (icon desktop o ben trai),
        // o moi do phan giai — day la cai lam cho "tu bam theo man hinh" dung.
        foreach (var scr in new[] { new Size(3840, 2160), new Size(2560, 1440), new Size(1920, 1080) })
        {
            var p = DesktopPanel.PanelRect(scr);
            Ok($"panel nam gon trong man {scr.Width}x{scr.Height}",
                p.Left >= scr.Width * 0.3 && p.Right <= scr.Width && p.Top >= 0 && p.Bottom <= scr.Height,
                $"{p}, chua {p.Left * 100 / scr.Width}% be ngang ben trai");
        }

        // Cung mot bo cuc o moi man: ti le khung phai nhu nhau, chi khac do net.
        var r4k = DesktopPanel.PanelRect(new Size(3840, 2160));
        var r1440 = DesktopPanel.PanelRect(new Size(2560, 1440));
        Ok("bo cuc panel giong nhau o 4K va 1440p",
            Math.Abs((float)r4k.Width / r4k.Height - (float)r1440.Width / r1440.Height) < 0.01f,
            $"{(float)r4k.Width / r4k.Height:0.000} vs {(float)r1440.Width / r1440.Height:0.000}");

        // Ve ra bitmap phai ra hinh that. Bug kieu "quen doi g.Clear" hay "font khong
        // nhan theo scale" deu cho ra anh mot mau — dem so mau rieng biet la bat duoc.
        var rdir = Path.Combine(Path.GetTempPath(), "deskcal-render-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var size = new Size(900, 620);
            using var bmp = new Bitmap(size.Width, size.Height);
            using (var rs = new Store(Path.Combine(rdir, "r.db")))
            {
                rs.Add(Row("Hop team", today, new TimeOnly(9, 0), "NONE"));
                rs.Add(Row("Deadline", today.AddDays(2), new TimeOnly(17, 0), "NONE", tag: "deadline"));
                using var g = Graphics.FromImage(bmp);
                using var view = new CalendarView(rs) { Month = today };
                view.Render(g, size, 2f);
            }

            var seen = new HashSet<int>();
            for (int y = 0; y < size.Height; y += 7)
                for (int x = 0; x < size.Width; x += 7)
                    seen.Add(bmp.GetPixel(x, y).ToArgb());

            Ok("ve lich ra bitmap ra hinh that, khong phai mot mau", seen.Count > 5, $"{seen.Count} mau");
        }
        catch (Exception e) { Ok("ve lich ra bitmap ra hinh that, khong phai mot mau", false, e.Message); }
        finally { try { Directory.Delete(rdir, true); } catch { /* WAL con giu file thi bo qua */ } }

        // ---------- lop lich mo tren desktop ----------

        var pdir = Path.Combine(Path.GetTempPath(), "deskcal-panel-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var ps = new Store(Path.Combine(pdir, "p.db"));
            using var panel = new DesktopPanel(ps);
            panel.CreateControl();

            const int GWL_EXSTYLE = -20;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            long ex = GetWindowLongPtr(panel.Handle, GWL_EXSTYLE).ToInt64();

            // NOACTIVATE: bam vao khong cuop focus cua app dang go.
            // TOOLWINDOW: khong chiem mot o trong Alt+Tab.
            Ok("lop mo co WS_EX_NOACTIVATE", (ex & WS_EX_NOACTIVATE) != 0, $"exstyle=0x{ex:X}");
            Ok("lop mo co WS_EX_TOOLWINDOW", (ex & WS_EX_TOOLWINDOW) != 0, $"exstyle=0x{ex:X}");

            Ok("lop mo la trong suot", panel.Opacity is > 0.0 and < 1.0, panel.Opacity.ToString("0.00"));
            Ok("do mo cua so khop voi muc da luu",
                Math.Abs(panel.Opacity - DesktopPanel.OpacityPercent / 100.0) < 0.001,
                $"{panel.Opacity:0.00} vs {DesktopPanel.OpacityPercent}%");
            Ok("moi muc do mo deu la gia tri dung duoc",
                DesktopPanel.OpacityLevels.Length > 0 && DesktopPanel.OpacityLevels.All(v => v is >= 5 and <= 100),
                string.Join(" ", DesktopPanel.OpacityLevels));
            Ok("lop mo khong hien tren taskbar", !panel.ShowInTaskbar);
            Ok("lop mo bo goc bang Region", panel.Region is not null);

            var scr = Screen.PrimaryScreen?.Bounds.Size ?? new Size(1920, 1080);
            Ok("lop mo nam dung o vi tri tinh ra",
                panel.Bounds == DesktopPanel.PanelRect(scr), $"{panel.Bounds} vs {DesktopPanel.PanelRect(scr)}");
        }
        catch (Exception e) { Ok("dung duoc lop lich mo tren desktop", false, e.Message); }
        finally { try { Directory.Delete(pdir, true); } catch { /* WAL con giu file thi bo qua */ } }

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
