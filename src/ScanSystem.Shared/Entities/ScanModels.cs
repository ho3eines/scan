namespace ScanSystem.Shared.Entities;

/// <summary>
/// مدل سبک نمایش یک Agent در UI.
/// بقیه داده‌ها مستقیماً از طریق DataTable/DataRow منتقل می‌شوند.
/// </summary>
public class AgentDto
{
    public Guid Id { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
    public string StatusDisplay => IsOnline ? "آنلاین" : "آفلاین";
}
