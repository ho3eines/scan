using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using ScanSystem.Shared;

namespace ScanSystem.Shared.Data;

/// <summary>
/// لایه دسترسی یکپارچه به داده‌ها با DataTable/DataRow.
/// همه کوئری‌ها از <see cref="ScanSql"/> خوانده می‌شوند.
///
/// تصاویر در جدول اصلی پروژه (PDDImage.ImagesTable) ذخیره می‌شوند و
/// Thumbnail ها در جدول جداگانه PDDImage.ImageThumbnails.
/// </summary>
public class ScanDataAccess
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<ScanDataAccess>? _logger;

    public ScanDataAccess(IDbConnectionFactory factory, ILogger<ScanDataAccess>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    // ───────────────────────── Agents ─────────────────────────

    public async Task<DataTable> GetAgentsAsync()
    {
        return await QueryDataTableAsync(ScanSql.AgentsGetAll);
    }

    public async Task<DataRow?> GetAgentByMachineNameAsync(string machineName)
    {
        var dt = await QueryDataTableAsync(ScanSql.AgentsGetByMachineName, new { MachineName = machineName });
        return dt.Rows.Count > 0 ? dt.Rows[0] : null;
    }

    public async Task<DataRow?> GetAgentByIdAsync(Guid id)
    {
        var dt = await QueryDataTableAsync(ScanSql.AgentsGetById, new { Id = id });
        return dt.Rows.Count > 0 ? dt.Rows[0] : null;
    }

    public async Task UpsertAgentAsync(string machineName, bool isOnline)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        await ExecuteAsync(ScanSql.AgentsUpsert, new
        {
            Id = Guid.NewGuid(),
            MachineName = machineName.Trim(),
            IsOnline = isOnline
        });
    }

    public async Task SetAgentOfflineAsync(Guid id)
        => await ExecuteAsync(ScanSql.AgentsSetOfflineById, new { Id = id });

    public async Task SetAgentOfflineByMachineAsync(string machineName)
        => await ExecuteAsync(ScanSql.AgentsSetOfflineByMachineName, new { MachineName = machineName });

    public async Task<int> DeleteAgentAsync(Guid id)
        => await ExecuteAsync(ScanSql.AgentsDelete, new { Id = id });

    // ───────────────────────── ScanRequests ─────────────────────────

    public async Task<Guid> CreateRequestAsync(
        Guid agentId,
        bool isMultiPage,
        string? relationCode,
        string? picType,
        string? softwareCode,
        string? userCode)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(ScanSql.RequestsCreate, new
        {
            Id = id,
            AgentId = agentId,
            Status = ScanStatus.Pending,
            IsMultiPage = isMultiPage,
            RelationCode = NormalizeCode(relationCode),
            PicType = NormalizeCode(picType),
            SoftwareCode = NormalizeCode(softwareCode),
            UserCode = NormalizeCode(userCode)
        });
        return id;
    }

    public async Task SetRequestStatusAsync(Guid id, string status)
        => await ExecuteAsync(ScanSql.RequestsSetStatus, new { Id = id, Status = status });

    public async Task CompleteRequestAsync(Guid id)
        => await ExecuteAsync(ScanSql.RequestsComplete, new { Id = id, Status = ScanStatus.Done });

    public async Task SetRequestErrorAsync(Guid id)
        => await ExecuteAsync(ScanSql.RequestsSetError, new { Id = id, Status = ScanStatus.Error });

    public async Task DeleteRequestAsync(Guid id)
        => await ExecuteAsync(ScanSql.RequestsDelete, new { Id = id });

    public async Task<DataTable> GetRecentRequestsAsync(int take)
        => await QueryDataTableAsync(ScanSql.RequestsGetRecent, new { Take = take <= 0 ? 50 : take });

    public async Task<DataTable> GetRequestsListAsync()
        => await QueryDataTableAsync(ScanSql.RequestsGetList);

    // ───────────────────────── Images (جدول اصلی پروژه) ─────────────────────────

    /// <summary>
    /// ذخیره یک صفحه اسکن در PDDImage.ImagesTable.
    /// فایل اصلی در ImageField و Thumbnail در PDDImage.ImageThumbnails ذخیره می‌شود.
    /// </summary>
    public async Task<decimal> SaveImageAsync(
        Guid requestId,
        string fileName,
        string? contentType,
        byte[] data,
        byte[]? thumbnail,
        int pageNumber)
    {
        var fileType = ResolveFileType(fileName, contentType);
        decimal? fileSizeKb = data.Length > 0 ? Math.Round(data.Length / 1024m, 0) : null;

        var id = await ExecuteScalarAsync<decimal?>(
            ScanSql.ImagesAdd,
            new
            {
                RequestId = requestId,
                Data = data,
                FileName = NormalizeFileName(fileName, pageNumber),
                FileType = fileType,
                Date = PersianDate.Today(),
                ScanTime = PersianDate.NowTime(),
                FileSizeKB = fileSizeKb
            });

        if (id is null || id.Value == 0) return 0;

        if (thumbnail is { Length: > 0 })
        {
            await ExecuteAsync(ScanSql.ThumbnailsAdd, new
            {
                ImageId = id.Value,
                Thumbnail = thumbnail,
                ThumbSizeKB = Math.Round(thumbnail.Length / 1024m, 0)
            });
        }

        await ExecuteAsync(ScanSql.ScanRequestImagesAdd, new
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            ImageId = id.Value,
            PageNumber = pageNumber
        });

        return id.Value;
    }

    public async Task<byte[]?> GetImageDataAsync(decimal id)
        => await ExecuteScalarAsync<byte[]?>(ScanSql.ImagesGetData, new { Id = id });

    public async Task<byte[]?> GetImageThumbnailAsync(decimal id)
        => await ExecuteScalarAsync<byte[]?>(ScanSql.ImagesGetThumbnail, new { Id = id });

    /// <summary>حذف نرم از جدول اصلی (ISDELETED=1) + حذف Thumbnail و ارتباط درخواست.</summary>
    public async Task DeleteImageAsync(decimal id)
        => await ExecuteAsync(ScanSql.ImagesDelete, new { Id = id });

    public async Task UpdateImageAsync(decimal id, byte[] data, byte[]? thumbnail)
    {
        decimal? fileSizeKb = data.Length > 0 ? Math.Round(data.Length / 1024m, 0) : null;
        await ExecuteAsync(ScanSql.ImagesUpdate, new
        {
            Id = id,
            Data = data,
            FileSizeKB = fileSizeKb,
            Thumbnail = thumbnail,
            ThumbSizeKB = thumbnail is { Length: > 0 } ? Math.Round(thumbnail.Length / 1024m, 0) : (decimal?)null
        });
    }

    // ───────────────────────── Gallery ─────────────────────────

    public async Task<(DataTable data, int total)> GetGalleryAsync(
        int skip,
        int take,
        decimal? groupId,
        string? relationCode,
        string? picType,
        string? softwareCode)
    {
        var whereParts = new List<string> { "i.ISDELETED = 0" };
        var p = new DynamicParameters();
        p.Add("@Skip", skip);
        p.Add("@Take", take);

        if (groupId.HasValue)
        {
            whereParts.Add("i.ImageGroupID = @GroupId");
            p.Add("@GroupId", groupId.Value);
        }
        // گالری فقط بر اساس SoftwareCode / PicType / RelationCode فیلتر می‌شود
        // (بدون فیلتر دستگاه و بدون فیلتر UserCode)
        AddCodeFilter(whereParts, p, "SoftwareCode", softwareCode);
        AddCodeFilter(whereParts, p, "PicType", picType);
        AddCodeFilter(whereParts, p, "RelationCode", relationCode);

        var where = "WHERE " + string.Join(" AND ", whereParts);

        var countSql = string.Format(ScanSql.GalleryCount, where);
        var dataSql = string.Format(ScanSql.GalleryPage, where);

        using var conn = _factory.CreateConnection();
        int total = await conn.ExecuteScalarAsync<int>(countSql, p);
        await conn.OpenAsync();
        using var reader = await conn.ExecuteReaderAsync(dataSql, p);
        var dt = new DataTable();
        dt.Load(reader);
        return (dt, total);
    }

    // ───────────────────────── Groups (1 به n) ─────────────────────────

    public async Task<DataTable> GetGroupsAsync()
        => await QueryDataTableAsync(ScanSql.GroupsGetAll);

    public async Task<DataRow?> GetGroupByNameAsync(string name, string softwareCode)
    {
        var dt = await QueryDataTableAsync(ScanSql.GroupsGetByName, new { Name = name.Trim(), SoftwareCode = softwareCode });
        return dt.Rows.Count > 0 ? dt.Rows[0] : null;
    }

    public async Task<decimal> EnsureGroupAsync(string name, string softwareCode)
    {
        name = name.Trim();
        var existing = await GetGroupByNameAsync(name, softwareCode);
        if (existing != null) return Convert.ToDecimal(existing["ID"]);

        var id = await ExecuteScalarAsync<decimal?>(
            ScanSql.GroupsCreate,
            new { Name = name, SoftwareCode = softwareCode });
        return id ?? 0;
    }

    public async Task DeleteGroupAsync(decimal id)
        => await ExecuteAsync(ScanSql.GroupsDelete, new { Id = id });

    /// <summary>تخصیص تصویر به یک گروه (ارتباط 1 به n).</summary>
    public async Task AssignImageToGroupAsync(decimal imageId, decimal groupId)
        => await ExecuteAsync(ScanSql.GroupSet, new { ImageId = imageId, GroupId = groupId });

    /// <summary>حذف تصویر از گروه (ImageGroupID = NULL).</summary>
    public async Task RemoveImageFromGroupAsync(decimal imageId, decimal groupId)
        => await ExecuteAsync(ScanSql.GroupClear, new { ImageId = imageId, GroupId = groupId });

    // ───────────────────────── Helpers ─────────────────────────

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    private static string NormalizeFileName(string? fileName, int pageNumber)
        => string.IsNullOrWhiteSpace(fileName) ? $"scan_p{pageNumber}.jpg" : fileName.Trim();

    private static string ResolveFileType(string? fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var ct = contentType.ToLowerInvariant();
            if (ct.Contains("jpeg") || ct.Contains("jpg")) return "jpg";
            if (ct.Contains("png")) return "png";
            if (ct.Contains("tiff") || ct.Contains("tif")) return "tif";
            if (ct.Contains("bmp")) return "bmp";
        }
        var ext = Path.GetExtension(fileName)?.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return "jpg";
        return ext.Length > 5 ? ext[..5] : ext;
    }

    private static void AddCodeFilter(List<string> whereParts, DynamicParameters parameters, string columnName, string? value)
    {
        var normalized = NormalizeCode(value);
        if (normalized is null) return;

        whereParts.Add($"i.{columnName} = @{columnName}");
        parameters.Add($"@{columnName}", normalized);
    }

    private async Task<DataTable> QueryDataTableAsync(string sql, object? param = null)
    {
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.OpenAsync();
            using var reader = await conn.ExecuteReaderAsync(sql, param);
            var dt = new DataTable();
            dt.Load(reader);
            return dt;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "QueryDataTableAsync failed for: {Sql}", sql);
            return new DataTable();
        }
    }

    private async Task<int> ExecuteAsync(string sql, object? param = null)
    {
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.ExecuteAsync(sql, param);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ExecuteAsync failed for: {Sql}", sql);
            return 0;
        }
    }

    private async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.ExecuteScalarAsync<T>(sql, param);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ExecuteScalarAsync failed for: {Sql}", sql);
            return default;
        }
    }
}
