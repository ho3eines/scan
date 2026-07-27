using ScanSystem.Shared.Entities;

namespace ScanSystem.Web.Services;

/// <summary>
/// ساختار نمایش لیست Agentها در UI گالری/اسکن.
/// جدا از entity پایه برای اضافه کردن خواص کمکی مانند «شمارش» و «نام ماشین یونیک».
/// </summary>
public class AgentListDto
{
    public List<AgentDto> All { get; private set; } = new();
    public List<string> Machines { get; private set; } = new();

    /// <summary>به‌روزرسانی از روی لیست خام سرور و محاسبه فهرست Machines منحصر.</summary>
    public void Update(IEnumerable<AgentDto>? list)
    {
        All = (list ?? Enumerable.Empty<AgentDto>()).ToList();
        Machines = All
            .Select(a => a.MachineName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(m => m)
            .ToList();
    }
}
