using Microsoft.AspNetCore.SignalR;
using ScanSystem.Shared;
using ScanSystem.Shared.Entities;
using ScanSystem.Web.Services;

namespace ScanSystem.Web.Hubs;

/// <summary>
/// مرکز ارتباط زنده بین Blazor UI و WPF Agentها.
/// تمام عملیات (اسکن، گالری، گروه‌بندی، ویرایش تصویر) از طریق این Hub انجام می‌شود.
/// </summary>
public class ScanHub : Hub
{
    private readonly IScanService _service;
    private readonly AgentConnectionRegistry _connections;
    private readonly ILogger<ScanHub> _logger;

    public ScanHub(IScanService service, AgentConnectionRegistry connections, ILogger<ScanHub> logger)
    {
        _service = service;
        _connections = connections;
        _logger = logger;
    }

    // ───────────────────────── Agentها ─────────────────────────

    public async Task RegisterAgent(string machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        machineName = machineName.Trim();

        await _service.UpsertAgentAsync(machineName, Context.ConnectionId);
        _logger.LogInformation("Agent registered: {Machine}", machineName);

        await Clients.All.SendAsync("AgentStatusChanged", machineName, true);
        await Clients.All.SendAsync("AgentsChanged");
    }

    // ───────────────────────── اسکن ─────────────────────────

    public async Task<Guid> RequestScan(string machineName, bool isMultiPage)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return Guid.Empty;
        machineName = machineName.Trim();

        var id = await _service.CreateRequestAsync(machineName, isMultiPage);
        if (id == Guid.Empty) return id;

        _logger.LogInformation("Scan requested {Id} for {Machine} (multiPage={Mp})", id, machineName, isMultiPage);

        var connectionId = _connections.GetConnectionId(machineName);
        if (connectionId is null)
        {
            await _service.SetErrorAsync(id, $"Agent '{machineName}' آنلاین نیست.");
            await Clients.All.SendAsync("RequestsChanged");
            return id;
        }

        await Clients.Client(connectionId).SendAsync("ScanRequested", machineName, id, isMultiPage);
        await Clients.All.SendAsync("RequestsChanged");
        return id;
    }

    public async Task StartProcessing(Guid id)
    {
        await _service.SetProcessingAsync(id);
        await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Processing);
        await Clients.All.SendAsync("RequestsChanged");
    }

    public async Task<Guid> UploadPage(Guid id, string fileName, string contentType, byte[] data, int pageNumber)
    {
        try
        {
            var imageId = await _service.SavePageAsync(id, fileName, data, pageNumber);
            _logger.LogInformation("Page {Page} uploaded for request {Id} ({Size} bytes)", pageNumber, id, data.Length);

            await Clients.All.SendAsync("PageUploaded", id, imageId, pageNumber);
            await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Processing);
            await Clients.All.SendAsync("RequestsChanged");
            return imageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadPage failed for request {Id}", id);
            throw;
        }
    }

    public async Task CompleteScan(Guid id)
    {
        await _service.SetCompletedAsync(id);
        await Clients.All.SendAsync("ScanCompleted", id);
        await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Done);
        await Clients.All.SendAsync("RequestsChanged");
    }

    public async Task ReportError(Guid id, string message)
    {
        await _service.SetErrorAsync(id, message);
        await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Error);
        await Clients.All.SendAsync("RequestsChanged");
    }

    // ───────────────────────── درخواست‌ها ─────────────────────────

    public async Task DeleteRequest(Guid id)
    {
        await _service.DeleteRequestAsync(id);
        await Clients.All.SendAsync("RequestsChanged");
    }

    // ───────────────────────── گالری ─────────────────────────

    public async Task<bool> DeleteImage(Guid id)
    {
        await _service.DeleteImageAsync(id);
        await Clients.All.SendAsync("GalleryChanged");
        return true;
    }

    public async Task<bool> RotateImage(Guid id, int angle)
    {
        if (angle is not (90 or 180 or 270)) return false;
        var data = await _service.GetImageDataAsync(id);
        if (data is null) return false;
        var rotated = ThumbnailGenerator.Rotate(data, angle);
        if (rotated is null) return false;
        await _service.UpdateImageAsync(id, rotated);
        await Clients.All.SendAsync("GalleryChanged");
        return true;
    }

    public async Task<bool> ReplaceImage(Guid id, byte[] data)
    {
        if (data is null || data.Length == 0) return false;
        await _service.UpdateImageAsync(id, data);
        await Clients.All.SendAsync("GalleryChanged");
        return true;
    }

    // ───────────────────────── گروه‌ها ─────────────────────────

    public async Task<Guid> CreateGroup(string name)
    {
        return await _service.EnsureGroupAsync(name);
    }

    public async Task<bool> DeleteGroup(Guid id)
    {
        await _service.DeleteGroupAsync(id);
        await Clients.All.SendAsync("GroupsChanged");
        await Clients.All.SendAsync("GalleryChanged");
        return true;
    }

    public async Task<bool> AssignImageToGroup(Guid imageId, string groupName)
    {
        await _service.AssignImageToGroupAsync(imageId, groupName);
        await Clients.All.SendAsync("GroupsChanged");
        await Clients.All.SendAsync("GalleryChanged");
        return true;
    }

    public async Task<bool> RemoveImageFromGroup(Guid imageId, Guid groupId)
    {
        await _service.RemoveImageFromGroupAsync(imageId, groupId);
        await Clients.All.SendAsync("GalleryChanged");
        return true;
    }

    // ───────────────────────── چرخه عمر ─────────────────────────

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("AgentConnected", Context.ConnectionId);
        await Clients.Caller.SendAsync("AgentsList", await _service.GetAgentsAsync());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        var machine = _connections.Unregister(Context.ConnectionId);
        if (!string.IsNullOrWhiteSpace(machine))
        {
            await _service.SetAgentOfflineByMachineAsync(machine);
            await Clients.All.SendAsync("AgentStatusChanged", machine, false);
        }
        await Clients.All.SendAsync("AgentsChanged");
        await base.OnDisconnectedAsync(exception);
    }
}
