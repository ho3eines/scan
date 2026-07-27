using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using ScanSystem.Shared.Entities;
using ScanSystem.Web.Services;

namespace ScanSystem.Web.Controllers;

[ApiController]
[Route("api")]
public class DocumentController : ControllerBase
{
    private readonly IScanService _service;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(IScanService service, ILogger<DocumentController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ───────────────────────── Agentها ─────────────────────────

    /// <summary>لیست Agentها (برای صفحه مدیریت و انتخاب).</summary>
    [HttpGet("agents")]
    public async Task<IActionResult> GetAgents()
    {
        var agents = await _service.GetAgentsAsync();
        return Ok(agents);
    }

    /// <summary>حذف یک Agent (غیرفعال‌سازی در UI).</summary>
    [HttpDelete("agents/{id:guid}")]
    public async Task<IActionResult> DeleteAgent(Guid id)
    {
        // از طریق سرویس: حذف از دیتابیس.
        // NOTE: Agent در صورت اجرای دوباره خودش ثبت می‌شود.
        await _service.SetAgentOfflineByMachineAsync(id.ToString());
        return Ok();
    }

    // ───────────────────────── گروه‌ها ─────────────────────────

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        return Ok(await _service.GetGroupsAsync());
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Name)) return BadRequest("نام گروه الزامی است.");
        var group = await _service.CreateGroupAsync(req.Name);
        return Ok(group);
    }

    [HttpDelete("groups/{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id)
    {
        await _service.DeleteGroupAsync(id);
        return Ok();
    }

    /// <summary>تخصیص یک تصویر به یک گروه (با درگ و دراپ).</summary>
    [HttpPost("images/assignGroup")]
    public async Task<IActionResult> AssignGroup([FromBody] AssignGroupRequest req)
    {
        if (req?.ImageId == Guid.Empty) return BadRequest();
        await _service.AssignGroupAsync(req.ImageId, req.GroupName ?? "");
        return Ok();
    }

    // ───────────────────────── گالری تصاویر ─────────────────────────

    /// <summary>صفحه گالری با Lazy Loading (OFFSET/FETCH).</summary>
    [HttpGet("images")]
    public async Task<IActionResult> GetGallery(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        [FromQuery] Guid? groupId = null,
        [FromQuery] string? machineName = null)
    {
        var (items, total) = await _service.GetGalleryPageAsync(skip, take, groupId, machineName);
        return Ok(new { items, total, skip, take });
    }

    /// <summary>تصویر اصلی (فول‌اسکرین / دانلود).</summary>
    [HttpGet("images/{id:guid}")]
    public async Task<IActionResult> GetImage(Guid id)
    {
        var doc = await _service.GetImageDownloadAsync(id);
        if (doc is null) return NotFound();
        return File(doc.Data, doc.ContentType, doc.FileName);
    }

    /// <summary>Thumbnail تصویر (برای گالری). از HasThumbnail برای fallback استفاده کن.</summary>
    [HttpGet("images/thumb/{id:guid}")]
    public async Task<IActionResult> GetThumbnail(Guid id)
    {
        var data = await _service.GetImageThumbnailAsync(id);
        if (data is null || data.Length == 0)
        {
            // Fallback به تصویر اصلی با کیفیت پایین
            var full = await _service.GetImageDownloadAsync(id);
            if (full is null) return NotFound();
            return File(full.Data, full.ContentType);
        }
        return File(data, "image/jpeg");
    }

    /// <summary>حذف یک تصویر.</summary>
    [HttpDelete("images/{id:guid}")]
    public async Task<IActionResult> DeleteImage(Guid id)
    {
        await _service.DeleteImageAsync(id);
        return Ok();
    }

    /// <summary>تصاویر یک درخواست (با order صفحه).</summary>
    [HttpGet("request/{requestId:guid}/images")]
    public async Task<IActionResult> GetImagesByRequest(Guid requestId)
    {
        var images = await _service.GetImagesByRequestAsync(requestId);
        if (images.Count == 0) return NotFound();
        return Ok(images.Select(i => new { i.Id, i.RequestId, i.FileName, i.PageNumber }));
    }

    /// <summary>بایت‌های خام یک تصویر مشخص.</summary>
    [HttpGet("request/{requestId:guid}/page/{pageNumber:int}")]
    public async Task<IActionResult> GetRequestPage(Guid requestId, int pageNumber)
    {
        var images = await _service.GetImagesByRequestAsync(requestId);
        var img = images.FirstOrDefault(i => i.PageNumber == pageNumber)
                  ?? images.FirstOrDefault();
        if (img is null) return NotFound();
        return File(img.Data, img.ContentType, img.FileName);
    }

    // ───────────────────────── دانلود دسته‌ای ZIP ─────────────────────────

    /// <summary>دانلود چند تصویر به‌صورت یک فایل ZIP.</summary>
    [HttpPost("zip")]
    public async Task<IActionResult> Zip([FromBody] ZipRequest req)
    {
        if (req?.Ids is null || req.Ids.Count == 0)
            return BadRequest("هیچ تصویری انتخاب نشده است.");

        var images = await _service.GetImagesByIdsAsync(req.Ids);
        if (images.Count == 0) return NotFound();

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new HashSet<string>();
            foreach (var img in images)
            {
                var name = EnsureUniqueFileName(img.FileName ?? $"page_{img.PageNumber}.jpg", usedNames);
                var entry = archive.CreateEntry(name);
                await using var entryStream = entry.Open();
                entryStream.Write(img.Data, 0, img.Data.Length);
            }
        }

        stream.Position = 0;
        return File(stream, "application/zip", $"scans_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
    }

    private static string EnsureUniqueFileName(string name, HashSet<string> used)
    {
        if (string.IsNullOrEmpty(name)) name = "scan.bin";
        if (!used.Contains(name))
        {
            used.Add(name);
            return name;
        }
        var dir = System.IO.Path.GetDirectoryName(name) ?? "";
        var file = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        for (int i = 1; ; i++)
        {
            var candidate = string.IsNullOrEmpty(dir)
                ? $"{file} ({i}){ext}"
                : System.IO.Path.Combine(dir, $"{file} ({i}){ext}");
            if (!used.Contains(candidate))
            {
                used.Add(candidate);
                return candidate;
            }
        }
    }
}

// ─────────────────── Request DTOs ───────────────────

public class CreateGroupRequest
{
    public string Name { get; set; } = "";
}

public class AssignGroupRequest
{
    public Guid ImageId { get; set; }
    public string? GroupName { get; set; }
}

public class ZipRequest
{
    public List<Guid> Ids { get; set; } = new();
}