USE [PddDocuments];
GO

-- ───────────────────────── Sample Agent ─────────────────────────
DECLARE @AgentId UNIQUEIDENTIFIER = NEWID();

IF NOT EXISTS (SELECT 1 FROM PDDImage.Agents WHERE MachineName = N'TEST-PC')
BEGIN
    INSERT INTO PDDImage.Agents (Id, MachineName, IsOnline, LastSeen)
    VALUES (@AgentId, N'TEST-PC', 1, SYSDATETIME());
END
ELSE
BEGIN
    SELECT @AgentId = Id FROM PDDImage.Agents WHERE MachineName = N'TEST-PC';
END
GO

-- ───────────────────────── Sample BaseImageGroups ─────────────────────────
IF NOT EXISTS (SELECT TOP 1 1 FROM PDDImage.BaseImageGroups)
BEGIN
    INSERT INTO PDDImage.BaseImageGroups (ImageGroupName, SoftwareCode, ColorRGB)
    VALUES
        (N'مدارک هویتی', N'SCAN', 0x336699),
        (N'مدارک پزشکی', N'SCAN', 0x993366),
        (N'قراردادها',   N'SCAN', 0x669933);
END
GO

-- ───────────────────────── Sample ScanRequests ─────────────────────────
DECLARE @AgentId UNIQUEIDENTIFIER;
SELECT @AgentId = Id FROM PDDImage.Agents WHERE MachineName = N'TEST-PC';

-- فقط اگر هنوز داده‌ای نباشد، اضافه کن
IF NOT EXISTS (SELECT TOP 1 1 FROM PDDImage.ScanRequests)
BEGIN
    DECLARE @Req1 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Req2 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Req3 UNIQUEIDENTIFIER = NEWID();

    INSERT INTO PDDImage.ScanRequests (Id, AgentId, Status, IsMultiPage, RelationCode, PicType, SoftwareCode, UserCode, CreatedAt, CompletedAt)
    VALUES 
        (@Req1, @AgentId, N'Done', 1, N'REL-001', N'Card',  N'SW-2024', N'U-1001', DATEADD(day, -2, SYSDATETIME()), DATEADD(day, -2, SYSDATETIME())),
        (@Req2, @AgentId, N'Processing', 1, N'REL-002', N'Doc',  N'SW-2024', N'U-1002', DATEADD(day, -1, SYSDATETIME()), NULL),
        (@Req3, @AgentId, N'Pending', 0, N'REL-003', N'Other', N'SW-2025', N'U-1003', SYSDATETIME(), NULL);
END
GO

PRINT N'داده‌های نمونه با موفقیت اضافه شدند.';
GO
