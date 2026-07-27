using Dapper;
using Microsoft.Extensions.Logging;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Shared.Repositories;

/// <summary>
/// توجه: ConnectionId در schema جدول Agents وجود ندارد.
/// نگاشت آن در حافظه (SignalR Hub) قابل دسترسی است؛ اینجا فقط وضعیت آنلاین/آفلاین را نگه می‌داریم.
/// </summary>
public class AgentRepository : IAgentRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<AgentRepository>? _logger;

    public AgentRepository(IDbConnectionFactory factory, ILogger<AgentRepository>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<Agent?> GetByMachineNameAsync(string machineName)
    {
        const string sql = @"
            SELECT Id, MachineName, IsOnline, LastSeen
            FROM dbo.Agents
            WHERE MachineName = @MachineName;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<Agent>(sql, new { MachineName = machineName });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    public async Task<Agent?> GetByIdAsync(Guid id)
    {
        const string sql = @"
            SELECT Id, MachineName, IsOnline, LastSeen
            FROM dbo.Agents
            WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<Agent>(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return null; }
    }

    public async Task<List<AgentDto>> GetAllAsync()
    {
        const string sql = @"
            SELECT Id, MachineName, IsOnline, LastSeen
            FROM dbo.Agents
            ORDER BY IsOnline DESC, LastSeen DESC;";
        try
        {
            using var conn = _factory.CreateConnection();
            var list = await conn.QueryAsync<AgentDto>(sql);
            return list.AsList();
        }
        catch (Exception ex) { LogErr(ex); return new List<AgentDto>(); }
    }

    public async Task UpsertAsync(string machineName, bool isOnline, string? connectionId)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE MachineName = @MachineName)
                INSERT INTO dbo.Agents (Id, MachineName, IsOnline, LastSeen)
                VALUES (@Id, @MachineName, @IsOnline, SYSDATETIME());
            ELSE
                UPDATE dbo.Agents
                SET IsOnline = @IsOnline, LastSeen = SYSDATETIME()
                WHERE MachineName = @MachineName;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new
            {
                Id = Guid.NewGuid(),
                MachineName = machineName,
                IsOnline = isOnline
            });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    public async Task SetOnlineAsync(Guid id, string connectionId)
    {
        const string sql = @"
            UPDATE dbo.Agents
            SET IsOnline = 1, LastSeen = SYSDATETIME()
            WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    /// <summary>علامت‌گذاری Agent مشخص (با Id) به‌صورت آفلاین؛ LastSeen به‌روز می‌شود.</summary>
    public async Task SetOfflineAsync(Guid id)
    {
        const string sql = @"
            UPDATE dbo.Agents
            SET IsOnline = 0, LastSeen = SYSDATETIME()
            WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            await conn.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); }
    }

    public async Task<int> DeleteAsync(Guid id)
    {
        const string sql = "DELETE FROM dbo.Agents WHERE Id = @Id;";
        try
        {
            using var conn = _factory.CreateConnection();
            return await conn.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex) { LogErr(ex); return 0; }
    }

    private void LogErr(Exception ex) => _logger?.LogError(ex, "AgentRepository error");
}
