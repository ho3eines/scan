using Microsoft.AspNetCore.Mvc;
using ScanSystem.Web.Services;

namespace ScanSystem.Web.Controllers;

/// <summary>
/// API endpoint برای سرویس تصاویر اسکن‌شده.
/// آدرس‌ها:
///   GET /api/images/{id}        → تصویر کامل
///   GET /api/images/{id}/thumbnail → تصویر کوچک
/// </summary>
[ApiController]
[Route("api/images")]
public class ImagesController : ControllerBase
{
    private readonly IScanService _scanService;

    public ImagesController(IScanService scanService)
    {
        _scanService = scanService;
    }

    /// <summary>تصویر کامل</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetImage(Guid id)
    {
        var data = await _scanService.GetImageDataAsync(id);
        if (data == null || data.Length == 0)
            return NotFound();

        return File(data, "image/jpeg");
    }

    /// <summary>تصویر کوچک (Thumbnail)</summary>
    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> GetThumbnail(Guid id)
    {
        var data = await _scanService.GetImageThumbnailAsync(id);
        if (data == null || data.Length == 0)
        {
            // اگر thumbnail نداشت، تصویر اصلی را برگردان
            data = await _scanService.GetImageDataAsync(id);
        }
        if (data == null || data.Length == 0)
            return NotFound();

        return File(data, "image/jpeg");
    }
}
