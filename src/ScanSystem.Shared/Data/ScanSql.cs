namespace ScanSystem.Shared.Data;

/// <summary>
/// مرکز تمام دستورات T-SQL برنامه اسکن.
/// همه کوئری‌ها Parametrized هستند و فقط در این یک کلاس نگهداری می‌شوند.
/// </summary>
public static class ScanSql
{
    // ───────────────────────── Agents ─────────────────────────
    public const string AgentsGetAll = @"
        SELECT Id, MachineName, IsOnline, LastSeen
        FROM dbo.Agents
        ORDER BY IsOnline DESC, LastSeen DESC;";

    public const string AgentsGetByMachineName = @"
        SELECT Id, MachineName, IsOnline, LastSeen
        FROM dbo.Agents
        WHERE MachineName = @MachineName;";

    public const string AgentsGetById = @"
        SELECT Id, MachineName, IsOnline, LastSeen
        FROM dbo.Agents
        WHERE Id = @Id;";

    public const string AgentsUpsert = @"
        IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE MachineName = @MachineName)
            INSERT INTO dbo.Agents (Id, MachineName, IsOnline, LastSeen)
            VALUES (@Id, @MachineName, @IsOnline, SYSDATETIME());
        ELSE
            UPDATE dbo.Agents
            SET IsOnline = @IsOnline, LastSeen = SYSDATETIME()
            WHERE MachineName = @MachineName;";

    public const string AgentsSetOfflineById = @"
        UPDATE dbo.Agents
        SET IsOnline = 0, LastSeen = SYSDATETIME()
        WHERE Id = @Id;";

    public const string AgentsSetOfflineByMachineName = @"
        UPDATE dbo.Agents
        SET IsOnline = 0, LastSeen = SYSDATETIME()
        WHERE MachineName = @MachineName;";

    public const string AgentsDelete = @"
        DELETE FROM dbo.Agents WHERE Id = @Id;";

    // ───────────────────────── ScanRequests ─────────────────────────
    public const string RequestsCreate = @"
        INSERT INTO dbo.ScanRequests (Id, AgentId, Status, IsMultiPage, RelationCode, InquiryCode, SoftwareCode, CreatedAt)
        OUTPUT inserted.Id
        VALUES (@Id, @AgentId, @Status, @IsMultiPage, @RelationCode, @InquiryCode, @SoftwareCode, SYSDATETIME());";

    public const string RequestsSetStatus = @"
        UPDATE dbo.ScanRequests
        SET Status = @Status
        WHERE Id = @Id;";

    public const string RequestsComplete = @"
        UPDATE dbo.ScanRequests
        SET Status = @Status, CompletedAt = SYSDATETIME()
        WHERE Id = @Id;";

    public const string RequestsSetError = @"
        UPDATE dbo.ScanRequests
        SET Status = @Status, CompletedAt = SYSDATETIME()
        WHERE Id = @Id;";

    public const string RequestsDelete = @"
        DELETE FROM dbo.ScanRequests WHERE Id = @Id;";

    public const string RequestsGetRecent = @"
        SELECT TOP (@Take)
               r.Id, r.AgentId, a.MachineName,
               r.Status, r.IsMultiPage, r.RelationCode, r.InquiryCode, r.SoftwareCode, r.CreatedAt, r.CompletedAt,
               ImageCount = (SELECT COUNT(*) FROM dbo.Images i WHERE i.RequestId = r.Id)
        FROM dbo.ScanRequests r
        LEFT JOIN dbo.Agents a ON a.Id = r.AgentId
        ORDER BY r.CreatedAt DESC;";

    // ───────────────────────── Images ─────────────────────────
    public const string ImagesAdd = @"
        INSERT INTO dbo.Images (Id, RequestId, FileName, Data, Thumbnail, RelationCode, InquiryCode, SoftwareCode, PageNumber, CreatedAt)
        OUTPUT inserted.Id
        SELECT @Id, @RequestId, @FileName, @Data, @Thumbnail,
               r.RelationCode, r.InquiryCode, r.SoftwareCode,
               @PageNumber, SYSDATETIME()
        FROM dbo.ScanRequests r
        WHERE r.Id = @RequestId;";

    public const string ImagesGetData = @"
        SELECT Data FROM dbo.Images WHERE Id = @Id;";

    public const string ImagesGetThumbnail = @"
        SELECT Thumbnail FROM dbo.Images WHERE Id = @Id;";

    public const string ImagesDelete = @"
        DELETE FROM dbo.Images WHERE Id = @Id;";

    public const string ImagesUpdate = @"
        UPDATE dbo.Images
        SET Data = @Data, Thumbnail = @Thumbnail
        WHERE Id = @Id;";

    // ───────────────────────── Gallery ─────────────────────────
    public const string GalleryCount = @"
        SELECT COUNT(*)
        FROM dbo.Images i
        INNER JOIN dbo.ScanRequests r ON r.Id = i.RequestId
        INNER JOIN dbo.Agents a ON a.Id = r.AgentId
        {0};";

    public const string GalleryPage = @"
        SELECT i.Id, i.RequestId, i.FileName, i.PageNumber,
               i.RelationCode, i.InquiryCode, i.SoftwareCode,
               i.CreatedAt,
               a.MachineName AS AgentMachineName,
               ({0}) AS Groups,
               CASE WHEN i.Thumbnail IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasThumbnail
        FROM dbo.Images i
        INNER JOIN dbo.ScanRequests r ON r.Id = i.RequestId
        INNER JOIN dbo.Agents a ON a.Id = r.AgentId
        {1}
        ORDER BY i.CreatedAt DESC
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";

    public const string GalleryGroupsSubquery = @"
        STUFF((
            SELECT N', ' + g.Name
            FROM dbo.ImageGroupItems igi
            JOIN dbo.ImageGroups g ON g.Id = igi.GroupId
            WHERE igi.ImageId = i.Id
            ORDER BY g.Name
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'), 1, 2, N'')";

    // ───────────────────────── ImageGroups ─────────────────────────
    public const string GroupsGetAll = @"
        SELECT Id, Name FROM dbo.ImageGroups ORDER BY Name ASC;";

    public const string GroupsGetByName = @"
        SELECT Id, Name FROM dbo.ImageGroups WHERE Name = @Name;";

    public const string GroupsCreate = @"
        INSERT INTO dbo.ImageGroups (Id, Name)
        OUTPUT inserted.Id, inserted.Name
        VALUES (@Id, @Name);";

    public const string GroupsDelete = @"
        DELETE FROM dbo.ImageGroups WHERE Id = @Id;";

    // ───────────────────────── ImageGroupItems ─────────────────────────
    public const string GroupItemsAssign = @"
        IF NOT EXISTS (SELECT 1 FROM dbo.ImageGroupItems WHERE ImageId = @ImageId AND GroupId = @GroupId)
            INSERT INTO dbo.ImageGroupItems (Id, ImageId, GroupId)
            VALUES (@Id, @ImageId, @GroupId);";

    public const string GroupItemsRemove = @"
        DELETE FROM dbo.ImageGroupItems
        WHERE ImageId = @ImageId AND GroupId = @GroupId;";
}
