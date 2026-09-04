using System.Text.Json;

namespace Deskcal;

/// <summary>
/// Dich mot su kien Google sang TaskRow cua deskcal.
///
/// <para><b>Mui gio.</b> Google tra ve mot THOI DIEM co offset ("2026-09-07T09:00:00+07:00").
/// deskcal luu gio TRAN theo gio may. Nen doi o day: ToLocalTime roi bo offset di. Viec
/// ca ngay thi Google tra "date" khong co gio — do la ngay tran, KHONG duoc doi mui gio,
/// doi la lech mot ngay.</para>
///
/// <para><b>Khong bieu dien duoc thi noi ro.</b> Ham tra ve ca ly do bo qua, de ben goi
/// ghi log thay vi im lang nuot mat su kien.</para>
/// </summary>
public static class GoogleMap
{
    /// <summary>Ket qua dich: hoac ra mot TaskRow, hoac ra ly do khong dich duoc.</summary>
    public sealed record Mapped(TaskRow? Row, string? Skip, bool NeedsExpanding = false)
    {
        public static Mapped Ok(TaskRow r) => new(r, null);
        public static Mapped No(string why) => new(null, why);
        public static Mapped Expand(string why) => new(null, why, true);
    }

    /// <summary>Trang thai "cancelled" nghia la su kien da bi xoa hoac lan lap do bi bo.</summary>
    public static bool IsCancelled(JsonElement ev) =>
        Str(ev, "status") is "cancelled";

    /// <summary>Su kien ngoai le cua mot chuoi — Google gan no voi chuoi qua recurringEventId.</summary>
    public static string? ParentId(JsonElement ev) => Str(ev, "recurringEventId");

    /// <summary>Ngay GOC cua lan lap bi doi, nam trong originalStartTime.</summary>
    public static DateOnly? OriginalStart(JsonElement ev)
    {
        if (!ev.TryGetProperty("originalStartTime", out var o)) return null;
        return DatePart(o);
    }

    public static Mapped FromEvent(JsonElement ev)
    {
        if (IsCancelled(ev)) return Mapped.No("đã huỷ");

        string id = Str(ev, "id") ?? "";
        if (id.Length == 0) return Mapped.No("thiếu id");

        string title = Str(ev, "summary") ?? "(không tiêu đề)";

        if (!ev.TryGetProperty("start", out var start)) return Mapped.No("thiếu start");
        var date = DatePart(start);
        if (date is null) return Mapped.No("không đọc được ngày bắt đầu");

        var at = TimePart(start);
        TimeOnly? end = ev.TryGetProperty("end", out var e) ? TimePart(e) : null;

        // Viec ca ngay: Google de "end.date" la ngay HOM SAU (nua khoang mo). Bo qua end
        // luon, deskcal khong bieu dien viec ca ngay nhieu ngay.
        if (at is null) end = null;

        string? rrule = null;
        if (ev.TryGetProperty("recurrence", out var rec) && rec.ValueKind == JsonValueKind.Array)
        {
            var lines = rec.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            rrule = GoogleRrule.FromGoogle(lines, date.Value);
            // Co lap that nhung deskcal khong bieu dien duoc chinh xac -> phai gian ra
            // tung lan, khong duoc nhan bua thanh WEEKLY.
            if (rrule is null) return Mapped.Expand("kiểu lặp deskcal không biểu diễn được");
        }

        return Mapped.Ok(new TaskRow(
            0, title, date.Value, at, end,
            Tag: "work",
            Rrule: rrule ?? "NONE",
            LeadMinutes: 0,
            Location: Str(ev, "location"),
            Note: Str(ev, "description"),
            GoogleId: id,
            GoogleUpdated: Str(ev, "updated")));
    }

    // ---------- doc start / end ----------

    /// <summary>
    /// Ngay cua mot moc thoi gian Google. "date" la ngay tran (viec ca ngay) — lay nguyen,
    /// KHONG doi mui gio. "dateTime" la thoi diem — doi ve gio may roi moi lay ngay.
    /// </summary>
    public static DateOnly? DatePart(JsonElement slot)
    {
        if (Str(slot, "date") is { Length: > 0 } d && DateOnly.TryParse(d, out var only))
            return only;
        if (Str(slot, "dateTime") is { Length: > 0 } dt
            && DateTimeOffset.TryParse(dt, out var off))
            return DateOnly.FromDateTime(off.ToLocalTime().DateTime);
        return null;
    }

    /// <summary>Gio theo dong ho may. null = viec ca ngay.</summary>
    public static TimeOnly? TimePart(JsonElement slot)
    {
        if (Str(slot, "dateTime") is { Length: > 0 } dt
            && DateTimeOffset.TryParse(dt, out var off))
            return TimeOnly.FromDateTime(off.ToLocalTime().DateTime);
        return null;
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
