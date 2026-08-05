using System.Data;
using ScanSystem.Shared;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Web.Services;

public class ScanService : IScanService
{
    /// <summary>SoftwareCode پیش‌فرض برای گروه‌هایی که از داخل سیستم اسکن ساخته می‌شوند.</summary>
    public const string DefaultGroupSoftwareCode = "SCAN";

    private readonly ScanDataAccess _db;
    private readonly AgentConnectionRegistry _connections;
    private readonly ILogger<ScanService> _logger;

    public ScanService(ScanDataAccess db, AgentConnectionRegistry connections, ILogger<ScanService> logger)
    {
        _db = db;
        _connections = connections;
        _logger = logger;
    }

    // ───────────────────────── Agentها ─────────────────────────

    public async Task UpsertAgentAsync(string machineName, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        machineName = machineName.Trim();

        await _db.UpsertAgentAsync(machineName, isOnline: true);
        _connections.Register(machineName, connectionId);
        _logger.LogInformation("Agent registered: {Machine} ({ConnectionId})", machineName, connectionId);
    }

    public async Task SetAgentOfflineByMachineAsync(string machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        var agent = await _db.GetAgentByMachineNameAsync(machineName);
        if (agent is null) return;
        await _db.SetAgentOfflineAsync((Guid)agent["Id"]);
    }

    public async Task<List<AgentDto>> GetAgentsAsync()
    {
        var dt = await _db.GetAgentsAsync();
        var list = new List<AgentDto>(dt.Rows.Count);
        foreach (DataRow row in dt.Rows)
        {
            var machine = Str(row, "MachineName");
            list.Add(new AgentDto
            {
                Id = GuidVal(row, "Id"),
                MachineName = machine,
                IsOnline = Bool(row, "IsOnline") && _connections.IsOnline(machine),
                LastSeen = DateVal(row, "LastSeen")
            });
        }
        return list;
    }

    public async Task<DataTable> GetAgentsDataTableAsync()
    {
        var dt = await _db.GetAgentsAsync();
        // ستون IsOnline را با وضعیت واقعی SignalR ترکیب می‌کنیم.
        foreach (DataRow row in dt.Rows)
        {
            var machine = Str(row, "MachineName");
            row["IsOnline"] = Bool(row, "IsOnline") && _connections.IsOnline(machine);
        }
        return dt;
    }

    public async Task<int> DeleteAgentAsync(Guid id)
    {
        var agent = await _db.GetAgentByIdAsync(id);
        if (agent is not null)
            _connections.UnregisterByMachine(Str(agent, "MachineName"));
        return await _db.DeleteAgentAsync(id);
    }

    // ───────────────────────── درخواست‌های اسکن ─────────────────────────

    public async Task<Guid> CreateRequestAsync(string machineName, bool isMultiPage, string? relationCode, string? picType, string? softwareCode, string? userCode)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return Guid.Empty;
        machineName = machineName.Trim();

        var agent = await _db.GetAgentByMachineNameAsync(machineName);
        if (agent is null)
        {
            await _db.UpsertAgentAsync(machineName, isOnline: false);
            agent = await _db.GetAgentByMachineNameAsync(machineName);
        }
        if (agent is null) return Guid.Empty;

        return await _db.CreateRequestAsync((Guid)agent["Id"], isMultiPage, relationCode, picType, softwareCode, userCode);
    }

    public async Task SetProcessingAsync(Guid id) => await _db.SetRequestStatusAsync(id, ScanStatus.Processing);
    public async Task SetCompletedAsync(Guid id) => await _db.CompleteRequestAsync(id);

    public async Task SetErrorAsync(Guid id, string error)
    {
        _logger.LogError("Scan {Id} failed: {Error}", id, error);
        await _db.SetRequestErrorAsync(id);
    }

    public async Task DeleteRequestAsync(Guid id) => await _db.DeleteRequestAsync(id);

    public async Task<DataTable> GetRecentRequestsDataTableAsync(int take)
        => await _db.GetRecentRequestsAsync(take);

    public async Task<DataTable> GetRequestsListAsync()
        => await _db.GetRequestsListAsync();

    // ───────────────────────── تصاویر / گالری ─────────────────────────

    public async Task<decimal> SavePageAsync(Guid requestId, string fileName, string? contentType, byte[] data, int pageNumber)
    {
        var thumbnail = ThumbnailGenerator.Generate(data);
        return await _db.SaveImageAsync(requestId, fileName, contentType, data, thumbnail, pageNumber);
    }

    public async Task<(DataTable data, int total)> GetGalleryPageAsync(
        int skip,
        int take,
        decimal? groupId,
        string? relationCode,
        string? picType,
        string? userCode,
        string? softwareCode)
        => await _db.GetGalleryAsync(skip, take, groupId, relationCode, picType, userCode, softwareCode);

    public async Task<byte[]?> GetImageDataAsync(decimal id) => await _db.GetImageDataAsync(id);
    public async Task<byte[]?> GetImageThumbnailAsync(decimal id) => await _db.GetImageThumbnailAsync(id);
    public async Task DeleteImageAsync(decimal id) => await _db.DeleteImageAsync(id);

    public async Task UpdateImageAsync(decimal id, byte[] data)
    {
        var thumbnail = ThumbnailGenerator.Generate(data);
        await _db.UpdateImageAsync(id, data, thumbnail);
    }

    // ───────────────────────── گروه‌ها (1 به n) ─────────────────────────

    public async Task<DataTable> GetGroupsDataTableAsync() => await _db.GetGroupsAsync();

    public async Task<decimal> EnsureGroupAsync(string name, string? softwareCode)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var sw = NormalizeGroupSoftwareCode(softwareCode);
        return await _db.EnsureGroupAsync(name.Trim(), sw);
    }

    public async Task DeleteGroupAsync(decimal id) => await _db.DeleteGroupAsync(id);

    public async Task AssignImageToGroupAsync(decimal imageId, string groupName, string? softwareCode)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;
        var sw = NormalizeGroupSoftwareCode(softwareCode);
        var groupId = await _db.EnsureGroupAsync(groupName.Trim(), sw);
        if (groupId == 0) return;
        await _db.AssignImageToGroupAsync(imageId, groupId);
    }

    public async Task RemoveImageFromGroupAsync(decimal imageId, decimal groupId)
        => await _db.RemoveImageFromGroupAsync(imageId, groupId);

    // ───────────────────────── Helpers ─────────────────────────

    private static string NormalizeGroupSoftwareCode(string? softwareCode)
    {
        // ستون SoftwareCode جدول BaseImageGroups فقط nvarchar(5) است.
        var sw = string.IsNullOrWhiteSpace(softwareCode) ? DefaultGroupSoftwareCode : softwareCode.Trim();
        return sw.Length > 5 ? sw[..5] : sw;
    }

    private static string Str(DataRow r, string col)
        => r.IsNull(col) ? string.Empty : r[col]?.ToString() ?? string.Empty;

    private static bool Bool(DataRow r, string col)
        => !r.IsNull(col) && Convert.ToBoolean(r[col]);

    private static Guid GuidVal(DataRow r, string col)
        => r.IsNull(col) ? Guid.Empty : (Guid)r[col];

    private static DateTime? DateVal(DataRow r, string col)
        => r.IsNull(col) ? null : (DateTime?)r[col];
}
