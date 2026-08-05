/* ============================================================================
   ScanSystem - Enterprise Document Scanning System
   Database: PddDocuments (متصل به دیتابیس اصلی پروژه)
   SQL Server + T-SQL
   ----------------------------------------------------------------------------
   اجرا با SQL Server Management Studio یا sqlcmd:
     sqlcmd -S . -E -i 01_CreateDatabase.sql

   نکته مهم:
   - جدول‌های اصلی عکس و گروه دقیقاً مطابق ساختار پروژه اصلی (Schema: PDDImage)
     ساخته می‌شوند و اگر از قبل موجود باشند، دست‌نخورده می‌مانند.
   - فایل اصلی تصویر در ستون ImageField خود جدول ImagesTable ذخیره می‌شود.
   - Thumbnail ها (به دلیل حجم بالای جدول اصلی) در جدول جداگانه
     PDDImage.ImageThumbnails ذخیره می‌شوند.
   - ارتباط گروه با تصویر 1 به n است: ImagesTable.ImageGroupID -> BaseImageGroups.ID
   ============================================================================ */

IF DB_ID(N'PddDocuments') IS NULL
BEGIN
    CREATE DATABASE [PddDocuments];
END
GO

USE [PddDocuments];
GO

/* ─────────────────────────── Schema PDDImage ─────────────────────────── */
IF SCHEMA_ID(N'PDDImage') IS NULL
    EXEC(N'CREATE SCHEMA [PDDImage]');
GO

/* ════════════════════════════════════════════════════════════════════════
   جدول اصلی عکس — دقیقاً مطابق جدول پروژه اصلی
   (اگر در دیتابیس اصلی موجود باشد، تغییری در آن ایجاد نمی‌شود)
   ════════════════════════════════════════════════════════════════════════ */
IF OBJECT_ID(N'PDDImage.ImagesTable', N'U') IS NULL
BEGIN
    CREATE TABLE [PDDImage].[ImagesTable](
        [ImageField] [varbinary](max) NULL,
        [SoftwareCode] [nvarchar](50) NULL,
        [PicType] [nvarchar](50) NULL,
        [RelationCode] [nvarchar](50) NULL,
        [OldRelationCode] [nvarchar](300) NULL,
        [UserCode] [nvarchar](50) NULL,
        [Date] [nvarchar](10) NULL,
        [Id] [numeric](18, 0) IDENTITY(1,1) NOT NULL,
        [ImageGroupID] [numeric](18, 0) NULL,
        [FileType] [nvarchar](5) NULL,
        [FileName] [varchar](50) NULL,
        [Priority] [int] NULL,
        [QualityPercent] [int] NULL,
        [ScanTime] [nvarchar](5) NULL,
        [BarCode] [int] NULL,
        [ISLock] [bit] NULL,
        [IsPrint] [bit] NULL,
        [ISDELETED] [bit] NOT NULL,
        [ImageDescription] [nvarchar](500) NULL,
        [ReceptionID] [numeric](18, 0) NULL,
        [PacsCode] [nvarchar](100) NULL,
        [ISLeadToolsCompression] [bit] NULL,
        [FileSizeKB] [numeric](18, 0) NULL,
        [ISDirectUploadLink] [bit] NULL,
        [PDDImageVersion] [varchar](20) NULL,
     CONSTRAINT [PK_ImagesTable] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE [PDDImage].[ImagesTable] ADD  CONSTRAINT [DF_IMAGESTABLE_ISDELETED]  DEFAULT ((0)) FOR [ISDELETED]
END
GO

/* ════════════════════════════════════════════════════════════════════════
   جدول گروه‌ها — دقیقاً مطابق جدول پروژه اصلی
   ════════════════════════════════════════════════════════════════════════ */
IF OBJECT_ID(N'PDDImage.BaseImageGroups', N'U') IS NULL
BEGIN
    CREATE TABLE [PDDImage].[BaseImageGroups](
        [ID] [numeric](18, 0) IDENTITY(1,1) NOT NULL,
        [ImageGroupName] [nvarchar](50) NOT NULL,
        [SoftwareCode] [nvarchar](5) NOT NULL,
        [BarCode] [int] NULL,
        [ColorRGB] [int] NULL,
     CONSTRAINT [PK_BaseImageGroups] PRIMARY KEY CLUSTERED
    (
        [ID] ASC
    )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
    ) ON [PRIMARY]
END
GO

/* ════════════════════════════════════════════════════════════════════════
   جدول Thumbnail ها — جدا از جدول اصلی عکس (چون جدول اصلی حجم بالایی دارد)
   ارتباط 1 به 1: ImageId همان Id جدول ImagesTable است.
   ════════════════════════════════════════════════════════════════════════ */
IF OBJECT_ID(N'PDDImage.ImageThumbnails', N'U') IS NULL
BEGIN
    CREATE TABLE PDDImage.ImageThumbnails
    (
        ImageId    NUMERIC(18,0)  NOT NULL,
        Thumbnail  VARBINARY(MAX) NOT NULL,
        FileSizeKB NUMERIC(18,0)  NULL,
        CreatedAt  DATETIME       NOT NULL CONSTRAINT DF_ImageThumbnails_CreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_ImageThumbnails PRIMARY KEY (ImageId),
        CONSTRAINT FK_ImageThumbnails_ImagesTable
            FOREIGN KEY (ImageId) REFERENCES PDDImage.ImagesTable(Id) ON DELETE CASCADE
    );
END
GO

/* ─────────────────────────── Agents (داخلی سیستم اسکن) ─────────────────────────── */
IF OBJECT_ID(N'PDDImage.Agents', N'U') IS NULL
BEGIN
    CREATE TABLE PDDImage.Agents
    (
        Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Agents PRIMARY KEY,
        MachineName   NVARCHAR(100)    NOT NULL,
        IsOnline      BIT              NOT NULL CONSTRAINT DF_Agents_IsOnline DEFAULT (0),
        LastSeen      DATETIME         NULL,
        CONSTRAINT UQ_Agents_MachineName UNIQUE (MachineName)
    );
END
GO

/* ─────────────────────────── ScanRequests (داخلی سیستم اسکن) ─────────────────────────── */
IF OBJECT_ID(N'PDDImage.ScanRequests', N'U') IS NULL
BEGIN
    CREATE TABLE PDDImage.ScanRequests
    (
        Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ScanRequests PRIMARY KEY,
        AgentId      UNIQUEIDENTIFIER NOT NULL,
        Status       NVARCHAR(50)     NOT NULL CONSTRAINT DF_ScanRequests_Status DEFAULT (N'Pending'),
        IsMultiPage  BIT              NOT NULL CONSTRAINT DF_ScanRequests_IsMultiPage DEFAULT (0),
        RelationCode NVARCHAR(100)    NULL,
        PicType      NVARCHAR(100)    NULL,
        SoftwareCode NVARCHAR(100)    NULL,
        UserCode     NVARCHAR(100)    NULL,
        CreatedAt    DATETIME         NOT NULL CONSTRAINT DF_ScanRequests_CreatedAt DEFAULT (SYSDATETIME()),
        CompletedAt  DATETIME         NULL,
        CONSTRAINT FK_ScanRequests_Agents
            FOREIGN KEY (AgentId) REFERENCES PDDImage.Agents(Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PDDImage.ScanRequests') AND name = N'IX_ScanRequests_AgentId')
    CREATE INDEX IX_ScanRequests_AgentId ON PDDImage.ScanRequests(AgentId);
GO

/* ─────────────────────────── ScanRequestImages (ارتباط داخلی درخواست ↔ تصویر) ───────────────────────────
   جدول اصلی عکس ستون RequestId ندارد (نباید به آن فیلد اضافه کرد)،
   پس برای شمارش/مدیریت تصاویر هر درخواست از این جدول کمکی استفاده می‌کنیم. */
IF OBJECT_ID(N'PDDImage.ScanRequestImages', N'U') IS NULL
BEGIN
    CREATE TABLE PDDImage.ScanRequestImages
    (
        Id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ScanRequestImages PRIMARY KEY,
        RequestId  UNIQUEIDENTIFIER NOT NULL,
        ImageId    NUMERIC(18,0)    NOT NULL,
        PageNumber INT              NOT NULL CONSTRAINT DF_ScanRequestImages_PageNumber DEFAULT (1),
        CreatedAt  DATETIME         NOT NULL CONSTRAINT DF_ScanRequestImages_CreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT FK_ScanRequestImages_ScanRequests
            FOREIGN KEY (RequestId) REFERENCES PDDImage.ScanRequests(Id) ON DELETE CASCADE,
        CONSTRAINT FK_ScanRequestImages_ImagesTable
            FOREIGN KEY (ImageId) REFERENCES PDDImage.ImagesTable(Id) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PDDImage.ScanRequestImages') AND name = N'IX_ScanRequestImages_RequestId')
    CREATE INDEX IX_ScanRequestImages_RequestId ON PDDImage.ScanRequestImages(RequestId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PDDImage.ScanRequestImages') AND name = N'IX_ScanRequestImages_ImageId')
    CREATE INDEX IX_ScanRequestImages_ImageId ON PDDImage.ScanRequestImages(ImageId);
GO

PRINT N'PDDImage schema created successfully.';
GO

/* ════════════════════════════════════════════════════════════════════════
   ایندکس‌های گالری روی جدول اصلی عکس (PDDImage.ImagesTable)

   این ایندکس‌ها برای کوئری‌های گالری (فیلتر ISDELETED + کدها + مرتب‌سازی Id)
   ضروری هستند؛ بدون آن‌ها هر بارگذاری گالری روی جدول پرحجم، Full Scan می‌شود.
   در صورت وجود ایندکس مشابه از طرف پروژه اصلی، دستور مربوطه را حذف کنید.

   نکته: چون جدول پرحجم است، هر ایندکس اضافی هزینه Insert دارد؛
   این سه ایندکس حداقل لازم برای گالری هستند.
   ════════════════════════════════════════════════════════════════════════ */

-- فیلتر گالری با کدها (SoftwareCode + PicType + RelationCode) + مرتب‌سازی Id نزولی
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PDDImage.ImagesTable') AND name = N'IX_ImagesTable_Gallery')
    CREATE INDEX IX_ImagesTable_Gallery
        ON PDDImage.ImagesTable(ISDELETED, SoftwareCode, PicType, RelationCode, Id DESC);

-- گالری بدون فیلتر کد (همه تصاویر) + مرتب‌سازی Id نزولی
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PDDImage.ImagesTable') AND name = N'IX_ImagesTable_ISDELETED_Id')
    CREATE INDEX IX_ImagesTable_ISDELETED_Id
        ON PDDImage.ImagesTable(ISDELETED, Id DESC);

-- فیلتر گروه (ImageGroupID) + مرتب‌سازی Id نزولی
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PDDImage.ImagesTable') AND name = N'IX_ImagesTable_Group')
    CREATE INDEX IX_ImagesTable_Group
        ON PDDImage.ImagesTable(ISDELETED, ImageGroupID, Id DESC);
GO

/* ─────────────────────────── پاک‌سازی جداول قدیمی (اختیاری) ───────────────────────────
   اگر نسخه قبلی سیستم با جداول dbo.Agents / dbo.ScanRequests / dbo.Images /
   dbo.ImageGroups / dbo.ImageGroupItems اجرا شده است و دیگر به آن‌ها نیازی
   نیست، می‌توانید با این دستورات آن‌ها را حذف کنید:
   
   DROP TABLE IF EXISTS dbo.ImageGroupItems;
   DROP TABLE IF EXISTS dbo.ImageGroups;
   DROP TABLE IF EXISTS dbo.Images;
   DROP TABLE IF EXISTS dbo.ScanRequests;
   DROP TABLE IF EXISTS dbo.Agents;
   ──────────────────────────────────────────────────────────────────────── */
