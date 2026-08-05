using System.Globalization;

namespace ScanSystem.Shared;

/// <summary>
/// ابزار تبدیل تاریخ میلادی به شمسی.
/// ستون Date جدول PDDImage.ImagesTable از نوع nvarchar(10) است و
/// تاریخ به صورت شمسی «1404/05/14» ذخیره می‌شود.
/// </summary>
public static class PersianDate
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>تاریخ شمسی امروز به فرمت yyyy/MM/dd (مثلاً 1404/05/14).</summary>
    public static string Today()
        => FormatDate(DateTime.Now);

    /// <summary>ساعت فعلی به فرمت HH:mm (مثلاً 14:35) — برای ستون ScanTime.</summary>
    public static string NowTime()
        => DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>تبدیل یک تاریخ میلادی به شمسی با فرمت yyyy/MM/dd.</summary>
    public static string FormatDate(DateTime dt)
        => $"{Calendar.GetYear(dt):0000}/{Calendar.GetMonth(dt):00}/{Calendar.GetDayOfMonth(dt):00}";

    /// <summary>تبدیل تاریخ و ساعت میلادی به شمسی با فرمت yyyy/MM/dd HH:mm.</summary>
    public static string FormatDateTime(DateTime dt)
        => $"{FormatDate(dt)} {dt:HH:mm}";
}
