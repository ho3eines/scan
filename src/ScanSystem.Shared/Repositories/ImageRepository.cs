using Dapper;
using Microsoft.Extensions.Logging;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

/// <summary>
/// Repository برای جدول Images (تصاویر اسکن‌شده + Thumbnail باینری).
/// تمام کوئری‌ها Parametrized هستند و گالری از OFFSET/FETCH NEXT برای Lazy Loading استفاده می‌کند.
/// </summary>
public class ImageRepository : IImageRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<ImageRepository>? _logger;

    public ImageRepository(IDbConnectionFactory factory, ILogger<ImageRepository>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>ذخیره یک صفحه تصویر همراه با Thumbnail.</summary>
    public async Task<ScanImage> AddPageAsync(Guid requestId, string fileName, byte[] data, byte[]? thumbnail, int pageNumber)
    {
        const string sql = @"
            INSERT INTO dbo.Images (Id, RequestId, FileName, Data, Thumbnail, PageNumber, CreatedAt)
            OUTPUT inserted.Id, inserted.RequestId, inserted.FileName, inserted.Data, inserted.Thumbnail,
                    inserted.PageNumber, inserted.CreatedAt
            VALUES (@Id, @RequestId, @FileName, @Data, @Thumbnail, @PageNumber, SYSDATETIME());";

        var image = new ScanImage
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? $"page_{pageNumber}.jpg" : fileName,
            Data = data,
            Thumbnail = thumbnail,
            PageNumber = pageNumber
        };

        try
        {
            using var conn = _factory.CreateConnection();
            var inserted = await conn.QuerySingleAsync<ScanImage>(sql, new
            {
                image.Id, image.RequestId, image.FileName, image.Data,
                Thumbnail = thumbnail, image.PageNumber
            });
            return inserted;
        }
        catch (Exception ex)
        {
            LogErr(ex);
            return image;
        }
    }

    public async Task<ScanImage?> GetByIdAsync(Guid id)
    {
        const string sql = @"
            SELECT Id, RequestId, FileName, Data, Thumbnail, PageNumber, CreatedAt
            FROM dbo.Images WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<ScanImage>(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    /// <summary>بایت‌های تصویر اصلی (Data) برای نمایش/دانلود Fullscreen.</summary>
    public async Task<byte[]?> GetDataAsync(Guid id)
    {
        const string sql = "SELECT Data FROM dbo.Images WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<byte[]>(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    /// <summary>بایت‌های Thumbnail برای نمایش در گالری.</summary>
    public async Task<byte[]?> GetThumbnailAsync(Guid id)
    {
        const string sql = "SELECT Thumbnail FROM dbo.Images WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<byte[]>(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    public async Task<int> DeleteAsync(Guid id)
    {
        const string sql = "DELETE FROM dbo.Images WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return 0; }
    }

    /// <summary>به‌روزرسانی داده‌های تصویر و Thumbnail (بعد از Rotate یا Replace Upload).</summary>
    public async Task UpdateDataAsync(Guid id, byte[] data, byte[]? thumbnail)
    {
        const string sql = @"
            UPDATE dbo.Images
            SET Data = @Data, Thumbnail = @Thumbnail
            WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Data = data, Thumbnail = thumbnail, Id = id });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    /// <summary>
    /// یک صفحه گالری با OFFSET/FETCH NEXT (Lazy Loading).
    /// می‌توان فیلتر بر اساس GroupId یا MachineName اعمال کرد.
    /// NOTE: Data و Thumbnail در خروجی نیستند تا پهنای باند حفظ شود؛ فقط HasThumbnail برمی‌گردد.
    /// </summary>
    public async Task<(List<ImageGalleryItemDto> items, int total)> GetGalleryPageAsync(
        int skip, int take, Guid? groupId, string? machineName)
    {
        var whereParts = new List<string>();
        var p = new DynamicParameters();
        p.Add("@Skip", skip);
        p.Add("@Take", take);

        if (groupId.HasValue)
        {
            whereParts.Add("EXISTS (SELECT 1 FROM dbo.ImageGroupItems igi WHERE igi.ImageId = i.Id AND igi.GroupId = @GroupId)");
            p.Add("@GroupId", groupId.Value);
        }
        if (!string.IsNullOrWhiteSpace(machineName))
        {
            whereParts.Add("a.MachineName = @MachineName");
            p.Add("@MachineName", machineName.Trim());
        }

        var where = whereParts.Count > 0 ? "WHERE " + string.Join(" AND ", whereParts) : "";

        // نام گروه‌های هر تصویر به‌صورت یک رشته‌ی CSV جدا شده با کاما برای نمایش آسان در UI.
        var groupsExpr = @"
            STUFF((
                SELECT N', ' + g.Name
                FROM dbo.ImageGroupItems igi2
                JOIN dbo.ImageGroups g ON g.Id = igi2.GroupId
                WHERE igi2.ImageId = i.Id
                ORDER BY g.Name
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'), 1, 2, N'')";

        var dataSql = $@"
            SELECT i.Id, i.RequestId, i.FileName, i.PageNumber, i.CreatedAt,
                   a.MachineName AS AgentMachineName,
                   ({groupsExpr}) AS Groups,
                   CASE WHEN i.Thumbnail IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasThumbnail
            FROM dbo.Images i
            INNER JOIN dbo.ScanRequests r ON r.Id = i.RequestId
            INNER JOIN dbo.Agents a ON a.Id = r.AgentId
            {where}
            ORDER BY i.CreatedAt DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

        var countSql = $@"
            SELECT COUNT(*)
            FROM dbo.Images i
            INNER JOIN dbo.ScanRequests r ON r.Id = i.RequestId
            INNER JOIN dbo.Agents a ON a.Id = r.AgentId
            {where};";

        try
        {
            using var conn = _factory.CreateConnection();
            int total = await conn.ExecuteScalarAsync<int>(countSql, p);
            var rows = await conn.QueryAsync<ImageGalleryItemDto>(dataSql, p);
            return (rows.AsList(), total);
        }
        catch (Exception ex)
        {
            LogErr(ex);
            return (new List<ImageGalleryItemDto>(), 0);
        }
    }

    /// <summary>تمام صفحات یک درخواست (به ترتیب PageNumber).</summary>
    public async Task<List<ScanImage>> GetByRequestAsync(Guid requestId)
    {
        const string sql = @"
            SELECT Id, RequestId, FileName, Data, Thumbnail, PageNumber, CreatedAt
            FROM dbo.Images
            WHERE RequestId = @RequestId
            ORDER BY PageNumber ASC;";
        try
        {
            using var conn = _factory.CreateConnection();
            var list = await conn.QueryAsync<ScanImage>(sql, new { RequestId = requestId });
            return list.AsList();
        }
        catch (Exception ex) { LogErr(ex); return new List<ScanImage>(); }
    }

    /// <summary>فقط شناسه صفحات یک درخواست (سبک، برای ZIP یا شمارش).</summary>
    public async Task<List<Guid>> GetIdsByRequestAsync(Guid requestId)
    {
        const string sql = "SELECT Id FROM dbo.Images WHERE RequestId = @RequestId ORDER BY PageNumber ASC;";
        try
        {
            using var conn = _factory.CreateConnection();
            var list = await conn.QueryAsync<Guid>(sql, new { RequestId = requestId });
            return list.AsList();
        }
        catch (Exception ex) { LogErr(ex); return new List<Guid>(); }
    }

    private void LogErr(Exception ex) => _logger?.LogError(ex, "ImageRepository error");
}
