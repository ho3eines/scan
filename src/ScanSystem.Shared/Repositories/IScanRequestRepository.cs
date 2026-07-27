using System.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

public interface IScanRequestRepository
{
    /// <summary>ساخت درخواست جدید و بازگرداندن Id آن + entity.</summary>
    Task<ScanRequest> CreateAsync(Guid agentId, bool isMultiPage);

    Task<ScanRequest?> GetByIdAsync(Guid id);
    Task SetStatusAsync(Guid id, string status);
    Task SetCompletedAsync(Guid id, string status);
    Task SetErrorAsync(Guid id, string message);

    /// <summary>
    /// لیست درخواست‌ها برای جدول Blazor — خروجی به‌صورت System.Data.DataTable
    /// (ExecuteReader + DataTable.Load طبق نیازمندی §6.1).
    /// Paging/Sorting/Filtering کاملاً سمت سرور و در T-SQL انجام می‌شود.
    /// </summary>
    Task<(DataTable data, int recordsTotal, int recordsFiltered)> GetDataTableAsync(
        int page, int pageSize, string? search, int orderColumnIndex, string orderDir);

    /// <summary>آخرین درخواست‌ها برای صفحه اسکن (DataTable).</summary>
    Task<DataTable> GetRecentDataTableAsync(int take);

    Task<int> DeleteAsync(Guid id);
}
