using System.Text.Json;

namespace Deskcal;

/// <summary>
/// Keo su kien tu Google ve deskcal.
///
/// <para><b>Chi xoa khi Google noi ro la da huy.</b> Cach khac la doi chieu tap hop:
/// cai nao co trong DB ma khong thay trong lan keo nay thi xoa. Nghe hop ly nhung rat
/// nguy hiem — mot lan goi API loi giua chung, hoac cua so thoi gian tinh lech, la xoa
/// nham hang loat viec that. Doi lay: su kien bi xoa hoi app chua chay se con lai. Do la
/// cai gia dung de tra.</para>
///
/// <para><b>Khong dung singleEvents=true.</b> No gian moi chuoi lap thanh tung lan rieng,
/// nen mot cuoc hop tuan thanh 52 dong trong DB va mat han khai niem "chuoi". Lay su kien
/// goc kem RRULE roi tu dich, ngoai le thi ghi vao bang overrides.</para>
/// </summary>
public static class GooglePull
{
    /// <summary>Keo bao xa. Lui 1 thang de con thay viec vua qua, toi 1 nam.</summary>
    const int DaysBack = 31, DaysAhead = 365;

    public sealed record Result(int Added, int Updated, int Deleted, int Skipped,
                                int Exceptions, List<string> Unsupported)
    {
        public override string ToString() =>
            $"them {Added}, sua {Updated}, xoa {Deleted}, ngoai le {Exceptions}, " +
            $"bo qua {Skipped}" + (Unsupported.Count > 0 ? $", KHONG NHAP DUOC {Unsupported.Count}" : "");
    }

    public static async Task<Result> RunAsync(Store store, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from = today.AddDays(-DaysBack);
        var to = today.AddDays(DaysAhead);

        var events = await GoogleApi.EventsAsync(from, to, ct);
        Log.Write($"google: lay ve {events.Count} su kien tu {from:yyyy-MM-dd} den {to:yyyy-MM-dd}");

        int added = 0, updated = 0, deleted = 0, skipped = 0, exceptions = 0;
        var unsupported = new List<string>();

        // Vong 1: su kien goc. Phai xong truoc vong 2 vi ngoai le can task cha da ton tai.
        foreach (var ev in events)
        {
            if (GoogleMap.ParentId(ev) is not null) continue;

            string id = ev.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            if (id.Length == 0) { skipped++; continue; }

            var existing = store.ByGoogleId(id);

            if (GoogleMap.IsCancelled(ev))
            {
                if (existing is not null) { store.Delete(existing.Id); deleted++; }
                continue;
            }

            var m = GoogleMap.FromEvent(ev);
            if (m.NeedsExpanding)
            {
                unsupported.Add(m.Row?.Title ?? Title(ev));
                continue;
            }
            if (m.Row is null) { skipped++; continue; }

            if (existing is null) { store.Add(m.Row); added++; }
            else if (existing.GoogleUpdated != m.Row.GoogleUpdated)
            {
                // Giu lai nhan va nhac-truoc nguoi dung da dat trong deskcal — Google
                // khong co hai thu do, ghi de la mat cong nguoi dung chinh lai moi lan.
                store.Update(m.Row with
                {
                    Id = existing.Id,
                    Tag = existing.Tag,
                    LeadMinutes = existing.LeadMinutes,
                });
                updated++;
            }
        }

        // Vong 2: ngoai le cua chuoi lap — bo han mot lan, hoac doi lan do sang ngay khac.
        foreach (var ev in events)
        {
            if (GoogleMap.ParentId(ev) is not { } parentGid) continue;
            if (GoogleMap.OriginalStart(ev) is not { } orig) continue;

            var parent = store.ByGoogleId(parentGid);
            if (parent is null) { skipped++; continue; }

            if (GoogleMap.IsCancelled(ev))
            {
                store.Skip(parent.Id, orig);
                exceptions++;
                continue;
            }

            if (!ev.TryGetProperty("start", out var st)) { skipped++; continue; }
            var newDate = GoogleMap.DatePart(st);
            if (newDate is null) { skipped++; continue; }

            store.Move(parent.Id, orig, newDate,
                       GoogleMap.TimePart(st),
                       ev.TryGetProperty("end", out var en) ? GoogleMap.TimePart(en) : null);
            exceptions++;
        }

        var r = new Result(added, updated, deleted, skipped, exceptions, unsupported);
        Log.Write($"google: keo xong — {r}");
        foreach (var u in unsupported)
            Log.Write($"google:   chua nhap duoc (kieu lap phuc tap): {u}");
        return r;
    }

    static string Title(JsonElement ev) =>
        ev.TryGetProperty("summary", out var s) ? s.GetString() ?? "(không tiêu đề)" : "(không tiêu đề)";
}
