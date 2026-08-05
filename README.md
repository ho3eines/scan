# ScanSystem — سیستم Enterprise اسکن مدارک

سیستم داخلی اسکن مدارک با معماری **Blazor Server + SignalR + WPF Agent + SQL Server (Dapper)**.

هر ایستگاه کاری یک Agent سبک اجرا می‌کند که با نام ماشین (Machine Name) شناسایی می‌شود،
درخواست‌های اسکن را لحظه‌ای دریافت کرده و صفحات را به‌محض اسکن به سرور Stream می‌کند.

```
Blazor Server (UI + API)
        │
     SignalR  (/scanhub)
        │
   ┌────┴─────────┐
Agent (WPF)    SQL Server
   │            (دیتابیس اصلی پروژه)
 Scanner       PDDImage.ImagesTable + ImageThumbnails
```

سیستم به **دیتابیس اصلی پروژه** (Schema: `PDDImage`) متصل می‌شود:
- فایل اصلی تصویر در جدول اصلی **`PDDImage.ImagesTable`** (ستون `ImageField`) ذخیره می‌شود.
- **Thumbnail ها** به دلیل حجم بالای جدول اصلی، در جدول جداگانه **`PDDImage.ImageThumbnails`** ذخیره می‌شوند.
- گروه‌بندی با جدول **`PDDImage.BaseImageGroups`** و ارتباط **1 به n** (ستون `ImageGroupID` در جدول تصاویر) انجام می‌شود.
- تاریخ تصویر به‌صورت **شمسی** (مثلاً `1404/05/14`) در ستون `Date` ذخیره می‌شود.

---

## ساختار Solution

| پروژه | نقش |
|---|---|
| `src/ScanSystem.Web` | Blazor Server (.NET 8) — صفحه لیست اسکن‌ها، فرم اسکن، مدیریت Agent + SignalR Hub |
| `src/ScanSystem.Agent` | WPF Worker (net8.0-windows) — Tray icon، Auto-Start، صف داخلی، اسکن WIA چندصفحه‌ای/Streaming |
| `src/ScanSystem.Shared` | لایه مشترک — Entityها، Repositoryهای Dapper، ساخت Thumbnail |
| `database/01_CreateDatabase.sql` | اسکریپت کامل T-SQL ساخت دیتابیس و جداول |

---

## جریان کار سیستم

### صفحه لیست (`/`)
- نمایش تمام درخواست‌های اسکن در جدول با ستون‌ها:
  **اینکویری کد** | **نام و نام خانوادگی** | **سافتور کد** | **ریلیشن کد** | **وضعیت** | **تاریخ** | **تعداد تصاویر**
- فرم افزودن اسکن جدید در بالای صفحه
- دکمه «اسکن» روی هر ردیف → انتقال به صفحه اسکن با اطلاعات آن ردیف
- بروزرسانی لحظه‌ای با SignalR (وقتی Agent اسکن انجام می‌دهد، لیست بروز می‌شود)

### صفحه اسکن (`/scan`)
- دریافت اطلاعات از مسیر URL:
  ```text
  /scan/{SoftwareCode}/{PicType}/{RelationCode}/{UserCode}
  /scan/SW-2024/Card/REL-100/U-200
  ```
  (پارامتر خالی با `-` پر می‌شود: `/scan/-/-/REL-100/-`)
- نمایش اطلاعات زمینه (سافتور کد، نوع تصویر، ریلیشن کد، کد کاربر) با آیکون
- انتخاب دستگاه اسکنر (Agent) از لیست Agentهای آنلاین
- دکمه شروع اسکن → ارسال درخواست به Agent انتخاب‌شده
- ذخیره تصویر در `PDDImage.ImagesTable` با کدهای صحیح (از جدول `ScanRequests` کپی می‌شود)
- **گالری** تمام تصاویرِ دارای همان `SoftwareCode / PicType / RelationCode` را نشان می‌دهد
  (بدون فیلتر دستگاه و بدون فیلتر `UserCode` — یعنی تصاویر همه کاربران و همه دستگاه‌ها با این سه کد دیده می‌شوند)

### Agent (WPF)
- بدون دکمه اسکن دستی — تمام اسکن‌ها از طریق وب ارسال می‌شوند
- دریافت درخواست از Hub فقط اگر `machineName` با نام ماشین Agent مطابقت داشته باشد
- صف داخلی (`Channel<ScanJob>`) برای سریال‌سازی درخواست‌ها
- اسکن چندصفحه‌ای تا پایان Feeder + ارسال هر صفحه به‌محض آماده شدن

---

## پشتیبانی از چند Agent همزمان

سیستم کاملاً از سناریوی **چند Agent + چند کاربر همزمان** پشتیبانی می‌کند:

| مکانیزم | توضیح |
|---|---|
| `ConcurrentDictionary` | نگاشت Agent ↔ ConnectionId کاملاً thread-safe |
| `Clients.Client(connectionId)` | هر درخواست اسکن فقط به Agent خاص ارسال می‌شود (unicast) |
| `string.Equals(machineName)` | Agent فقط درخواست‌هایی را قبول می‌کند که نام ماشینش مطابقت داشته باشد |
| `Channel<ScanJob>` | صف داخلی Agent — درخواست‌ها به ترتیب و بدون تداخل پردازش می‌شوند |
| `Guid.NewGuid()` | هر درخواست و تصویر ID منحصربه‌فرد دارد — بدون تداخل |
| کپی کدها از ScanRequests | تصاویر کدهای مربوط به درخواست خودشان را دریافت می‌کنند |

**مثال:** کاربر A روی Agent-1 اسکن با `REL-A` می‌زند، کاربر B همزمان روی Agent-2 اسکن با `REL-B` می‌زند — هر تصویر دقیقاً با کد خودش ذخیره می‌شود.

---

## پیش‌نیازها

- **.NET 8 SDK**
- **SQL Server** (محلی یا شبکه)
- **Windows** برای اجرای Agent

## ۱) راه‌اندازی دیتابیس

```bat
sqlcmd -S . -E -i database\01_CreateDatabase.sql
```

اسکریپت Idempotent است و دیتابیس **PddDocuments** را با جداول زیر آماده می‌کند
(جداول اصلی دقیقاً مطابق ساختار پروژه اصلی ساخته می‌شوند و اگر موجود باشند تغییری نمی‌کنند):

| جدول | نقش |
|---|---|
| `PDDImage.ImagesTable` | جدول اصلی عکس (دقیقاً مطابق DDL پروژه اصلی) — فایل در `ImageField` |
| `PDDImage.BaseImageGroups` | جدول گروه‌ها (دقیقاً مطابق DDL پروژه اصلی) — ارتباط 1 به n با `ImageGroupID` |
| `PDDImage.ImageThumbnails` | **Thumbnail ها** — جدا از جدول اصلی (کلید: `ImageId` = `ImagesTable.Id`) |
| `PDDImage.Agents` | دستگاه‌های اسکنر (داخلی سیستم اسکن) |
| `PDDImage.ScanRequests` | درخواست‌های اسکن (داخلی سیستم اسکن) |
| `PDDImage.ScanRequestImages` | ارتباط درخواست ↔ تصویر (چون جدول اصلی ستون RequestId ندارد) |

کدهای ذخیره‌شده روی هر درخواست (`ScanRequests`):
- `SoftwareCode` NVARCHAR(100) NULL
- `PicType` NVARCHAR(100) NULL
- `RelationCode` NVARCHAR(100) NULL
- `UserCode` NVARCHAR(100) NULL

این کدها هنگام ذخیره تصویر به `ImagesTable` کپی می‌شوند. حذف تصویر به‌صورت **نرم** (`ISDELETED = 1`)
انجام می‌شود چون جدول اصلی متعلق به پروژه اصلی است.

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

سرور به‌صورت پیش‌فرض روی **http://localhost:5002** اجرا می‌شود.

صفحات:

| مسیر | کاربرد |
|---|---|
| `/` | لیست اسکن‌ها + فرم افزودن جدید |
| `/scan` | فرم اسکن — دریافت کدها از مسیر: `/scan/{SoftwareCode}/{PicType}/{RelationCode}/{UserCode}` |

## ۴) اجرای Agent (کلاینت)

روی کامپیوتر متصل به اسکنر:

```bat
cd src\ScanSystem.Agent
dotnet run
```

- Agent با **Machine Name** ثبت می‌شود و آیکون آن در **Tray** قرار می‌گیرد.
- از منوی Tray → **تنظیمات (اسکنر و اتصال)**: انتخاب دستگاه WIA، تنظیم آدرس سرور و مهلت پاسخ (Time Out)
- امکان تنظیم مهلت پاسخ سرور (Time Out) در پنجره اصلی و پنجره تنظیمات برای جلوگیری از خطای `Server timeout (30000.00ms) elapsed without receiving a message from the server` در شبکه‌های کند یا پردازش‌های طولانی (پیش‌فرض ۱۲۰ ثانیه).
- تنظیمات در `agentsettings.json`:
  ```json
  {
    "ServerUrl": "http://SERVER-IP:5002/scanhub",
    "ServerTimeoutSeconds": 120,
    "AutoConnect": true,
    "AutoStart": false,
    "MaxPages": 50,
    "SelectedScannerId": "",
    "UsePlaceholderWhenNoScanner": false,
    "SkipBlankPages": false
  }
  ```
- اگر اسکنر تنظیم نباشد، Agent خطای «اسکنر تنظیم نیست» گزارش می‌کند.

## ۵) توزیع Agent

```bat
dotnet publish src\ScanSystem.Agent\ScanSystem.Agent.csproj -c Release -r win-x64 --self-contained false -o publish\agent
```

---

## قرارداد SignalR Hub (`/scanhub`)

| جهت | متد | آرگومان‌ها |
|---|---|---|
| Client→Server | `RegisterAgent` | `machineName` |
| Client→Server | `RequestScanWithContext` | `machineName, isMultiPage, softwareCode, picType, relationCode, userCode` |
| Client→Server | `StartProcessing` / `CompleteScan` / `ReportError` | `requestId[, message]` |
| Client→Server | `UploadPage` | `requestId, fileName, contentType, data, pageNumber` |
| Server→All | `AgentStatusChanged`, `AgentsChanged`, `RequestsChanged` | — |
| Server→All | `ScanCompleted`, `StatusChanged`, `ScanError` | `requestId[, message]` |
| Server→Agent | `ScanRequested` | `machineName, requestId, isMultiPage` |

---

## نکات فنی

- **بدون EF Core** — همه دسترسی‌ها با Dapper و کوئری‌های Parametrized.
- همه کوئری‌های SELECT از `WITH (NOLOCK)` استفاده می‌کنند (سیستم اسکن/نمایش — Dirty Read قابل قبول است و قفل‌گذاری روی جدول پرحجم اصلی حذف می‌شود).
- کوئری‌های گالری (WHERE پویا) `OPTION (RECOMPILE)` دارند تا برای هر ترکیب فیلتر پلن بهینه ساخته شود.
- ایندکس‌های گالری روی `PDDImage.ImagesTable` در `01_CreateDatabase.sql` تعریف شده‌اند:
  `IX_ImagesTable_Gallery (ISDELETED, SoftwareCode, PicType, RelationCode, Id DESC)`،
  `IX_ImagesTable_ISDELETED_Id (ISDELETED, Id DESC)` و `IX_ImagesTable_Group (ISDELETED, ImageGroupID, Id DESC)`.
- ذخیره هر صفحه (تصویر + Thumbnail + ارتباط درخواست) در **یک تراکنش** انجام می‌شود — یا همه ثبت می‌شوند یا هیچ‌کدام.
- تصاویر به‌صورت `VARBINARY(MAX)` در `PDDImage.ImagesTable.ImageField` و Thumbnail خودکار در جدول جداگانه `PDDImage.ImageThumbnails` ذخیره می‌شوند.
- تاریخ (`Date`) و ساعت (`ScanTime`) به‌صورت **شمسی** (مثلاً `1404/05/14` و `14:35`) ذخیره می‌شوند.
- کدهای `SoftwareCode`، `PicType`، `RelationCode` و `UserCode` روی `ScanRequests` ذخیره می‌شوند و هنگام Insert تصویر به `ImagesTable` کپی می‌شوند.
- گالری فقط بر اساس `SoftwareCode / PicType / RelationCode` فیلتر می‌شود — بدون فیلتر دستگاه (Agent) و بدون فیلتر `UserCode`.
- گروه‌بندی **1 به n** است: هر تصویر فقط یک گروه دارد (`ImagesTable.ImageGroupID → BaseImageGroups.ID`).
- حذف تصویر از جدول اصلی به‌صورت نرم (`ISDELETED = 1`) انجام می‌شود و Thumbnail آن پاک می‌شود.
- `AgentConnectionRegistry` از `ConcurrentDictionary` استفاده می‌کند — کاملاً thread-safe.
- Agent از `Channel<ScanJob>` برای سریال‌سازی درخواست‌ها استفاده می‌کند.
- حداکثر حجم آپلود: 100MB (Kestrel + FormOptions).
- صفحه `/scan` از `BlankLayout` و CSS مستقل (`css/scan.css`، پیشوند `sa-`) استفاده می‌کند.
- صفحه `/` از `MainLayout` و CSS مستقل (`css/index.css`، پیشوند `si-`) استفاده می‌کند.
- فونت آیکون‌ها (Bootstrap Icons) به‌صورت محلی سرو می‌شود — بدون نیاز به اینترنت.
- نام برند: **pdd Scan**

---

## فازهای بعدی (اختیاری)

Retry خودکار، فشرده‌سازی تصاویر، خروجی PDF، واترمارک، OCR، Audit Log، کنترل دسترسی مبتنی بر نقش.
