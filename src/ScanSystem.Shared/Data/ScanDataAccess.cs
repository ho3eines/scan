using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using ScanSystem.Shared;

namespace ScanSystem.Shared.Data;

/// <summary>
/// لایه دسترسی یکپارچه به داده‌ها با DataTable/DataRow.
/// همه کوئری‌ها از <see cref="ScanSql"/> خوانده می‌شوند.
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

    public async Task<Guid> CreateRequestAsync(Guid agentId, bool isMultiPage, string? relationCode, string? inquiryCode, string? softwareCode, string? fullName = null)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(ScanSql.RequestsCreate, new
        {
            Id = id,
            AgentId = agentId,
            Status = ScanStatus.Pending,
            IsMultiPage = isMultiPage,
            RelationCode = NormalizeCode(relationCode),
            InquiryCode = NormalizeCode(inquiryCode),
            SoftwareCode = NormalizeCode(softwareCode),
            FullName = NormalizeString(fullName)
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

    // ───────────────────────── Images ─────────────────────────

    public async Task<Guid> SaveImageAsync(Guid requestId, string fileName, byte[] data, byte[]? thumbnail, int pageNumber)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(ScanSql.ImagesAdd, new
        {
            Id = id,
            RequestId = requestId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? $"page_{pageNumber}.jpg" : fileName,
            Data = data,
            Thumbnail = thumbnail,
            PageNumber = pageNumber
        });
        return id;
    }

    public async Task<byte[]?> GetImageDataAsync(Guid id)
        => await ExecuteScalarAsync<byte[]?>(ScanSql.ImagesGetData, new { Id = id });

    public async Task<byte[]?> GetImageThumbnailAsync(Guid id)
        => await ExecuteScalarAsync<byte[]?>(ScanSql.ImagesGetThumbnail, new { Id = id });

    public async Task DeleteImageAsync(Guid id)
        => await ExecuteAsync(ScanSql.ImagesDelete, new { Id = id });

    public async Task UpdateImageAsync(Guid id, byte[] data, byte[]? thumbnail)
        => await ExecuteAsync(ScanSql.ImagesUpdate, new { Id = id, Data = data, Thumbnail = thumbnail });

    // ───────────────────────── Gallery ─────────────────────────

    public async Task<(DataTable data, int total)> GetGalleryAsync(
        int skip,
        int take,
        Guid? groupId,
        string? machineName,
        string? relationCode,
        string? inquiryCode,
        string? softwareCode)
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
        AddCodeFilter(whereParts, p, "RelationCode", relationCode);
        AddCodeFilter(whereParts, p, "InquiryCode", inquiryCode);
        AddCodeFilter(whereParts, p, "SoftwareCode", softwareCode);

        var where = whereParts.Count > 0 ? "WHERE " + string.Join(" AND ", whereParts) : "";

        var countSql = string.Format(ScanSql.GalleryCount, where);
        var dataSql = string.Format(ScanSql.GalleryPage, ScanSql.GalleryGroupsSubquery, where);

        using var conn = _factory.CreateConnection();
        int total = await conn.ExecuteScalarAsync<int>(countSql, p);
        await conn.OpenAsync();
        using var reader = await conn.ExecuteReaderAsync(dataSql, p);
        var dt = new DataTable();
        dt.Load(reader);
        return (dt, total);
    }

    // ───────────────────────── Groups ─────────────────────────

    public async Task<DataTable> GetGroupsAsync()
        => await QueryDataTableAsync(ScanSql.GroupsGetAll);

    public async Task<DataRow?> GetGroupByNameAsync(string name)
    {
        var dt = await QueryDataTableAsync(ScanSql.GroupsGetByName, new { Name = name.Trim() });
        return dt.Rows.Count > 0 ? dt.Rows[0] : null;
    }

    public async Task<Guid> EnsureGroupAsync(string name)
    {
        name = name.Trim();
        var existing = await GetGroupByNameAsync(name);
        if (existing != null) return (Guid)existing["Id"];

        var id = Guid.NewGuid();
        await ExecuteAsync(ScanSql.GroupsCreate, new { Id = id, Name = name });
        return id;
    }

    public async Task DeleteGroupAsync(Guid id)
        => await ExecuteAsync(ScanSql.GroupsDelete, new { Id = id });

    public async Task AssignImageToGroupAsync(Guid imageId, Guid groupId)
        => await ExecuteAsync(ScanSql.GroupItemsAssign, new { Id = Guid.NewGuid(), ImageId = imageId, GroupId = groupId });

    public async Task RemoveImageFromGroupAsync(Guid imageId, Guid groupId)
        => await ExecuteAsync(ScanSql.GroupItemsRemove, new { ImageId = imageId, GroupId = groupId });

    // ───────────────────────── Helpers ─────────────────────────

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    private static string? NormalizeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
