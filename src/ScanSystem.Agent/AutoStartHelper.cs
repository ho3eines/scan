using Microsoft.Win32;

namespace ScanSystem.Agent;

/// <summary>
/// شروع خودکار Agent همراه با ویندوز از طریق Run Key در HKCU
/// (بدون نیاز به دسترسی Administrator).
/// </summary>
public static class AutoStartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PddScanAgent";
    // نام قدیمی برای پاک‌سازی رجیستری هنگام تعویض نام برند
    private const string LegacyValueName = "ScanSystemAgent";

    /// <summary>فعال/غیرفعال کردن Auto Start.</summary>
    public static void Set(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                // اجرا در حالت Tray هنگام بالا آمدن ویندوز
                key.SetValue(ValueName, $"\"{exe}\" --tray");
                // ریشه‌کنی ورودی نام قدیمی (ScanSystemAgent) تا برنامه دوبار اجرا نشود
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // در محیط‌های بدون دسترسی به Registry خطا نادیده گرفته می‌شود.
        }
    }

    /// <summary>آیا Auto Start فعلاً فعال است؟</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch { return false; }
    }

    public static void Enable() => Set(true);
    public static void Disable() => Set(false);
}
