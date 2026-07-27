using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

public interface IImageGroupRepository
{
    Task<List<ImageGroup>> GetAllAsync();
    Task<ImageGroup?> GetByIdAsync(Guid id);
    Task<ImageGroup> CreateAsync(string name);
    Task<int> DeleteAsync(Guid id);

    /// <summary>تخصیص یک تصویر به یک گروه (درج اگر موجود نباشد).</summary>
    Task AssignImageAsync(Guid imageId, Guid groupId);
    Task UnassignImageAsync(Guid imageId, Guid groupId);
    Task UnassignAllFromImageAsync(Guid imageId);

    /// <summary>لیست گروه‌های یک تصویر.</summary>
    Task<List<ImageGroup>> GetGroupsForImageAsync(Guid imageId);
}
