# ScanSystem — سیستم Enterprise اسکن مدارک

سیستم داخلی اسکن مدارک با معماری **Blazor Server + SignalR + WPF Agent + SQL Server (Dapper)**.
هر ایستگاه کاری یک Agent سبک اجرا می‌کند که با نام ماشین (Machine Name) شناسایی می‌شود،
درخواست‌های اسکن را لحظه‌ای دریافت کرده و صفحات را به‌محض اسکن به سرور Stream می‌کند.

صفحه اسکن می‌تواند از طریق Query String سه کد زمینه‌ای دریافت کند:
`relationCode`، `inquiryCode` و `softwareCode`. این کدها همراه درخواست اسکن ثبت می‌شوند،
هنگام Insert تصویر به رکورد `Images` منتقل می‌شوند، و در زمان لود گالری همان صفحه به‌عنوان فیلتر در نظر گرفته می‌شوند.
نمونه:

```text
/scan?relationCode=REL-100&inquiryCode=INQ-200&softwareCode=SW-300
```

> **طراحی داده لیست‌ها (طبق سفارش):** کوئری‌های لیست‌های جدولی (درخواست‌های اسکن و Agentها)
> با Dapper به‌صورت `System.Data.DataTable`/`DataRow` خوانده می‌شوند (`ExecuteReader` + `DataTable.Load`)
> و جدول‌ها در Blazor با پیمایش `DataRow`ها ساخته می‌شوند.
> Paging/Sorting/Filtering کاملاً سمت سرور و در T-SQL انجام می‌شود — بدون کتابخانه JS جانبی.
> تصاویر برخلاف این‌ها به‌صورت گالری کاشی‌ای (گرافیکی) نمایش داده می‌شوند.

```
Blazor Server (UI + API)
        │
     SignalR  (/scanhub)
        │
   ┌────┴─────────┐
Agent (WPF)    SQL Server
   │            (PddDocuments)
 Scanner       Images + Data (VARBINARY)
```

## ساختار Solution

| پروژه | نقش |
|---|---|
| `src/ScanSystem.Web` | Blazor Server (.NET 8) — صفحات مدیریت Agent، اسکن، گالری، درخواست‌ها + Web API + SignalR Hub |
| `src/ScanSystem.Agent` | WPF Worker (net8.0-windows) — Tray icon، Auto-Start، صف داخلی، اسکن WIA چندهدفه‌ای/Streaming |
| `src/ScanSystem.Shared` | لایه مشترک — Entityها، Repositoryهای Dapper، ساخت Thumbnail |
| `database/01_CreateDatabase.sql` | اسکریپت کامل T-SQL ساخت دیتابیس و جداول |

> فایل `ScanSystem.slnx` (VS 2022 17.13+) و `ScanSystem.sln` (کلاسیک) هر دو موجودند.

---

## پیش‌نیازها

- **.NET 8 SDK**
- **SQL Server** (محلی یا شبکه) + دسترسی `sa` یا کاربر با حق CREATE/INSERT/UPDATE/DELETE
- **Windows** برای اجرای Agent و ساخت Thumbnail (وابستگی `System.Drawing.Common`)

---

## ۱) راه‌اندازی دیتابیس

```bat
sqlcmd -S . -E -i database\01_CreateDatabase.sql
```

یا اسکریپت `database/01_CreateDatabase.sql` را در SSMS اجرا کنید.
اسکریپت Idempotent است و دیتابیس **PddDocuments** را با ۵ جدول می‌سازد:
`Agents`, `ScanRequests`, `Images`, `ImageGroups`, `ImageGroupItems`.
در جدول‌های `ScanRequests` و `Images` سه ستون زمینه‌ای `RelationCode`، `InquiryCode` و `SoftwareCode`
به‌صورت nullable وجود دارد. اگر دیتابیس از قبل ساخته شده باشد، همین اسکریپت ستون‌های جدید و ایندکس‌های لازم را با `ALTER TABLE`/`sys.indexes` اضافه می‌کند.

## ۲) تنظیم Connection String

فایل `src/ScanSystem.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "Default": "Server=.;Database=PddDocuments;User Id=sa;Password=YOUR_PASSWORD;Encrypt=False;TrustServerCertificate=True"
}
```

## ۳) اجرای وب (سرور)

```bat
cd src\ScanSystem.Web
dotnet run
```

سرور به‌صورت پیش‌فرض روی **http://localhost:5002** اجرا می‌شود (قابل تغییر در `Properties/launchSettings.json`).

صفحات:

| مسیر | کاربرد |
|---|---|
| `/` | داشبورد |
| `/scan` | شروع اسکن + گالری اسکن‌ها؛ پشتیبانی از `relationCode`، `inquiryCode` و `softwareCode` در Query String |
| `/requests` | لیست درخواست‌ها — جدول Server-side با Paging/Sorting/Filtering در T-SQL (خروجی `System.Data.DataTable`) |
| `/gallery` | گالری Lazy Load، انتخاب چندگانه، ZIP، چرخش/جایگزینی/حذف، گروه‌بندی Drag & Drop |
| `/agents` | مدیریت Agentها (آنلاین/آفلاین + دانلود + غیرفعال‌سازی) |
| `/setup-guide` | راهنمای نصب Agent |

> در صفحه `/scan` لیست درخواست‌های اسکن به کاربر نمایش داده نمی‌شود؛ وضعیت داخلی همچنان از طریق SignalR و دیتابیس مدیریت می‌شود.

## ۴) اجرای Agent (کلاینت)

روی کامپیوتر متصل به اسکنر:

```bat
cd src\ScanSystem.Agent
dotnet run
```

- Agent با **Machine Name** ثبت می‌شود و آیکون آن در **Tray** قرار می‌گیرد.
- «شروع خودکار با ویندوز» از منوی Tray یا `"AutoStart": true` در `agentsettings.json`.
- شروع مستقیم در Tray: `ScanSystem.Agent.exe --tray`
- از پنجره Agent یا منوی Tray → **«تنظیمات اسکنر»**: لیست دستگاه‌های WIA شناسایی‌شده روی سیستم
  نمایش داده می‌شود و می‌توانید مشخص کنید Agent با کدام اسکنر کار کند (`SelectedScannerId`).
- تنظیمات در `agentsettings.json` کنار exe:

```json
{
  "ServerUrl": "http://SERVER-IP:5002/scanhub",
  "AutoConnect": true,
  "StartMinimized": false,
  "AutoStart": false,
  "MaxPages": 50,
  "SelectedScannerId": "",
  "UsePlaceholderWhenNoScanner": false,
  "SkipBlankPages": false
}
```

- **رفتار پیش‌فرض جدید:** اگر اسکنری (یا اسکنر انتخاب‌شده) در دسترس نباشد، Agent دیگر به‌صورت خودکار
  تصویر تستی/شبیه‌سازی‌شده نمی‌سازد. به‌جای آن به سرور خطای **«اسکنر تنظیم نیست»** گزارش می‌کند و
  در صفحه `/scan` یک بنر هشدار نمایش داده می‌شود.
  با فعال کردن تیک **«در صورت نبود اسکنر، تصویر آزمایشی (Placeholder) بسازد»** در «تنظیمات اسکنر»
  (`UsePlaceholderWhenNoScanner: true`) می‌توانید رفتار قدیمی (ساخت صفحه تستی) را دوباره فعال کنید.
- با فعال کردن تیک **«اگر صفحه اسکن‌شده کاملاً سفید/خالی بود، به سرور ارسال نشود»**
  (`SkipBlankPages: true`) صفحاتی که کاملاً یکدست/سفید تشخیص داده شوند (مثلاً برگ خالی در ADF)
  اصلاً به سرور آپلود نمی‌شوند.
- اسکن چندصفحه‌ای (ADF): حلقه تا پایان Feeder؛ هر صفحه بلافاصله با `UploadPage` ارسال و در پایان `CompleteScan` صدا زده می‌شود.


## ۵) توزیع Agent

### الف) ZIP
```bat
dotnet publish src\ScanSystem.Agent\ScanSystem.Agent.csproj -c Release -r win-x64 --self-contained false -o publish\agent
```
محتوای `publish\agent` را با نام **ScanSystem.Agent.zip** فشرده کنید و در پوشه
`src/ScanSystem.Web/downloads/` کنار فایل‌های اجرایی وب قرار دهید.
دکمه «دانلود» در صفحه `/agents` از endpoint `GET /api/agent/download.zip` سرو می‌شود.

### ب) ClickOnce
پروفایل آماده: `src/ScanSystem.Agent/Properties/PublishProfiles/ClickOnce.pubxml`
(در Visual Studio: Publish → پروفایل ClickOnce). خروجی در `publish/client` قرار می‌گیرد؛
`InstallUrl` را مطابق سرور خود تنظیم کنید. آپدیت‌های بعدی خودکار است.

---

## API (مهم‌ترین endpointها)

| متد | مسیر | توضیح |
|---|---|---|
| GET | `/api/images?skip=0&take=20&groupId=&machineName=&relationCode=&inquiryCode=&softwareCode=` | گالری (Lazy Load — OFFSET/FETCH) با امکان فیلتر بر اساس کدهای زمینه‌ای |
| GET | `/api/images/{id}` | تصویر اصلی |
| GET | `/api/images/thumb/{id}` | Thumbnail |
| POST | `/api/images/{id}/rotate` | چرخش ۹۰/۱۸۰/۲۷۰ — body: `{"angle":90}` |
| POST | `/api/images/{id}/replace` | جایگزینی تصویر (multipart، فیلد `file`) |
| DELETE | `/api/images/{id}` | حذف تصویر |
| POST | `/api/images/assignGroup` | تخصیص تصویر به گروه |
| POST | `/api/zip` | دانلود دسته‌ای ZIP — body: `{"ids":["..."]}` |
| GET | `/api/agents` / DELETE `/api/agents/{id}` | لیست/حذف Agent |
| GET | `/api/agent/download.zip` | دانلود ZIP برنامه Agent |

## قرارداد SignalR Hub (`/scanhub`)

| جهت | متد | آرگومان‌ها |
|---|---|---|
| Client→Server | `RegisterAgent` | `machineName` |
| Client→Server | `RequestScan` | `machineName, isMultiPage` (برای سازگاری با Agent/کلاینت قدیمی؛ بدون کد زمینه‌ای) |
| Client→Server | `RequestScanWithContext` | `machineName, isMultiPage, relationCode, inquiryCode, softwareCode` |
| Client→Server | `StartProcessing` / `CompleteScan` / `ReportError` | `requestId[, message]` |
| Client→Server | `UploadPage` | `requestId, fileName, contentType, data, pageNumber` |
| Server→All | `AgentStatusChanged`, `AgentsChanged`, `RequestsChanged`, `GalleryChanged` | — |
| Server→All | `PageUploaded` | `requestId, imageId, pageNumber` |
| Server→All | `ScanError` | `requestId, message` — از جمله پیام «اسکنر تنظیم نیست.» وقتی Agent دستگاهی پیدا نکند |
| Server→Agent | `ScanRequested` | `machineName, requestId, isMultiPage` |

## نکات فنی

- **بدون EF Core** — همه دسترسی‌ها با Dapper و کوئری‌های Parametrized.
- تصاویر به‌صورت `VARBINARY(MAX)` + Thumbnail خودکار (JPEG 240×320) ذخیره می‌شوند.
- کدهای `RelationCode`، `InquiryCode` و `SoftwareCode` ابتدا روی `ScanRequests` ذخیره می‌شوند و هنگام `UploadPage`/Insert تصویر در `Images` نیز ثبت می‌شوند؛ لود گالری می‌تواند دقیقاً بر اساس همین کدها فیلتر شود.
- `ConnectionId`های SignalR در حافظه (`AgentConnectionRegistry`) نگه‌داری می‌شوند؛ قطع Agent → `IsOnline=false` خودکار.
- حداکثر حجم آپلود: 100MB (Kestrel + FormOptions) — قابل تغییر در `Program.cs`.
- صفحه `/scan` از یک لایوت مینیمال (`BlankLayout`) و فایل CSS مستقل (`wwwroot/css/scan.css`، همه کلاس‌ها با پیشوند `sa-`) استفاده می‌کند و هیچ استایلی روی `html`/`body` اعمال نمی‌کند؛ به همین دلیل می‌توان آن را داخل یک پروژه دیگر (مثلاً به‌صورت iframe/صفحه جاسازی‌شده) بدون تداخل استایل استفاده کرد. تنها وابستگی ظاهری آن Bootstrap 5 + Bootstrap Icons است که باید در صفحهٔ میزبان لود شده باشند.

## فازهای بعدی (اختیاری طبق سفارش)

Retry خودکار، فشرده‌سازی تصاویر، خروجی PDF، واترمارک، OCR، Audit Log، کنترل دسترسی مبتنی بر نقش.

