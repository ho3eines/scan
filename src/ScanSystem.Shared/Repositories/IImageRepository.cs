using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

public interface IImageRepository
{
    /// <summary>ذخیره یک صفحه تصویر با Thumbnail.</summary>
    Task<ScanImage> AddPageAsync(Guid requestId, string fileName, byte[] data, byte[]? thumbnail, int pageNumber);

    Task<ScanImage?> GetByIdAsync(Guid id);
    Task<byte[]?> GetDataAsync(Guid id);
    Task<byte[]?> GetThumbnailAsync(Guid id);
    Task<int> DeleteAsync(Guid id);

    /// <summary>به‌روزرسانی داده‌های تصویر (مثلاً بعد از Rotate یا Replace).</summary>
    Task UpdateDataAsync(Guid id, byte[] data, byte[]? thumbnail);

    /// <summary>گالری Lazy Load با OFFSET/FETCH.</summary>
    Task<(List<ImageGalleryItemDto> items, int total)> GetGalleryPageAsync(
        int skip, int take, Guid? groupId, string? machineName);

    Task<List<ScanImage>> GetByRequestAsync(Guid requestId);
    Task<List<Guid>> GetIdsByRequestAsync(Guid requestId);
}
