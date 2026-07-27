using ScanSystem.Shared;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;
using ScanSystem.Shared.Repositories;

namespace ScanSystem.Web.Services;

/// <summary>
/// پیاده‌سازی <see cref="IScanService"/> بر پایه Repositoryهای Dapper.
/// هیچ EF Core/DbContext استفاده نمی‌شود. نگاشت اتصال SignalR از طریق
/// <see cref="AgentConnectionRegistry"/> در حافظه انجام می‌شود (طبق اسکیمای ضروری،
/// ConnectionId در جدول Agents نگه‌داری نمی‌شود).
/// </summary>
public class ScanService : IScanService
{
    private readonly IAgentRepository _agents;
    private readonly IScanRequestRepository _requests;
    private readonly IImageRepository _images;
    private readonly IImageGroupRepository _groups;
    private readonly AgentConnectionRegistry _connections;
    private readonly ILogger<ScanService> _logger;

    public ScanService(
        IAgentRepository agents,
        IScanRequestRepository requests,
        IImageRepository images,
        IImageGroupRepository groups,
        AgentConnectionRegistry connections,
        ILogger<ScanService> logger)
    {
        _agents = agents;
        _requests = requests;
        _images = images;
        _groups = groups;
        _connections = connections;
        _logger = logger;
    }

    // ───────────────────────── Agentها ─────────────────────────

    public async Task<Guid> UpsertAgentAsync(string machineName, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return Guid.Empty;
        machineName = machineName.Trim();

        try
        {
            await _agents.UpsertAsync(machineName, isOnline: true, connectionId);
            // ثبت نگاشت در حافظه برای ارسال هدفمند SignalR
            _connections.Register(machineName, connectionId);

            var agent = await _agents.GetByMachineNameAsync(machineName);
            return agent?.Id ?? Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpsertAgentAsync failed for {Machine}", machineName);
            return Guid.Empty;
        }
    }

    public async Task SetAgentOfflineByMachineAsync(string machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return;
        try
        {
            var agent = await _agents.GetByMachineNameAsync(machineName);
            if (agent is null) return;
            await _agents.SetOfflineAsync(agent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetAgentOfflineByMachineAsync failed for {Machine}", machineName);
        }
    }

    public async Task<List<AgentDto>> GetAgentsAsync()
        => await _agents.GetAllAsync();

    public async Task<int> DeleteAgentAsync(Guid id)
    {
        try
        {
            var agent = await _agents.GetByIdAsync(id);
            if (agent is not null)
                _connections.UnregisterByMachine(agent.MachineName); // نگاشت حافظه هم پاک شود
            return await _agents.DeleteAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteAgentAsync failed for {Id}", id);
            return 0;
        }
    }

    // ───────────────────────── درخواست‌های اسکن ─────────────────────────

    public async Task<Guid> CreateRequestAsync(string machineName, bool isMultiPage)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return Guid.Empty;
        machineName = machineName.Trim();

        try
        {
            var agent = await _agents.GetByMachineNameAsync(machineName);
            if (agent is null)
            {
                // Agent هنوز ثبت نشده؛ آن را ایجاد می‌کنیم (آفلاین) تا محدودیت FK رعایت شود.
                await _agents.UpsertAsync(machineName, isOnline: false, connectionId: null);
                agent = await _agents.GetByMachineNameAsync(machineName);
            }
            if (agent is null) return Guid.Empty;

            var req = await _requests.CreateAsync(agent.Id, isMultiPage);
            return req.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateRequestAsync failed for {Machine}", machineName);
            return Guid.Empty;
        }
    }

    public async Task SetProcessingAsync(Guid id) => await _requests.SetStatusAsync(id, ScanStatus.Processing);

    public async Task SetCompletedAsync(Guid id) => await _requests.SetCompletedAsync(id, ScanStatus.Done);

    public async Task SetErrorAsync(Guid id, string error)
    {
        _logger.LogError("Scan {Id} failed: {Error}", id, error);
        await _requests.SetErrorAsync(id, error);
    }

    public async Task DeleteRequestAsync(Guid id) => await _requests.DeleteAsync(id);

    public async Task<(List<ScanRequestDto> data, int recordsTotal, int recordsFiltered)> GetRequestsDataAsync(
        int start, int length, string? search, int orderColumnIndex, string orderDir)
        => await _requests.GetDataAsync(start, length, search, orderColumnIndex, orderDir);

    public async Task<List<ScanRequestDto>> GetRecentRequestsAsync(int take)
        => await _requests.GetRecentAsync(take);

    // ───────────────────────── تصاویر / گالری ─────────────────────────

    public async Task<ScanImage> SavePageAsync(Guid requestId, string fileName, byte[] data, int pageNumber)
    {
        // ساخت Thumbnail در سمت سرور (ویندوز) — کاهش پهنای باند گالری.
        byte[]? thumbnail = ThumbnailGenerator.Generate(data);

        try
        {
            return await _images.AddPageAsync(requestId, fileName, data, thumbnail, pageNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SavePageAsync failed for request {Id}", requestId);
            throw;
        }
    }

    public async Task<(List<ImageGalleryItemDto> items, int total)> GetGalleryPageAsync(
        int skip, int take, Guid? groupId, string? machineName)
        => await _images.GetGalleryPageAsync(skip, take, groupId, machineName);

    public async Task<ImageDownloadDto?> GetImageDownloadAsync(Guid id)
    {
        var img = await _images.GetByIdAsync(id);
        if (img is null) return null;
        return new ImageDownloadDto
        {
            Id = img.Id,
            RequestId = img.RequestId,
            FileName = img.FileName,
            Data = img.Data,
            PageNumber = img.PageNumber
        };
    }

    public async Task<byte[]?> GetImageThumbnailAsync(Guid id) => await _images.GetThumbnailAsync(id);

    public async Task<int> DeleteImageAsync(Guid id) => await _images.DeleteAsync(id);

    public async Task UpdateImageAsync(Guid id, byte[] data, byte[]? thumbnail)
    {
        // اگر Thumbnail ارائه نشده، از داده‌ی تصویر جدید ساخته می‌شود.
        thumbnail ??= ThumbnailGenerator.Generate(data);
        await _images.UpdateDataAsync(id, data, thumbnail);
    }

    public async Task<List<ImageDownloadDto>> GetImagesByRequestAsync(Guid requestId)
    {
        var list = await _images.GetByRequestAsync(requestId);
        return list.Select(img => new ImageDownloadDto
        {
            Id = img.Id,
            RequestId = img.RequestId,
            FileName = img.FileName,
            Data = img.Data,
            PageNumber = img.PageNumber
        }).ToList();
    }

    public async Task<List<ImageDownloadDto>> GetImagesByIdsAsync(IEnumerable<Guid> ids)
    {
        var result = new List<ImageDownloadDto>();
        foreach (var id in ids.Distinct())
        {
            var img = await _images.GetByIdAsync(id);
            if (img is null) continue;
            result.Add(new ImageDownloadDto
            {
                Id = img.Id,
                RequestId = img.RequestId,
                FileName = img.FileName,
                Data = img.Data,
                PageNumber = img.PageNumber
            });
        }
        return result;
    }

    // ───────────────────────── گروه‌ها ─────────────────────────

    public async Task<List<ImageGroup>> GetGroupsAsync() => await _groups.GetAllAsync();

    public async Task<ImageGroup> CreateGroupAsync(string name) => await _groups.CreateAsync(name);

    public async Task<int> DeleteGroupAsync(Guid id) => await _groups.DeleteAsync(id);

    public async Task AssignGroupAsync(Guid imageId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;
        groupName = groupName.Trim();
        try
        {
            var group = await _groups.CreateAsync(groupName); // اگر موجود باشد همان را برمی‌گرداند
            await _groups.AssignImageAsync(imageId, group.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AssignGroupAsync failed for image {Image}", imageId);
        }
    }

    public async Task UnassignImageAsync(Guid imageId, Guid groupId)
        => await _groups.UnassignImageAsync(imageId, groupId);

    public async Task<List<ImageGroup>> GetGroupsForImageAsync(Guid imageId)
        => await _groups.GetGroupsForImageAsync(imageId);
}
