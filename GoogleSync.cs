using Microsoft.Win32;

namespace Deskcal;

/// <summary>
/// Chon lich de dong bo, va rao chan khong cho ghi nham sang lich khac.
///
/// <para><b>Vi sao phai rao cung.</b> Tai khoan nay co ca lich "Gia dinh" chia se voi
/// nguoi khac. Ghi nham mot su kien vao do la ca nha nhin thay, va khong rut lai duoc
/// bang cach xin loi. Nen dich ghi KHONG bao gio la tham so truyen vao ham ghi — moi
/// duong ghi deu phai di qua <see cref="Target"/>, va ham do nem neu lich dang chon
/// khong con hop le.</para>
/// </summary>
public static class GoogleSync
{
    const string Key = @"Software\deskcal";

    /// <summary>
    /// Lich duy nhat duoc phep doc/ghi. Rong = chua chon.
    /// Luu id day du (voi lich chinh thi id chinh la dia chi email).
    /// </summary>
    public static string CalendarId
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("GoogleCalendarId") as string ?? "";
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            k?.SetValue("GoogleCalendarId", value ?? "", RegistryValueKind.String);
            Log.Write($"google: chon lich de dong bo = {value}");
        }
    }

    public static bool Chosen => CalendarId.Length > 0;

    /// <summary>
    /// Dich duy nhat duoc phep ghi. Moi ham ghi phai lay dich tu day, khong nhan tham so
    /// — de khong the goi nham.
    /// </summary>
    public static string Target => Chosen
        ? CalendarId
        : throw new InvalidOperationException("Chưa chọn lịch Google để đồng bộ.");

    /// <summary>
    /// Lich chia se (dang ...@group.calendar.google.com) thi hoi lai truoc khi chon:
    /// ghi vao do la nguoi khac nhin thay. Lich chinh co id la dia chi email.
    /// </summary>
    public static bool IsShared(string calendarId) =>
        calendarId.EndsWith("@group.calendar.google.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Chot lai truoc moi lan ghi: id sap ghi phai dung bang lich da chon. Bat duoc ca
    /// truong hop nguoi dung doi lich giua chung ma cho nao do con giu id cu.
    /// </summary>
    public static void EnsureAllowed(string calendarId)
    {
        if (!string.Equals(calendarId, Target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Chặn ghi vào lịch không được chọn: {calendarId} (chỉ cho phép {Target}).");
    }
}
