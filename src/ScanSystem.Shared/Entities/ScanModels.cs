namespace ScanSystem.Shared.Entities;

/// <summary>
/// یک ایستگاه کاری (Agent) ثبت‌شده در سامانه.
/// هر دستگاه Agent خودش را با MachineName یکتا ثبت می‌کند.
/// </summary>
public class Agent
{
    public Guid Id { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
}

/// <summary>مدل سبک برای ارسال لیست Agentها به UI / DataTable.</summary>
public class AgentDto
{
    public Guid Id { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
    /// <summary>متن نمایش وضعیت در UI: آنلاین / آفلاین.</summary>
    public string StatusDisplay => IsOnline ? "آنلاین" : "آفلاین";
}

/// <summary>یک درخواست اسکن (تکی یا چند صفحه‌ای).</summary>
public class ScanRequest
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string Status { get; set; } = ScanStatus.Pending;
    public bool IsMultiPage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>مدل سبک برای نمایش درخواست‌ها در DataTable (Server-side).</summary>
public class ScanRequestDto
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string AgentMachineName { get; set; } = string.Empty;
    public string Status { get; set; } = ScanStatus.Pending;
    public bool IsMultiPage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ImageCount { get; set; }
}

/// <summary>رکورد یک تصویر اسکن‌شده (یکی از صفحات یک درخواست).</summary>
public class ScanImage
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string? FileName { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public byte[]? Thumbnail { get; set; }
    public int PageNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>مدل سبک برای گالری تصاویر (بدون بایت‌های سنگین Data).</summary>
public class ImageGalleryItemDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string? FileName { get; set; }
    public int PageNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string AgentMachineName { get; set; } = string.Empty;
    /// <summary>نام گروه‌های اختصاص‌داده‌شده به این تصویر (با کاما جدا شده).</summary>
    public string? Groups { get; set; }
    public bool HasThumbnail { get; set; }
}

/// <summary>یک گروه تصاویر.</summary>
public class ImageGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>رابطه تصویر ↔ گروه (many-to-many).</summary>
public class ImageGroupItem
{
    public Guid Id { get; set; }
    public Guid ImageId { get; set; }
    public Guid GroupId { get; set; }
}

/// <summary>مدل سبک برای دانلود/استریم تصویر (Data + ContentType + FileName).</summary>
public class ImageDownloadDto
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string? FileName { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int PageNumber { get; set; }
    /// <summary>Content-Type استنباط‌شده از پسوند فایل.</summary>
    public string ContentType => GuessContentType(FileName);

    private static string GuessContentType(string? name)
    {
        var ext = (name ?? "").ToLowerInvariant();
        if (ext.EndsWith(".png")) return "image/png";
        if (ext.EndsWith(".tif") || ext.EndsWith(".tiff")) return "image/tiff";
        if (ext.EndsWith(".bmp")) return "image/bmp";
        if (ext.EndsWith(".gif")) return "image/gif";
        return "image/jpeg"; // پیش‌فرض برای تصاویر اسکن‌شده از WIA (JPEG)
    }
}
