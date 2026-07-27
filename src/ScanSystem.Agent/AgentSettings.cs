namespace ScanSystem.Agent;

/// <summary>تنظیمات Agent از فایل agentsettings.json کنار فایل اجرایی.</summary>
public class AgentSettings
{
    public string DisplayName { get; set; } = "";
    public string ServerUrl { get; set; } = "http://localhost:5002/scanhub";

    public static AgentSettings Load()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "agentsettings.json");
        try
        {
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                var s = System.Text.Json.JsonSerializer.Deserialize<AgentSettings>(json);
                if (s != null)
                {
                    if (string.IsNullOrWhiteSpace(s.ServerUrl)) s.ServerUrl = "http://localhost:5002/scanhub";
                    return s;
                }
            }
        }
        catch { }
        return new AgentSettings();
    }
}
