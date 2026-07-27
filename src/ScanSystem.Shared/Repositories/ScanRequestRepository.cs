using System.Data;
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
    /// لیست درخواست‌ها برای جدول Blazor — خروجی System.Data.DataTable
    /// (Dapper ExecuteReader + DataTable.Load طبق §6.1).
    /// جستجو روی MachineName/Status، مرتب‌سازی با فهرست سفید ستون‌ها و
    /// صفحه‌بندی با OFFSET/FETCH NEXT — همه در T-SQL و Parametrized.
    /// </summary>
    public async Task<(DataTable data, int recordsTotal, int recordsFiltered)> GetDataTableAsync(
        int page, int pageSize, string? search, int orderColumnIndex, string orderDir)
    {
        // فهرست سفید ستون‌های قابل مرتب‌سازی (جلوگیری از SQL Injection)
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

        const string countFiltered = @"
            SELECT COUNT(*)
            FROM dbo.ScanRequests r
            LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
            WHERE (@Search IS NULL OR a.MachineName LIKE @Search OR r.Status LIKE @Search);";

        const string dataSql = @"
            SELECT r.Id, r.AgentId, a.MachineName,
                   r.Status, r.IsMultiPage, r.CreatedAt, r.CompletedAt,
                   ImageCount = (SELECT COUNT(*) FROM dbo.Images i WHERE i.RequestId = r.Id)
            FROM dbo.ScanRequests r
            LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
            WHERE (@Search IS NULL OR a.MachineName LIKE @Search OR r.Status LIKE @Search)
            ORDER BY {orderColumn} {dir}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        // {orderColumn}/{dir} فقط از فهرست سفید بالا می‌آیند — تزریق SQL ممکن نیست.
        var finalDataSql = dataSql.Replace("{orderColumn}", orderColumn).Replace("{dir}", dir);

        try
        {
            using var conn = _factory.CreateConnection();
            await conn.OpenAsync();

            var parameters = new DynamicParameters();
            parameters.Add("@Offset", Math.Max(0, page) * Math.Max(1, pageSize));
            parameters.Add("@PageSize", pageSize <= 0 ? 10 : pageSize);
            parameters.Add("@Search", string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%");

            int total = await conn.ExecuteScalarAsync<int>(countAll);
            int filtered = await conn.ExecuteScalarAsync<int>(countFiltered, parameters);

            // خروجی جدولی: ExecuteReader + DataTable.Load (الگوی مورد تأیید نیازمندی)
            using var reader = await conn.ExecuteReaderAsync(finalDataSql, parameters);
            var dt = new DataTable();
            dt.Load(reader);

            return (dt, total, filtered);
        }
        catch (Exception ex)
        {
            LogErr(ex);
            return (new DataTable(), 0, 0);
        }
    }

    /// <summary>آخرین درخواست‌ها به ترتیب تاریخ ایجاد — خروجی DataTable.</summary>
    public async Task<DataTable> GetRecentDataTableAsync(int take)
    {
        const string sql = @"
            SELECT TOP (@Take)
                   r.Id, r.AgentId, a.MachineName,
                   r.Status, r.IsMultiPage, r.CreatedAt, r.CompletedAt,
                   ImageCount = (SELECT COUNT(*) FROM dbo.Images i WHERE i.RequestId = r.Id)
            FROM dbo.ScanRequests r
            LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
            ORDER BY r.CreatedAt DESC;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.OpenAsync();
            using var reader = await conn.ExecuteReaderAsync(sql, new { Take = take <= 0 ? 50 : take });
            var dt = new DataTable();
            dt.Load(reader);
            return dt;
        }
        catch (Exception ex) { LogErr(ex); return new DataTable(); }
    }

    private void LogErr(Exception ex) => _logger?.LogError(ex, "ScanRequestRepository error");
}
