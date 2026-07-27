using System.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

public interface IAgentRepository
{
    Task<Agent?> GetByMachineNameAsync(string machineName);
    Task<Agent?> GetByIdAsync(Guid id);
    Task<List<AgentDto>> GetAllAsync();

    /// <summary>
    /// لیست Agentها برای جدول Blazor — خروجی به‌صورت System.Data.DataTable
    /// (طبق نیازمندی §6.1/§6.6: پیمایش DataRowها در لایه نمایش).
    /// </summary>
    Task<DataTable> GetAllDataTableAsync();

    /// <summary>Upsert بر اساس MachineName. منظم: اگر موجود بود IsOnline/LastSeen را به‌روز می‌کند.</summary>
    Task UpsertAsync(string machineName, bool isOnline, string? connectionId);

    /// <summary>علامت‌گذاری Agent مشخص به‌صورت آنلاین.</summary>
    Task SetOnlineAsync(Guid id, string connectionId);

    /// <summary>علامت‌گذاری Agent مشخص (با Id) به‌صورت آفلاین.</summary>
    Task SetOfflineAsync(Guid id);

    /// <summary>حذف یک Agent.</summary>
    Task<int> DeleteAsync(Guid id);
}
