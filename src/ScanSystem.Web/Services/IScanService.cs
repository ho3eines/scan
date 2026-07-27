using ScanSystem.Shared;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;
using ScanSystem.Shared.Repositories;

namespace ScanSystem.Web.Services;

/// <summary>
/// Application Service هماهنگ‌کننده بین SignalR Hub، Repositoryهای Dapper و نگاشت اتصال Agentها.
/// هیچ DbContext/EF Core استفاده نمی‌شود — همه دسترسی‌ها از طریق Repositoryها و Dapper انجام می‌شود.
/// </summary>
public interface IScanService
{
    // ── Agentها ──
    Task<Guid> UpsertAgentAsync(string machineName, string connectionId);
    Task SetAgentOfflineByMachineAsync(string machineName);
    Task<List<AgentDto>> GetAgentsAsync();

    // ── درخواست‌های اسکن ──
    Task<Guid> CreateRequestAsync(string machineName, bool isMultiPage);
    Task SetProcessingAsync(Guid id);
    Task SetCompletedAsync(Guid id);
    Task SetErrorAsync(Guid id, string error);
    Task DeleteRequestAsync(Guid id);
    /// <summary>کوئری Server-side DataTable.</summary>
    Task<(List<ScanRequestDto> data, int recordsTotal, int recordsFiltered)> GetRequestsDataAsync(
        int start, int length, string? search, int orderColumnIndex, string orderDir);

    // ── تصاویر / گالری ──
    /// <summary>ذخیره یک صفحه اسکن‌شده + ساخت Thumbnail خودکار.</summary>
    Task<ScanImage> SavePageAsync(Guid requestId, string fileName, byte[] data, int pageNumber);
    Task<(List<ImageGalleryItemDto> items, int total)> GetGalleryPageAsync(int skip, int take, Guid? groupId, string? machineName);
    Task<ImageDownloadDto?> GetImageDownloadAsync(Guid id);
    Task<byte[]?> GetImageThumbnailAsync(Guid id);
    Task<int> DeleteImageAsync(Guid id);
    Task UpdateImageAsync(Guid id, byte[] data, byte[]? thumbnail);
    Task<List<ImageDownloadDto>> GetImagesByRequestAsync(Guid requestId);
    Task<List<ImageDownloadDto>> GetImagesByIdsAsync(IEnumerable<Guid> ids);

    // ── گروه‌ها ──
    Task<List<ImageGroup>> GetGroupsAsync();
    Task<ImageGroup> CreateGroupAsync(string name);
    Task<int> DeleteGroupAsync(Guid id);
    /// <summary>تخصیص یک تصویر به یک گروه (با نام گروه؛ اگر موجود نباشد ساخته می‌شود).</summary>
    Task AssignGroupAsync(Guid imageId, string groupName);
    Task UnassignImageAsync(Guid imageId, Guid groupId);
    Task<List<ImageGroup>> GetGroupsForImageAsync(Guid imageId);
}
