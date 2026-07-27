
```
یک سیستم Enterprise اسکن مدارک با معماری زیر برایم پیاده‌سازی کن.
پروژه را به صورت مرحله‌ای و با کد کامل و قابل اجرا بساز، نه فقط نمونه‌کد.
قبل از شروع، ساختار فولدر پروژه را پیشنهاد بده و تأیید بگیر، سپس ادامه بده.
```

---

## 1. نقش و زمینه (Role & Context)

من در حال ساخت یک سیستم داخلی (Enterprise) برای اسکن مدارک هستم که باید در محیط شرکتی قابل استقرار باشد. تو باید به عنوان یک مهندس Full-Stack .NET با تجربه در معماری‌های Enterprise عمل کنی و کد Production-Ready تولید کنی، نه Prototype.

## 2. استک فنی (Tech Stack) — الزامی

| بخش | فناوری |
|---|---|
| Backend / UI | Blazor Server (.NET 8) |
| Data Access | Dapper (بدون EF Core) |
| دیتابیس | SQL Server + T-SQL خام |
| ارتباط Real-time | SignalR |
| Agent محلی | WPF Worker Service |
| توزیع Agent | ClickOnce + دانلود ZIP |
| Frontend کمکی | Bootstrap 5 + DataTables + JavaScript خالص |

## 3. هدف سیستم

- اسکن خودکار مدارک بدون دخالت کاربر در لحظه اسکن
- پشتیبانی از اسکن چند صفحه‌ای (Multi-page) در یک درخواست
- شناسایی هر ایستگاه کاری (Agent) بر اساس Machine Name
- مدیریت وضعیت Agent‌ها: آنلاین / آفلاین / نصب‌نشده
- ذخیره تصاویر به صورت باینری در SQL Server همراه با Thumbnail
- گالری تصاویر با Lazy Loading
- امکان انتخاب چندگانه، دانلود دسته‌ای ZIP، ویرایش، و گروه‌بندی تصاویر (Drag & Drop)

## 4. معماری کلی

```
Blazor Server (UI + API)
        |
     SignalR
        |
   ------------------
   |                |
Agent (WPF)     SQL Server
   |                |
 Scanner        Images + Data
```

## 5. اسکیمای دیتابیس (باید دقیقاً همین ساختار رعایت شود)

```sql
CREATE TABLE Agents (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    MachineName NVARCHAR(100),
    IsOnline BIT,
    LastSeen DATETIME
);

CREATE TABLE ScanRequests (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    AgentId UNIQUEIDENTIFIER,
    Status NVARCHAR(50),
    IsMultiPage BIT,
    CreatedAt DATETIME,
    CompletedAt DATETIME
);

CREATE TABLE Images (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    RequestId UNIQUEIDENTIFIER,
    FileName NVARCHAR(255),
    Data VARBINARY(MAX),
    Thumbnail VARBINARY(MAX),
    PageNumber INT,
    CreatedAt DATETIME
);

CREATE TABLE ImageGroups (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(100)
);

CREATE TABLE ImageGroupItems (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ImageId UNIQUEIDENTIFIER,
    GroupId UNIQUEIDENTIFIER
);
```

## 6. الزامات فنی هر لایه

### 6.1 Data Access (Dapper)
- یک Repository مجزا برای هر Entity بساز (`AgentRepository`, `ScanRequestRepository`, `ImageRepository`, `ImageGroupRepository`)
- تمام کوئری‌ها Parametrized باشند (جلوگیری از SQL Injection)
- Connection String از `appsettings.json` خوانده شود
- Pagination با `OFFSET / FETCH NEXT` برای گالری تصاویر

### 6.2 SignalR Hub
- متدهای موردنیاز: `RegisterAgent`, `RequestScan`, `UploadPage`, و رویداد `AgentStatusChanged`
- Auto-reconnect در سمت کلاینت پیاده‌سازی شود
- مدیریت قطع اتصال Agent و بروزرسانی خودکار وضعیت `IsOnline`

### 6.3 Agent (WPF)
- بدون UI اصلی، فقط آیکون در Tray
- Auto Start همراه با ویندوز
- صف داخلی برای درخواست‌های اسکن
- حلقه اسکن چند صفحه‌ای تا پایان Feeder اسکنر
- ارسال هر صفحه به‌محض اسکن (Streaming) نه بعد از اتمام کل کار

### 6.4 توزیع Agent
- خروجی هم به صورت ZIP قابل دانلود و هم ClickOnce
- صفحه مدیریت Agent باید بر اساس Machine Name وضعیت را تشخیص دهد و دکمه مناسب (دانلود / غیرفعال / انتخاب) را نشان دهد

### 6.5 گالری تصاویر
- API: `GET /api/images?skip=0&take=20`
- Lazy Load با Scroll یا دکمه "بارگذاری بیشتر"
- نمایش Thumbnail در گالری و تصویر اصلی فقط در Fullscreen/Modal
- انتخاب چندگانه با Checkbox و دانلود دسته‌ای به صورت ZIP (`System.IO.Compression`)
- ویرایش تصویر: چرخش (Rotate)، حذف، جایگزینی (Replace Upload)
- گروه‌بندی با Drag & Drop و ذخیره رابطه در `ImageGroupItems`

### 6.6 لیست‌ها
- استفاده از DataTables برای نمایش لیست درخواست‌های اسکن با Server-side Processing

## 7. خروجی‌های موردانتظار از Claude Code

لطفاً پروژه را دقیقاً به این ترتیب تحویل بده:

1. ساختار فولدر کامل Solution (`.sln` + پروژه‌های Blazor Server و WPF Agent)
2. اسکریپت کامل T-SQL برای ساخت دیتابیس و جداول
3. لایه Dapper Repository + Interfaces
4. SignalR Hub کامل
5. صفحات Blazor: مدیریت Agent، گالری تصاویر، لیست درخواست‌ها
6. پروژه WPF Agent قابل اجرا
7. راهنمای اجرا (README) شامل نحوه تنظیم Connection String و اجرای هر دو پروژه

## 8. محدودیت‌ها و نکات مهم

- کد باید Build شود؛ از کد نیمه‌کاره یا placeholder پرهیز کن مگر جایی که صراحتاً اعلام می‌کنم بعداً تکمیل می‌شود
- خطاها و Exception ها باید مدیریت شوند (حداقل try/catch در نقاط حساس مثل I/O و اتصال دیتابیس)
- در صورت وجود فرض یا ابهام، فرض را به‌صورت خلاصه اعلام کن و ادامه بده؛ منتظر تأیید نمان مگر تصمیم مسیر پروژه را عوض کند

## 9. ویژگی‌های اختیاری (فاز بعدی — فقط اگر خواستم اضافه کن)

- Retry خودکار اسکن‌های ناموفق
- فشرده‌سازی تصاویر قبل از ذخیره
- Cache کردن Thumbnail
- خروجی PDF از مجموعه صفحات
- واترمارک روی تصاویر
- OCR
- Audit Log
- کنترل دسترسی مبتنی بر نقش (Role-based Access)

---

### نحوه استفاده
این متن را به‌عنوان اولین پیام در Claude Code (در پوشه خالی پروژه یا با `claude` در ترمینال) کپی و ارسال کن. اگر می‌خواهی مرحله‌به‌مرحله پیش بروی، می‌توانی بخش‌های 7 را یکی‌یکی درخواست بدهی به‌جای اینکه همه را یکجا بخواهی.