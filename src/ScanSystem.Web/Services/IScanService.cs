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
    Task<Guid> CreateRequestAsync(string machineName, bool isMultiPage, string? relationCode, string? inquiryCode, string? softwareCode, string? fullName = null);
    Task SetProcessingAsync(Guid id);
    Task SetCompletedAsync(Guid id);
    Task SetErrorAsync(Guid id, string error);
    Task DeleteRequestAsync(Guid id);
    Task<DataTable> GetRecentRequestsDataTableAsync(int take);
    Task<DataTable> GetRequestsListAsync();

    // ── تصاویر / گالری ──
    Task<Guid> SavePageAsync(Guid requestId, string fileName, byte[] data, int pageNumber);
    Task<(DataTable data, int total)> GetGalleryPageAsync(
        int skip,
        int take,
        Guid? groupId,
        string? machineName,
        string? relationCode,
        string? inquiryCode,
        string? softwareCode);
    Task<byte[]?> GetImageDataAsync(Guid id);
    Task<byte[]?> GetImageThumbnailAsync(Guid id);
    Task DeleteImageAsync(Guid id);
    Task UpdateImageAsync(Guid id, byte[] data);

    // ── گروه‌ها ──
    Task<DataTable> GetGroupsDataTableAsync();
    Task<Guid> EnsureGroupAsync(string name);
    Task DeleteGroupAsync(Guid id);
    Task AssignImageToGroupAsync(Guid imageId, string groupName);
    Task RemoveImageFromGroupAsync(Guid imageId, Guid groupId);
}
