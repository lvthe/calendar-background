using System.Globalization;
using System.Text.RegularExpressions;

namespace Deskcal;

/// <summary>Doc ngay va gio nguoi dung go tay. Tach rieng de test duoc khong can UI.</summary>
public static class Parse
{
    static readonly Regex TimeRx = new(@"^(\d{1,2})\s*[:h.,]\s*(\d{1,2})$", RegexOptions.Compiled);
    static readonly Regex HourRx = new(@"^(\d{1,2})\s*h?$", RegexOptions.Compiled);
    static readonly Regex PackedRx = new(@"^(\d{1,2})(\d{2})$", RegexOptions.Compiled);
    static readonly Regex PlusRx = new(@"^\+\s*(\d{1,4})$", RegexOptions.Compiled);

    public const string AllDay = "(cả ngày)";

    /// <summary>Nhan tuong doi, "+N" ngay, hoac ngay cu the.</summary>
    public static bool TryDate(string? text, DateOnly today, out DateOnly date)
    {
        date = today;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;

        switch (t.ToLowerInvariant())
        {
            case "hôm nay": case "hom nay": case "today": return true;
            case "mai": case "ngày mai": case "ngay mai": date = today.AddDays(1); return true;
            case "ngày mốt": case "ngay mot": date = today.AddDays(2); return true;
            case "tuần sau": case "tuan sau": date = today.AddDays(7); return true;
        }

        if (PlusRx.Match(t) is { Success: true } pm)
        {
            date = today.AddDays(int.Parse(pm.Groups[1].Value));
            return true;
        }

        string[] withYear = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy"];
        if (DateOnly.TryParseExact(t, withYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
        {
            date = full;
            return true;
        }

        string[] noYear = ["dd/MM", "d/M", "dd-MM", "d-M"];
        if (DateOnly.TryParseExact(t, noYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out var partial))
        {
            // .NET dien nam hien tai theo DateTime.Now; ep lai theo `today` de ham
            // thuc su ton trong tham so duoc truyen vao
            if (partial.Year != today.Year)
            {
                try { partial = new DateOnly(today.Year, partial.Month, partial.Day); }
                catch (ArgumentOutOfRangeException) { /* 29/02 vao nam khong nhuan */ }
            }
            // "15/09" khi hom nay la 20/09 thi hieu la nam sau, khong phai qua khu
            date = partial < today ? partial.AddYears(1) : partial;
            return true;
        }

        return false;
    }

    /// <summary>true + null nghia la ca ngay. false la khong hieu chuoi.</summary>
    public static bool TryTime(string? text, out TimeOnly? at)
    {
        at = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t == AllDay) return true;

        int h, m = 0;
        if (TimeRx.Match(t) is { Success: true } tm)
        {
            h = int.Parse(tm.Groups[1].Value);
            m = int.Parse(tm.Groups[2].Value);
        }
        else if (PackedRx.Match(t) is { Success: true } pk)
        {
            h = int.Parse(pk.Groups[1].Value);
            m = int.Parse(pk.Groups[2].Value);
        }
        else if (HourRx.Match(t) is { Success: true } hm)
        {
            h = int.Parse(hm.Groups[1].Value);
        }
        else return false;

        if (h > 23 || m > 59) return false;
        at = new TimeOnly(h, m);
        return true;
    }
}
