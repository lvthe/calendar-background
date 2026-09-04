namespace Deskcal;

/// <summary>
/// Dich qua lai giua RRULE cua RFC 5545 (Google dung) va 5 kieu lap cua deskcal.
///
/// <para><b>Nguyen tac: tu choi con hon doan.</b> RRULE bieu dien duoc nhieu thu deskcal
/// khong co — lap cach 2 tuan, "thu Hai va thu Tu", ket thuc sau 10 lan, ket thuc ngay
/// 31/12. Nhan bua mot trong so do roi luu thanh WEEKLY la lich hien SAI mai mai ma
/// khong ai bao. Nen chi nhan dung nhung gi deskcal bieu dien duoc CHINH XAC; con lai
/// tra null va de ben goi tu gian ra tung lan mot.</para>
/// </summary>
public static class GoogleRrule
{
    /// <summary>deskcal -> RRULE. Chuoi tra ve dat vao truong "recurrence" cua Google.</summary>
    public static string? ToGoogle(string rrule) => rrule switch
    {
        "DAILY" => "RRULE:FREQ=DAILY",
        "WEEKLY" => "RRULE:FREQ=WEEKLY",
        "MONTHLY" => "RRULE:FREQ=MONTHLY",
        "YEARLY" => "RRULE:FREQ=YEARLY",
        _ => null,   // NONE: khong lap, khong gui truong recurrence
    };

    /// <summary>
    /// RRULE -> deskcal. Tra null khi khong bieu dien duoc chinh xac.
    /// <paramref name="start"/> la ngay bat dau, de kiem BYDAY co khop dung thu do khong.
    /// </summary>
    public static string? FromGoogle(IEnumerable<string>? recurrence, DateOnly start)
    {
        if (recurrence is null) return null;

        // Bo qua RDATE/EXDATE o day: EXDATE la ngoai le, ben goi xu ly bang bang overrides.
        var line = recurrence.FirstOrDefault(r =>
            r.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in line["RRULE:".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq > 0) parts[kv[..eq].Trim()] = kv[(eq + 1)..].Trim();
        }

        if (!parts.TryGetValue("FREQ", out var freq)) return null;

        // deskcal khong co ngay ket thuc chuoi lap. Nhan COUNT/UNTIL la bien mot chuoi
        // huu han thanh vo han — lich se de ra su kien ma khong bao gio het.
        if (parts.ContainsKey("COUNT") || parts.ContainsKey("UNTIL")) return null;

        // deskcal khong co buoc lap. INTERVAL=2 ma nhan thanh WEEKLY la sai gap doi so lan.
        if (parts.TryGetValue("INTERVAL", out var iv) && iv != "1") return null;

        // Cac bo loc nay deu khong bieu dien duoc: BYMONTHDAY, BYSETPOS, BYWEEKNO...
        foreach (var k in parts.Keys)
            if (k.StartsWith("BY", StringComparison.OrdinalIgnoreCase) && !k.Equals("BYDAY", StringComparison.OrdinalIgnoreCase))
                return null;

        switch (freq.ToUpperInvariant())
        {
            case "DAILY":
                return parts.ContainsKey("BYDAY") ? null : "DAILY";

            case "WEEKLY":
                // Khong co BYDAY thi mac dinh lap dung thu cua ngay bat dau — dung y
                // deskcal. Co BYDAY thi chi nhan khi dung mot thu, va la thu do.
                if (!parts.TryGetValue("BYDAY", out var by)) return "WEEKLY";
                var days = by.Split(',', StringSplitOptions.RemoveEmptyEntries);
                return days.Length == 1 && days[0].Equals(Code(start.DayOfWeek), StringComparison.OrdinalIgnoreCase)
                    ? "WEEKLY" : null;

            case "MONTHLY":
                // BYDAY o MONTHLY nghia la "thu Hai thu hai cua thang" — deskcal lap theo
                // ngay trong thang, khong phai theo thu.
                return parts.ContainsKey("BYDAY") ? null : "MONTHLY";

            case "YEARLY":
                return parts.ContainsKey("BYDAY") ? null : "YEARLY";

            default:
                return null;
        }
    }

    /// <summary>Ma hai chu cai cua thu, theo RFC 5545.</summary>
    public static string Code(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };
}
