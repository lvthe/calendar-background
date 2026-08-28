using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Deskcal;

public static class Toast
{
    public const string AppId = "deskcal";

    /// <summary>Neu WinRT khong ban duoc thi doi sang balloon tip cua tray icon.</summary>
    public static Action<string, string>? Fallback;

    /// <summary>
    /// App khong dong goi (unpackaged) nen phai tu dang ky AUMID trong HKCU, khong can admin.
    /// Co key nay thi toast hien ten "deskcal" va bat/tat rieng duoc trong Settings > Notifications.
    /// </summary>
    public static void Register()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Classes\AppUserModelId\" + AppId);
            k?.SetValue("DisplayName", "deskcal", RegistryValueKind.String);
        }
        catch (Exception e) { Log.Write($"khong dang ky duoc AUMID: {e.Message}"); }
    }

    public static void Show(string title, string body)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(
                "<toast>" +
                  "<visual><binding template=\"ToastGeneric\">" +
                    "<text>" + SecurityElement.Escape(title) + "</text>" +
                    "<text>" + SecurityElement.Escape(body) + "</text>" +
                  "</binding></visual>" +
                  "<audio src=\"ms-winsoundevent:Notification.Reminder\"/>" +
                "</toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(doc));
        }
        catch (Exception e)
        {
            Log.Write($"toast that bai, doi sang balloon: {e.Message}");
            Fallback?.Invoke(title, body);
        }
    }
}
