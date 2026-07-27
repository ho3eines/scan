using Dapper;
using Microsoft.Extensions.Logging;
using ScanSystem.Shared;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

public class ScanRequestRepository : IScanRequestRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<ScanRequestRepository>? _logger;

    public ScanRequestRepository(IDbConnectionFactory factory, ILogger<ScanRequestRepository>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<ScanRequest> CreateAsync(Guid agentId, bool isMultiPage)
    {
        const string sql = @"
            INSERT INTO dbo.ScanRequests (Id, AgentId, Status, IsMultiPage, CreatedAt)
            OUTPUT inserted.Id, inserted.AgentId, inserted.Status, inserted.IsMultiPage, inserted.CreatedAt, inserted.CompletedAt
            VALUES (@Id, @AgentId, @Status, @IsMultiPage, SYSDATETIME());";
        var req = new ScanRequest
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Status = ScanStatus.Pending,
            IsMultiPage = isMultiPage
        };
        try
        {
            using var conn = _factory.CreateConnection();
            var inserted = await conn.QuerySingleAsync<ScanRequest>(sql, new
            {
                req.Id, req.AgentId, req.Status, req.IsMultiPage
            });
            return inserted;
        }
        catch (Exception ex)
        {
            LogErr(ex);
            return req;
        }
    }

    public async Task<ScanRequest?> GetByIdAsync(Guid id)
    {
        const string sql = @"
            SELECT Id, AgentId, Status, IsMultiPage, CreatedAt, CompletedAt
            FROM dbo.ScanRequests WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<ScanRequest>(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    public async Task SetStatusAsync(Guid id, string status)
    {
        const string sql = "UPDATE dbo.ScanRequests SET Status = @Status WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Status = status, Id = id });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    public async Task SetCompletedAsync(Guid id, string status)
    {
        const string sql = @"
            UPDATE dbo.ScanRequests
            SET Status = @Status, CompletedAt = SYSDATETIME()
            WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Status = status, Id = id });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    public async Task SetErrorAsync(Guid id, string message)
    {
        const string sql = @"
            UPDATE dbo.ScanRequests
            SET Status = @Status, CompletedAt = SYSDATETIME()
            WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Status = ScanStatus.Error, Id = id });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    public async Task<int> DeleteAsync(Guid id)
    {
        const string sql = "DELETE FROM dbo.ScanRequests WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return 0; }
    }

    /// <summary>
    /// کوئری دینامیک DataTable Server-side: جستجو روی MachineName/Status و مرتب‌سازی بر اساس ستون انتخابی.
    /// همه مقادیر کاربر به‌صورت Parametrized ارسال می‌شوند؛ فقط نام ستون از فهرست سفید انتخاب می‌شود.
    /// </summary>
    public async Task<(List<ScanRequestDto> data, int recordsTotal, int recordsFiltered)> GetDataAsync(
        int start, int length, string? search, int orderColumnIndex, string orderDir)
    {
        var orderColumn = orderColumnIndex switch
        {
            0 => "r.CreatedAt",
            1 => "a.MachineName",
            2 => "r.Status",
            3 => "r.IsMultiPage",
            4 => "r.CompletedAt",
            _ => "r.CreatedAt"
        };
        // مرتب‌سازی فقط ASC/DESC مجاز است
        var dir = string.Equals(orderDir, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";

        const string countAll = "SELECT COUNT(*) FROM dbo.ScanRequests;";

        // کوئری شمارش فیلترشده: با @Search خالی (NULL) همه رکوردها شمرده می‌شوند.
        const string countFiltered = @"
            SELECT COUNT(*)
            FROM dbo.ScanRequests r
            LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
            WHERE (@Search IS NULL OR a.MachineName LIKE @Search OR r.Status LIKE @Search);";

        const string dataSql = @"
            SELECT r.Id, r.AgentId, a.MachineName AS AgentMachineName,
                   r.Status, r.IsMultiPage, r.CreatedAt, r.CompletedAt,
                   ImageCount = (SELECT COUNT(*) FROM dbo.Images i WHERE i.RequestId = r.Id)
            FROM dbo.ScanRequests r
            LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
            WHERE (@Search IS NULL OR a.MachineName LIKE @Search OR r.Status LIKE @Search)
            ORDER BY {orderColumn} {dir}
            OFFSET @Start ROWS FETCH NEXT @Length ROWS ONLY;";

        // {orderColumn}/{dir} فقط از فهرست سفید بالا می‌آیند — تزریق SQL ممکن نیست.
        var finalDataSql = dataSql.Replace("{orderColumn}", orderColumn).Replace("{dir}", dir);

        try
        {
            using var conn = _factory.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@Start", Math.Max(0, start));
            parameters.Add("@Length", length <= 0 ? 10 : length);
            parameters.Add("@Search", string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%");

            int total = await conn.ExecuteScalarAsync<int>(countAll);
            int filtered = await conn.ExecuteScalarAsync<int>(countFiltered, parameters);

            var rows = await conn.QueryAsync<ScanRequestDto>(finalDataSql, parameters);
            return (rows.AsList(), total, filtered);
        }
        catch (Exception ex)
        {
            LogErr(ex);
            return (new List<ScanRequestDto>(), 0, 0);
        }
    }

    /// <summary>آخرین درخواست‌ها به ترتیب تاریخ ایجاد (برای صفحه اسکن/داشبورد).</summary>
    public async Task<List<ScanRequestDto>> GetRecentAsync(int take)
    {
        const string sql = @"
            SELECT TOP (@Take)
                   r.Id, r.AgentId, a.MachineName AS AgentMachineName,
                   r.Status, r.IsMultiPage, r.CreatedAt, r.CompletedAt,
                   ImageCount = (SELECT COUNT(*) FROM dbo.Images i WHERE i.RequestId = r.Id)
            FROM dbo.ScanRequests r
            LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
            ORDER BY r.CreatedAt DESC;";
        try
        {
            using var conn = _factory.CreateConnection();
            var list = await conn.QueryAsync<ScanRequestDto>(sql, new { Take = take <= 0 ? 50 : take });
            return list.AsList();
        }
        catch (Exception ex) { LogErr(ex); return new List<ScanRequestDto>(); }
    }

    private void LogErr(Exception ex) => _logger?.LogError(ex, "ScanRequestRepository error");
}
