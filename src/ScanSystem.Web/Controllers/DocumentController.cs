using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using ScanSystem.Shared.Data;
using ScanSystem.Shared.Entities;
using ScanSystem.Web.Services;

namespace ScanSystem.Web.Controllers;

[ApiController]
[Route("api")]
public class DocumentController : ControllerBase
{
    private readonly IScanService _service;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(IScanService service, IWebHostEnvironment env, ILogger<DocumentController> logger)
    {
        _service = service;
        _env = env;
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

    /// <summary>حذف یک Agent (از دیتابیس و نگاشت اتصال).</summary>
    [HttpDelete("agents/{id:guid}")]
    public async Task<IActionResult> DeleteAgent(Guid id)
    {
        await _service.DeleteAgentAsync(id);
        return Ok();
    }

    /// <summary>
    /// دانلود خروجی ZIP برنامه Agent.
    /// فایل باید در {ContentRoot}/downloads/ScanSystem.Agent.zip قرار گیرد
    /// (با `dotnet publish -c Release` از پروژه Agent ساخته و سپس ZIP شود).
    /// </summary>
    [HttpGet("agent/download.zip")]
    public IActionResult DownloadAgentZip()
    {
        var path = Path.Combine(_env.ContentRootPath, "downloads", "ScanSystem.Agent.zip");
        if (!System.IO.File.Exists(path))
        {
            return NotFound(new
            {
                message = "فایل ScanSystem.Agent.zip هنوز در پوشه downloads سرور قرار نگرفته است. " +
                          "پروژه ScanSystem.Agent را روی ویندوز Publish کرده و خروجی را ZIP کنید، " +
                          "سپس در پوشه downloads کنار فایل‌های اجرایی وب قرار دهید. " +
                          "برای جزئیات، صفحه «راهنمای نصب Agent» را ببینید."
            });
        }
        return PhysicalFile(path, "application/zip", "ScanSystem.Agent.zip");
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

    /// <summary>تخصیص یک تصویر به یک گروه (با درگ و دراپ یا از مودال گالری).</summary>
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

    /// <summary>Thumbnail تصویر (برای گالری). اگر Thumbnail موجود نباشد، تصویر اصلی برگردانده می‌شود.</summary>
    [HttpGet("images/thumb/{id:guid}")]
    public async Task<IActionResult> GetThumbnail(Guid id)
    {
        var data = await _service.GetImageThumbnailAsync(id);
        if (data is null || data.Length == 0)
        {
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

    /// <summary>چرخش تصویر (90/180/270) — Thumbnail هم بازسازی می‌شود.</summary>
    [HttpPost("images/{id:guid}/rotate")]
    public async Task<IActionResult> RotateImage(Guid id, [FromBody] RotateRequest req)
    {
        var angle = req?.Angle ?? 90;
        if (angle is not (90 or 180 or 270))
            return BadRequest("زاویه باید یکی از مقادیر 90، 180 یا 270 باشد.");

        var doc = await _service.GetImageDownloadAsync(id);
        if (doc is null) return NotFound();

        var rotated = ThumbnailGenerator.Rotate(doc.Data, angle);
        if (rotated is null)
            return StatusCode(StatusCodes.Status500InternalServerError, "خطا در پردازش تصویر (احتمالاً فرمت پشتیبانی‌نشده).");

        await _service.UpdateImageAsync(id, rotated, thumbnail: null);
        return Ok(new { id, angle });
    }

    /// <summary>جایگزینی تصویر با فایل آپلودشده (multipart/form-data، فیلد file).</summary>
    [HttpPost("images/{id:guid}/replace")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceImage(Guid id)
    {
        var doc = await _service.GetImageDownloadAsync(id);
        if (doc is null) return NotFound();

        if (Request.Form.Files.Count == 0)
            return BadRequest("فایلی ارسال نشده است.");

        var file = Request.Form.Files[0];
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();
        if (data.Length == 0) return BadRequest("فایل خالی است.");

        await _service.UpdateImageAsync(id, data, thumbnail: null);
        return Ok(new { id, size = data.Length });
    }

    /// <summary>تصاویر یک درخواست (با ترتیب صفحه).</summary>
    [HttpGet("request/{requestId:guid}/images")]
    public async Task<IActionResult> GetImagesByRequest(Guid requestId)
    {
        var images = await _service.GetImagesByRequestAsync(requestId);
        if (images.Count == 0) return NotFound();
        return Ok(images.Select(i => new { i.Id, i.RequestId, i.FileName, i.PageNumber }));
    }

    /// <summary>بایت‌های خام یک صفحه مشخص از یک درخواست.</summary>
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

public class RotateRequest
{
    public int Angle { get; set; } = 90;
}

public class ZipRequest
{
    public List<Guid> Ids { get; set; } = new();
}
