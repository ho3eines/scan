namespace ScanSystem.Agent;

/// <summary>تنظیمات Agent از فایل agentsettings.json کنار فایل اجرایی.</summary>
public class AgentSettings
{
    /// <summary>نام نمایشی اختیاری (در حال حاضر شناسه اصلی همان MachineName است).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>آدرس Hub سرور.</summary>
    public string ServerUrl { get; set; } = "http://localhost:5002/scanhub";

    /// <summary>اتصال خودکار به سرور هنگام اجرای برنامه.</summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>شروع به‌صورت Minimize در Tray (بدون نمایش پنجره).</summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>ثبت در Run Key ویندوز برای شروع خودکار همراه با لاگین.</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>حداکثر تعداد صفحات در یک اسکن چندصفحه‌ای (ADF).</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>
    /// شناسه (DeviceID) اسکنر انتخاب‌شده توسط کاربر. خالی = انتخاب خودکار اولین اسکنر موجود.
    /// از منوی «تنظیمات اسکنر» در پنجره Agent قابل انتخاب است.
    /// </summary>
    public string SelectedScannerId { get; set; } = "";

    /// <summary>
    /// اگر true باشد و هنگام اسکن هیچ اسکنری در دسترس نباشد، Agent یک تصویر آزمایشی (Placeholder)
    /// می‌سازد و به سرور ارسال می‌کند — دقیقاً مثل رفتار قبلی برنامه.
    /// اگر false باشد (پیش‌فرض جدید)، در نبود اسکنر هیچ تصویری ساخته نمی‌شود و به سرور
    /// خطای «اسکنر تنظیم نیست» گزارش می‌شود.
    /// </summary>
    public bool UsePlaceholderWhenNoScanner { get; set; } = false;

    /// <summary>
    /// اگر true باشد، صفحاتی که کاملاً سفید/خالی تشخیص داده شوند، به سرور ارسال نمی‌شوند
    /// (مثلاً وقتی ADF یک برگ خالی را هم اسکن کرده باشد).
    /// </summary>
    public bool SkipBlankPages { get; set; } = false;

    private static string FilePath
        => System.IO.Path.Combine(AppContext.BaseDirectory, "agentsettings.json");

    public static AgentSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                var json = System.IO.File.ReadAllText(FilePath);
                var s = System.Text.Json.JsonSerializer.Deserialize<AgentSettings>(json);
                if (s != null)
                {
                    if (string.IsNullOrWhiteSpace(s.ServerUrl)) s.ServerUrl = "http://localhost:5002/scanhub";
                    if (s.MaxPages <= 0) s.MaxPages = 50;
                    return s;
                }
            }
        }
        catch { /* فایل خراب → تنظیمات پیش‌فرض */ }
        return new AgentSettings();
    }

    /// <summary>ذخیره تنظیمات فعلی (مثلاً پس از تغییر آدرس سرور در UI).</summary>
    public static void Save(AgentSettings settings)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(settings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
