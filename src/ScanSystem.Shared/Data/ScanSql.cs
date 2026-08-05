namespace ScanSystem.Shared.Data;

/// <summary>
/// مرکز تمام دستورات T-SQL برنامه اسکن.
/// همه کوئری‌ها Parametrized هستند و فقط در این یک کلاس نگهداری می‌شوند.
///
/// جداول اصلی پروژه (Schema: PDDImage):
///   - PDDImage.ImagesTable      → جدول اصلی عکس (فایل در ImageField)
///   - PDDImage.BaseImageGroups  → جدول گروه‌ها (ارتباط 1 به n از طریق ImageGroupID)
///   - PDDImage.ImageThumbnails  → Thumbnail ها (جدا از جدول اصلی به دلیل حجم بالا)
///
/// نکته بهینگی:
///   - همه SELECT ها از WITH (NOLOCK) استفاده می‌کنند (سیستم اسکن/نمایش،
///     Dirty Read قابل قبول است و قفل‌گذاری روی جدول پرحجم اصلی را حذف می‌کند).
///   - کوئری‌های گالری که WHERE پویا دارند، OPTION (RECOMPILE) دارند تا
///     برای هر ترکیب فیلتر، پلن بهینه ساخته شود.
/// </summary>
public static class ScanSql
{
    // ───────────────────────── Agents ─────────────────────────
    public const string AgentsGetAll = @"
        SELECT Id, MachineName, IsOnline, LastSeen
        FROM PDDImage.Agents WITH (NOLOCK)
        ORDER BY IsOnline DESC, LastSeen DESC;";

    public const string AgentsGetByMachineName = @"
        SELECT Id, MachineName, IsOnline, LastSeen
        FROM PDDImage.Agents WITH (NOLOCK)
        WHERE MachineName = @MachineName;";

    public const string AgentsGetById = @"
        SELECT Id, MachineName, IsOnline, LastSeen
        FROM PDDImage.Agents WITH (NOLOCK)
        WHERE Id = @Id;";

    public const string AgentsUpsert = @"
        IF NOT EXISTS (SELECT 1 FROM PDDImage.Agents WITH (NOLOCK) WHERE MachineName = @MachineName)
            INSERT INTO PDDImage.Agents (Id, MachineName, IsOnline, LastSeen)
            VALUES (@Id, @MachineName, @IsOnline, SYSDATETIME());
        ELSE
            UPDATE PDDImage.Agents
            SET IsOnline = @IsOnline, LastSeen = SYSDATETIME()
            WHERE MachineName = @MachineName;";

    public const string AgentsSetOfflineById = @"
        UPDATE PDDImage.Agents
        SET IsOnline = 0, LastSeen = SYSDATETIME()
        WHERE Id = @Id;";

    public const string AgentsSetOfflineByMachineName = @"
        UPDATE PDDImage.Agents
        SET IsOnline = 0, LastSeen = SYSDATETIME()
        WHERE MachineName = @MachineName;";

    public const string AgentsDelete = @"
        DELETE FROM PDDImage.Agents WHERE Id = @Id;";

    // ───────────────────────── ScanRequests ─────────────────────────
    public const string RequestsCreate = @"
        INSERT INTO PDDImage.ScanRequests (Id, AgentId, Status, IsMultiPage, RelationCode, PicType, SoftwareCode, UserCode, CreatedAt)
        OUTPUT inserted.Id
        VALUES (@Id, @AgentId, @Status, @IsMultiPage, @RelationCode, @PicType, @SoftwareCode, @UserCode, SYSDATETIME());";

    public const string RequestsSetStatus = @"
        UPDATE PDDImage.ScanRequests
        SET Status = @Status
        WHERE Id = @Id;";

    public const string RequestsComplete = @"
        UPDATE PDDImage.ScanRequests
        SET Status = @Status, CompletedAt = SYSDATETIME()
        WHERE Id = @Id;";

    public const string RequestsSetError = @"
        UPDATE PDDImage.ScanRequests
        SET Status = @Status, CompletedAt = SYSDATETIME()
        WHERE Id = @Id;";

    public const string RequestsDelete = @"
        DELETE FROM PDDImage.ScanRequests WHERE Id = @Id;";

    public const string RequestsGetRecent = @"
        SELECT TOP (@Take)
               r.Id, r.AgentId, a.MachineName,
               r.Status, r.IsMultiPage, r.RelationCode, r.PicType, r.SoftwareCode, r.UserCode, r.CreatedAt, r.CompletedAt,
               ImageCount = (SELECT COUNT(*) FROM PDDImage.ScanRequestImages sri WITH (NOLOCK) WHERE sri.RequestId = r.Id)
        FROM PDDImage.ScanRequests r WITH (NOLOCK)
        LEFT JOIN PDDImage.Agents a WITH (NOLOCK) ON a.Id = r.AgentId
        ORDER BY r.CreatedAt DESC;";

    public const string RequestsGetList = @"
        SELECT r.Id, r.RelationCode, r.PicType, r.SoftwareCode, r.UserCode, r.Status, r.CreatedAt,
               ImageCount = (SELECT COUNT(*) FROM PDDImage.ScanRequestImages sri WITH (NOLOCK) WHERE sri.RequestId = r.Id)
        FROM PDDImage.ScanRequests r WITH (NOLOCK)
        ORDER BY r.CreatedAt DESC;";

    // ───────────────────────── Images (جدول اصلی پروژه) ─────────────────────────
    // فایل اصلی تصویر در ImageField ذخیره می‌شود؛ Thumbnail به‌صورت جداگانه
    // در PDDImage.ImageThumbnails و ارتباط درخواست↔تصویر در ScanRequestImages.
    public const string ImagesAdd = @"
        INSERT INTO PDDImage.ImagesTable
            (ImageField, SoftwareCode, PicType, RelationCode, UserCode, [Date],
             ImageGroupID, FileType, FileName, Priority, QualityPercent, ScanTime,
             BarCode, ISLock, IsPrint, ISDELETED, ImageDescription, ReceptionID, PacsCode,
             ISLeadToolsCompression, FileSizeKB, ISDirectUploadLink, PDDImageVersion)
        OUTPUT inserted.Id
        SELECT @Data, r.SoftwareCode, r.PicType, r.RelationCode, r.UserCode, @Date,
               NULL, @FileType, @FileName, NULL, NULL, @ScanTime,
               NULL, 0, 0, 0, NULL, NULL, NULL,
               0, @FileSizeKB, 0, NULL
        FROM PDDImage.ScanRequests r WITH (NOLOCK)
        WHERE r.Id = @RequestId;";

    public const string ThumbnailsAdd = @"
        INSERT INTO PDDImage.ImageThumbnails (ImageId, Thumbnail, FileSizeKB)
        VALUES (@ImageId, @Thumbnail, @ThumbSizeKB);";

    public const string ScanRequestImagesAdd = @"
        INSERT INTO PDDImage.ScanRequestImages (Id, RequestId, ImageId, PageNumber, CreatedAt)
        VALUES (@Id, @RequestId, @ImageId, @PageNumber, SYSDATETIME());";

    public const string ImagesGetData = @"
        SELECT ImageField FROM PDDImage.ImagesTable WITH (NOLOCK)
        WHERE Id = @Id AND isnull([ISDELETED],0)=0;";

    public const string ImagesGetThumbnail = @"
        SELECT Thumbnail FROM PDDImage.ImageThumbnails WITH (NOLOCK)
        WHERE ImageId = @Id;";

    // حذف نرم (ISDELETED=1) — چون جدول اصلی پروژه است نباید فیزیکی حذف کنیم.
    public const string ImagesDelete = @"
        UPDATE PDDImage.ImagesTable SET ISDELETED = 1 WHERE Id = @Id;
        DELETE FROM PDDImage.ImageThumbnails WHERE ImageId = @Id;
        DELETE FROM PDDImage.ScanRequestImages WHERE ImageId = @Id;";

    public const string ImagesUpdate = @"
        UPDATE PDDImage.ImagesTable
        SET ImageField = @Data, FileSizeKB = @FileSizeKB
        WHERE Id = @Id;
        DELETE FROM PDDImage.ImageThumbnails WHERE ImageId = @Id;
        IF @Thumbnail IS NOT NULL
            INSERT INTO PDDImage.ImageThumbnails (ImageId, Thumbnail, FileSizeKB)
            VALUES (@Id, @Thumbnail, @ThumbSizeKB);";

    // ───────────────────────── Gallery ─────────────────────────
    // WHERE پویا است (ترکیب فیلترها متغیر) → OPTION (RECOMPILE) پلن بهینه می‌سازد.
    public const string GalleryCount = @"
        SELECT COUNT(*)
        FROM PDDImage.ImagesTable i WITH (NOLOCK)
        {0}
        OPTION (RECOMPILE);";

    public const string GalleryPage = @"
        SELECT i.Id, i.FileName, i.SoftwareCode, i.PicType, i.RelationCode, i.UserCode,
               i.[Date], i.ScanTime, i.FileSizeKB, i.ImageGroupID,
               g.ImageGroupName AS GroupName,
               CASE WHEN t.ImageId IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasThumbnail
        FROM PDDImage.ImagesTable i WITH (NOLOCK)
        LEFT JOIN PDDImage.BaseImageGroups g WITH (NOLOCK) ON g.ID = i.ImageGroupID
        LEFT JOIN PDDImage.ImageThumbnails t WITH (NOLOCK) ON t.ImageId = i.Id
        {0}
        ORDER BY i.Id DESC
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
        OPTION (RECOMPILE);";

    // ───────────────────────── Groups (1 به n) ─────────────────────────
    public const string GroupsGetAll = @"
        SELECT ID, ImageGroupName, SoftwareCode
        FROM PDDImage.BaseImageGroups WITH (NOLOCK)
        ORDER BY ImageGroupName ASC;";

    public const string GroupsGetByName = @"
        SELECT ID, ImageGroupName, SoftwareCode
        FROM PDDImage.BaseImageGroups WITH (NOLOCK)
        WHERE ImageGroupName = @Name AND SoftwareCode = @SoftwareCode;";

    public const string GroupsCreate = @"
        INSERT INTO PDDImage.BaseImageGroups (ImageGroupName, SoftwareCode)
        OUTPUT inserted.ID
        VALUES (@Name, @SoftwareCode);";

    public const string GroupsDelete = @"
        UPDATE PDDImage.ImagesTable SET ImageGroupID = NULL WHERE ImageGroupID = @Id;
        DELETE FROM PDDImage.BaseImageGroups WHERE ID = @Id;";

    // هر تصویر فقط به یک گروه تعلق دارد (ارتباط 1 به n از طریق ImageGroupID)
    public const string GroupSet = @"
        UPDATE PDDImage.ImagesTable
        SET ImageGroupID = @GroupId
        WHERE Id = @ImageId;";

    public const string GroupClear = @"
        UPDATE PDDImage.ImagesTable
        SET ImageGroupID = NULL
        WHERE Id = @ImageId AND ImageGroupID = @GroupId;";
}
