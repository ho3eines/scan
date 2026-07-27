/* ============================================================================
   ScanSystem - Enterprise Document Scanning System
   Database: PddDocuments
   SQL Server + T-SQL
   ----------------------------------------------------------------------------
   اجرا با SQL Server Management Studio یا sqlcmd:
     sqlcmd -S . -E -i 01_CreateDatabase.sql
   ============================================================================ */

IF DB_ID(N'PddDocuments') IS NULL
BEGIN
    CREATE DATABASE [PddDocuments];
END
GO

USE [PddDocuments];
GO

/* ──────────────────────────── Agents ──────────────────────────── */
IF OBJECT_ID(N'dbo.Agents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Agents
    (
        Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Agents PRIMARY KEY,
        MachineName   NVARCHAR(100)    NOT NULL,
        IsOnline      BIT              NOT NULL CONSTRAINT DF_Agents_IsOnline DEFAULT (0),
        LastSeen      DATETIME         NULL,
        CONSTRAINT UQ_Agents_MachineName UNIQUE (MachineName)
    );
END
GO

/* ──────────────────────────── ScanRequests ──────────────────────────── */
IF OBJECT_ID(N'dbo.ScanRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ScanRequests
    (
        Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ScanRequests PRIMARY KEY,
        AgentId      UNIQUEIDENTIFIER NOT NULL,
        Status       NVARCHAR(50)     NOT NULL CONSTRAINT DF_ScanRequests_Status DEFAULT (N'Pending'),
        IsMultiPage  BIT              NOT NULL CONSTRAINT DF_ScanRequests_IsMultiPage DEFAULT (0),
        CreatedAt    DATETIME         NOT NULL CONSTRAINT DF_ScanRequests_CreatedAt DEFAULT (SYSDATETIME()),
        CompletedAt  DATETIME         NULL,
        CONSTRAINT FK_ScanRequests_Agents
            FOREIGN KEY (AgentId) REFERENCES dbo.Agents(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'IX_ScanRequests_AgentId', N'UQ') IS NULL
    CREATE INDEX IX_ScanRequests_AgentId ON dbo.ScanRequests(AgentId);
GO

/* ──────────────────────────── Images ──────────────────────────── */
IF OBJECT_ID(N'dbo.Images', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Images
    (
        Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Images PRIMARY KEY,
        RequestId   UNIQUEIDENTIFIER NOT NULL,
        FileName    NVARCHAR(255)    NULL,
        Data        VARBINARY(MAX)   NOT NULL,
        Thumbnail   VARBINARY(MAX)   NULL,
        PageNumber  INT              NOT NULL CONSTRAINT DF_Images_PageNumber DEFAULT (1),
        CreatedAt   DATETIME         NOT NULL CONSTRAINT DF_Images_CreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT FK_Images_ScanRequests
            FOREIGN KEY (RequestId) REFERENCES dbo.ScanRequests(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'IX_Images_RequestId', N'UQ') IS NULL
    CREATE INDEX IX_Images_RequestId ON dbo.Images(RequestId);
GO

/* ──────────────────────────── ImageGroups ──────────────────────────── */
IF OBJECT_ID(N'dbo.ImageGroups', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImageGroups
    (
        Id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ImageGroups PRIMARY KEY,
        Name  NVARCHAR(100)    NOT NULL,
        CONSTRAINT UQ_ImageGroups_Name UNIQUE (Name)
    );
END
GO

/* ──────────────────────────── ImageGroupItems ──────────────────────────── */
IF OBJECT_ID(N'dbo.ImageGroupItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImageGroupItems
    (
        Id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ImageGroupItems PRIMARY KEY,
        ImageId   UNIQUEIDENTIFIER NOT NULL,
        GroupId   UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT FK_ImageGroupItems_Images
            FOREIGN KEY (ImageId) REFERENCES dbo.Images(Id) ON DELETE CASCADE,
        CONSTRAINT FK_ImageGroupItems_ImageGroups
            FOREIGN KEY (GroupId) REFERENCES dbo.ImageGroups(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_ImageGroupItems_Image_Group UNIQUE (ImageId, GroupId)
    );
END
GO

IF OBJECT_ID(N'IX_ImageGroupItems_GroupId', N'UQ') IS NULL
    CREATE INDEX IX_ImageGroupItems_GroupId ON dbo.ImageGroupItems(GroupId);
GO

PRINT N'PddDocuments schema created successfully.';
GO
