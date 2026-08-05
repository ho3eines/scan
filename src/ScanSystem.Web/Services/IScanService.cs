using System.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Web.Services;

/// <summary>
/// سرویس هماهنگ‌کننده بین Hub، دیتابیس (DataTable/DataRow) و رجیستری اتصال SignalR.
/// هیچ EF Core استفاده نمی‌شود.
/// </summary>
public interface IScanService
{
    // ── Agentها ──
    Task UpsertAgentAsync(string machineName, string connectionId);
    Task SetAgentOfflineByMachineAsync(string machineName);
    Task<List<AgentDto>> GetAgentsAsync();
    Task<DataTable> GetAgentsDataTableAsync();
    Task<int> DeleteAgentAsync(Guid id);

    // ── درخواست‌های اسکن ──
    Task<Guid> CreateRequestAsync(string machineName, bool isMultiPage, string? relationCode, string? picType, string? softwareCode, string? userCode);
    Task SetProcessingAsync(Guid id);
    Task SetCompletedAsync(Guid id);
    Task SetErrorAsync(Guid id, string error);
    Task DeleteRequestAsync(Guid id);
    Task<DataTable> GetRecentRequestsDataTableAsync(int take);
    Task<DataTable> GetRequestsListAsync();

    // ── تصاویر / گالری (PDDImage.ImagesTable) ──
    Task<decimal> SavePageAsync(Guid requestId, string fileName, string? contentType, byte[] data, int pageNumber);
    Task<(DataTable data, int total)> GetGalleryPageAsync(
        int skip,
        int take,
        decimal? groupId,
        string? relationCode,
        string? picType,
        string? userCode,
        string? softwareCode);
    Task<byte[]?> GetImageDataAsync(decimal id);
    Task<byte[]?> GetImageThumbnailAsync(decimal id);
    Task DeleteImageAsync(decimal id);
    Task UpdateImageAsync(decimal id, byte[] data);

    // ── گروه‌ها (1 به n: هر تصویر یک گروه) ──
    Task<DataTable> GetGroupsDataTableAsync();
    Task<decimal> EnsureGroupAsync(string name, string? softwareCode);
    Task DeleteGroupAsync(decimal id);
    Task AssignImageToGroupAsync(decimal imageId, string groupName, string? softwareCode);
    Task RemoveImageFromGroupAsync(decimal imageId, decimal groupId);
}
