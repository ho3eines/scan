using System.Data;
using ScanSystem.Shared;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;

namespace ScanSystem.Web.Services;

public class ScanService : IScanService
{
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

    public async Task<Guid> CreateRequestAsync(string machineName, bool isMultiPage, string? relationCode, string? inquiryCode, string? softwareCode, string? fullName = null)
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

        return await _db.CreateRequestAsync((Guid)agent["Id"], isMultiPage, relationCode, inquiryCode, softwareCode, fullName);
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

    public async Task<Guid> SavePageAsync(Guid requestId, string fileName, byte[] data, int pageNumber)
    {
        var thumbnail = ThumbnailGenerator.Generate(data);
        return await _db.SaveImageAsync(requestId, fileName, data, thumbnail, pageNumber);
    }

    public async Task<(DataTable data, int total)> GetGalleryPageAsync(
        int skip,
        int take,
        Guid? groupId,
        string? machineName,
        string? relationCode,
        string? inquiryCode,
        string? softwareCode)
        => await _db.GetGalleryAsync(skip, take, groupId, machineName, relationCode, inquiryCode, softwareCode);

    public async Task<byte[]?> GetImageDataAsync(Guid id) => await _db.GetImageDataAsync(id);
    public async Task<byte[]?> GetImageThumbnailAsync(Guid id) => await _db.GetImageThumbnailAsync(id);
    public async Task DeleteImageAsync(Guid id) => await _db.DeleteImageAsync(id);

    public async Task UpdateImageAsync(Guid id, byte[] data)
    {
        var thumbnail = ThumbnailGenerator.Generate(data);
        await _db.UpdateImageAsync(id, data, thumbnail);
    }

    // ───────────────────────── گروه‌ها ─────────────────────────

    public async Task<DataTable> GetGroupsDataTableAsync() => await _db.GetGroupsAsync();

    public async Task<Guid> EnsureGroupAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.Empty;
        return await _db.EnsureGroupAsync(name.Trim());
    }

    public async Task DeleteGroupAsync(Guid id) => await _db.DeleteGroupAsync(id);

    public async Task AssignImageToGroupAsync(Guid imageId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;
        var groupId = await EnsureGroupAsync(groupName);
        if (groupId == Guid.Empty) return;
        await _db.AssignImageToGroupAsync(imageId, groupId);
    }

    public async Task RemoveImageFromGroupAsync(Guid imageId, Guid groupId)
        => await _db.RemoveImageFromGroupAsync(imageId, groupId);

    // ───────────────────────── Helpers ─────────────────────────

    private static string Str(DataRow r, string col)
        => r.IsNull(col) ? string.Empty : r[col]?.ToString() ?? string.Empty;

    private static bool Bool(DataRow r, string col)
        => !r.IsNull(col) && Convert.ToBoolean(r[col]);

    private static Guid GuidVal(DataRow r, string col)
        => r.IsNull(col) ? Guid.Empty : (Guid)r[col];

    private static DateTime? DateVal(DataRow r, string col)
        => r.IsNull(col) ? null : (DateTime?)r[col];
}
