using Dapper;
using Microsoft.Extensions.Logging;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

/// <summary>
/// Repository برای جداول ImageGroups و ImageGroupItems (رابطه many-to-many تصویر ↔ گروه).
/// استفاده از STUFF/FOR XML برای استخراج گروه‌ها در GetGalleryPageAsync مستقل از این کلاس است.
/// </summary>
public class ImageGroupRepository : IImageGroupRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<ImageGroupRepository>? _logger;

    public ImageGroupRepository(IDbConnectionFactory factory, ILogger<ImageGroupRepository>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<List<ImageGroup>> GetAllAsync()
    {
        const string sql = "SELECT Id, Name FROM dbo.ImageGroups ORDER BY Name ASC;";
        try
        {
            using var conn = _factory.CreateConnection();
            var list = await conn.QueryAsync<ImageGroup>(sql);
            return list.AsList();
        }
        catch (Exception ex) { LogErr(ex); return new List<ImageGroup>(); }
    }

    public async Task<ImageGroup?> GetByIdAsync(Guid id)
    {
        const string sql = "SELECT Id, Name FROM dbo.ImageGroups WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<ImageGroup>(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    /// <summary>ساخت یک گروه جدید با نام یکتا. اگر از قبل موجود باشد، همان را برمی‌گرداند.</summary>
    public async Task<ImageGroup> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("نام گروه نباید خالی باشد.", nameof(name));
        name = name.Trim();

        try
        {
            using var conn = _factory.CreateConnection();
            // اطمینان از یکتا بودن نام: اگر موجود بود همان رکورد را FETCH می‌کنیم.
            var existing = await conn.QuerySingleOrDefaultAsync<ImageGroup>(
                "SELECT Id, Name FROM dbo.ImageGroups WHERE Name = @Name;", new { Name = name });
            if (existing is not null) return existing;

            var group = new ImageGroup { Id = Guid.NewGuid(), Name = name };
            const string sql = @"
                INSERT INTO dbo.ImageGroups (Id, Name)
                OUTPUT inserted.Id, inserted.Name
                VALUES (@Id, @Name);";
            var inserted = await conn.QuerySingleAsync<ImageGroup>(sql, new { group.Id, group.Name });
            return inserted;
        }
        catch (Exception ex)
        {
            LogErr(ex);
            return new ImageGroup { Id = Guid.NewGuid(), Name = name };
        }
    }

    public async Task<int> DeleteAsync(Guid id)
    {
        const string sql = "DELETE FROM dbo.ImageGroups WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return 0; }
    }

    /// <summary>
    /// تخصیص یک تصویر به یک گروه فقط در صورت عدم وجود ثبت می‌شود (جلوگیری از تکرار توسط UQ).
    /// </summary>
    public async Task AssignImageAsync(Guid imageId, Guid groupId)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM dbo.ImageGroupItems WHERE ImageId = @ImageId AND GroupId = @GroupId)
                INSERT INTO dbo.ImageGroupItems (Id, ImageId, GroupId)
                VALUES (@Id, @ImageId, @GroupId);";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Id = Guid.NewGuid(), ImageId = imageId, GroupId = groupId });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    public async Task UnassignImageAsync(Guid imageId, Guid groupId)
    {
        const string sql = "DELETE FROM dbo.ImageGroupItems WHERE ImageId = @ImageId AND GroupId = @GroupId;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { ImageId = imageId, GroupId = groupId });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    /// <summary>حذف همه تخصیص‌های یک تصویر از هر گروه (مثلاً قبل از جابه‌جایی به گروه جدید).</summary>
    public async Task UnassignAllFromImageAsync(Guid imageId)
    {
        const string sql = "DELETE FROM dbo.ImageGroupItems WHERE ImageId = @ImageId;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { ImageId = imageId });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    /// <summary>لیست گروه‌های اختصاص‌داده‌شده به یک تصویر.</summary>
    public async Task<List<ImageGroup>> GetGroupsForImageAsync(Guid imageId)
    {
        const string sql = @"
            SELECT g.Id, g.Name
            FROM dbo.ImageGroups g
            INNER JOIN dbo.ImageGroupItems igi ON igi.GroupId = g.Id
            WHERE igi.ImageId = @ImageId
            ORDER BY g.Name ASC;";
        try
        {
            using var conn = _factory.CreateConnection();
            var list = await conn.QueryAsync<ImageGroup>(sql, new { ImageId = imageId });
            return list.AsList();
        }
        catch (Exception ex) { LogErr(ex); return new List<ImageGroup>(); }
    }

    private void LogErr(Exception ex) => _logger?.LogError(ex, "ImageGroupRepository error");
}
