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

    /// <summary>دریافت لیست درخواست‌ها برای DataTable Server-side (draw, start, length, search, order).</summary>
    Task<(List<ScanRequestDto> data, int recordsTotal, int recordsFiltered)> GetDataAsync(
        int start, int length, string? search, int orderColumnIndex, string orderDir);

    Task<int> DeleteAsync(Guid id);
}
