using Microsoft.AspNetCore.SignalR;
using ScanSystem.Shared;
using ScanSystem.Shared.Entities;
using ScanSystem.Web.Services;

namespace ScanSystem.Web.Hubs;

/// <summary>
/// مرکز ارتباط بین Blazor UI و WPF Agentها از طریق SignalR.
/// Agent با نام ماشین خود ثبت می‌شود و درخواست اسکن فقط به همان Agent ارسال می‌شود.
/// هر صفحه به‌محض اسکن از طریق UploadPage آپلود می‌شود (Streaming) — نه بعد از اتمام کل scan.
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

    /// <summary>Agent خودش را با نام ماشین ثبت می‌کند (شناسه یکتا = نام کامپیوتر).</summary>
    public async Task RegisterAgent(string machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        machineName = machineName.Trim();

        await _service.UpsertAgentAsync(machineName, Context.ConnectionId);
        _logger.LogInformation("Agent registered: {Machine}", machineName);

        // اعلام تغییر وضعیت Agentها به همه کلاینت‌ها (UI مدیریت + گالری).
        await Clients.All.SendAsync("AgentStatusChanged", machineName, true);
        await Clients.All.SendAsync("AgentsChanged");
    }

    /// <summary>لیست Agentها را برای فراخوان برمی‌گرداند.</summary>
    public async Task GetAgentsAsync()
    {
        var agents = await _service.GetAgentsAsync();
        await Clients.Caller.SendAsync("AgentsList", agents);
    }

    /// <summary>
    /// UI یک درخواست اسکن برای یک کامپیوتر مشخص می‌فرستد؛ ثبت در DB و ارسال فقط به همان Agent.
    /// isMultiPage: اگر true باشد، Agent تا پایان feeder اسکن می‌کند و صفحات را به‌صورت استریم آپلود می‌کند.
    /// </summary>
    public async Task RequestScan(string machineName, bool isMultiPage = false)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        machineName = machineName.Trim();

        var id = await _service.CreateRequestAsync(machineName, isMultiPage);
        _logger.LogInformation("Scan requested {Id} for {Machine} (multiPage={Mp})", id, machineName, isMultiPage);

        var connectionId = _connections.GetConnectionId(machineName);
        if (connectionId is null)
        {
            // Agent آفلاین است؛ درخواست ثبت می‌شود اما وضعیت خطا می‌گیرد.
            await _service.SetErrorAsync(id, $"Agent '{machineName}' آنلاین نیست.");
            await Clients.All.SendAsync("RequestsChanged");
            return;
        }

        // ارسال درخواست فقط به Agent هدف.
        await Clients.Client(connectionId).SendAsync("ScanRequested", machineName, id, isMultiPage);
        await Clients.All.SendAsync("RequestsChanged");
    }

    /// <summary>Agent شروع پردازش را اعلام می‌کند.</summary>
    public async Task StartProcessing(Guid id)
    {
        await _service.SetProcessingAsync(id);
        await Clients.All.SendAsync("AgentStatusChanged", string.Empty, true);
        await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Processing);
        await Clients.All.SendAsync("RequestsChanged");
    }

    /// <summary>
    /// Agent یک صفحه اسکن‌شده را آپلود می‌کند (به‌محض آماده شدن، نه در پایان کل scan).
    /// pageNumber از ۱ شروع می‌شود. برای multi-page چند بار با افزایش pageNumber فراخوانی می‌شود.
    /// </summary>
    public async Task UploadPage(Guid id, string fileName, string contentType, byte[] data, int pageNumber)
    {
        try
        {
            var image = await _service.SavePageAsync(id, fileName, data, pageNumber);
            _logger.LogInformation("Page {Page} uploaded for request {Id} ({Size} bytes)",
                pageNumber, id, data.Length);

            // اطلاع به UI گالری که یک تصویر جدید آماده است (Lazy Reload توسط کلاینت).
            await Clients.All.SendAsync("PageUploaded", id, image.Id, pageNumber);
            await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Processing);
            await Clients.All.SendAsync("RequestsChanged");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadPage failed for request {Id}", id);
        }
    }

    /// <summary>Agent پایان موفق scan را اعلام می‌کند (پس از آپلود آخرین صفحه).</summary>
    public async Task CompleteScan(Guid id)
    {
        await _service.SetCompletedAsync(id);
        await Clients.All.SendAsync("ScanCompleted", id);
        await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Done);
        await Clients.All.SendAsync("RequestsChanged");
    }

    /// <summary>Agent خطا را گزارش می‌کند.</summary>
    public async Task ReportError(Guid id, string message)
    {
        await _service.SetErrorAsync(id, message);
        await Clients.All.SendAsync("StatusChanged", id, ScanStatus.Error);
        await Clients.All.SendAsync("RequestsChanged");
    }

    /// <summary>تخصیص یک تصویر به یک گروه با درگ و دراپ.</summary>
    public async Task AssignGroup(Guid imageId, string? groupName)
    {
        if (!string.IsNullOrWhiteSpace(groupName))
            await _service.AssignGroupAsync(imageId, groupName!);

        await Clients.All.SendAsync("GalleryChanged");
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("AgentConnected", Context.ConnectionId);
        // اطلاع فوری از لیست Agentها به فراخوان جدید (UI).
        await Clients.Caller.SendAsync("AgentsList", await _service.GetAgentsAsync());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        // اگر Agent قطع شده، نام ماشین را پیدا و آفلاین می‌کنیم.
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
