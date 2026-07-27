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
