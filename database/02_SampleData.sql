/*
============================================================================
Sample Data Script - PddDocuments Database
============================================================================
این اسکریپت داده‌های نمونه برای تست و توسعه ایجاد می‌کند.
اجرا با: sqlcmd -S . -E -i 02_SampleData.sql
============================================================================
*/

USE [PddDocuments];
GO

-- ───────────────────────── Sample Agent ─────────────────────────
DECLARE @AgentId UNIQUEIDENTIFIER = NEWID();

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE MachineName = N'TEST-PC')
BEGIN
    INSERT INTO dbo.Agents (Id, MachineName, IsOnline, LastSeen)
    VALUES (@AgentId, N'TEST-PC', 1, SYSDATETIME());
END
ELSE
BEGIN
    SELECT @AgentId = Id FROM dbo.Agents WHERE MachineName = N'TEST-PC';
END
GO

-- ───────────────────────── Sample ScanRequests ─────────────────────────
DECLARE @AgentId UNIQUEIDENTIFIER;
SELECT @AgentId = Id FROM dbo.Agents WHERE MachineName = N'TEST-PC';

-- فقط اگر هنوز داده‌ای نباشد، اضافه کن
IF NOT EXISTS (SELECT TOP 1 1 FROM dbo.ScanRequests)
BEGIN
    DECLARE @Req1 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Req2 UNIQUEIDENTIFIER = NEWID();
    DECLARE @Req3 UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.ScanRequests (Id, AgentId, Status, IsMultiPage, RelationCode, InquiryCode, SoftwareCode, FullName, CreatedAt, CompletedAt)
    VALUES 
        (@Req1, @AgentId, N'Done', 1, N'REL-001', N'INQ-1001', N'SW-2024', N'علی محمدی', DATEADD(day, -2, SYSDATETIME()), DATEADD(day, -2, SYSDATETIME())),
        (@Req2, @AgentId, N'Processing', 1, N'REL-002', N'INQ-1002', N'SW-2024', N'رضا احمدی', DATEADD(day, -1, SYSDATETIME()), NULL),
        (@Req3, @AgentId, N'Pending', 0, N'REL-003', N'INQ-1003', N'SW-2025', N'محمد کریمی', SYSDATETIME(), NULL);
END
GO

PRINT N'داده‌های نمونه با موفقیت اضافه شدند.';
GO
