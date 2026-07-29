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
   │            (PddDocuments)
 Scanner       Images + Data (VARBINARY)
```

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
- دریافت اطلاعات از طریق Query String:
  ```text
  /scan?relationCode=REL-100&inquiryCode=INQ-200&softwareCode=SW-300&fullName=علی+رضایی
  ```
- نمایش اطلاعات زمینه (اینکویری، نام، سافتور، ریلیشن) با آیکون
- انتخاب دستگاه اسکنر (Agent) از لیست Agentهای آنلاین
- دکمه شروع اسکن → ارسال درخواست به Agent انتخاب‌شده
- ذخیره تصویر با کدهای صحیح (از جدول ScanRequests کپی می‌شود)

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

اسکریپت Idempotent است و دیتابیس **PddDocuments** را با ۵ جدول می‌سازد:
`Agents`, `ScanRequests`, `Images`, `ImageGroups`, `ImageGroupItems`.

ستون‌های اضافه‌شده در جدول `ScanRequests`:
- `RelationCode` NVARCHAR(100) NULL
- `InquiryCode` NVARCHAR(100) NULL
- `SoftwareCode` NVARCHAR(100) NULL
- `FullName` NVARCHAR(200) NULL

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
| `/scan` | فرم اسکن — دریافت اطلاعات از Query String |

## ۴) اجرای Agent (کلاینت)

روی کامپیوتر متصل به اسکنر:

```bat
cd src\ScanSystem.Agent
dotnet run
```

- Agent با **Machine Name** ثبت می‌شود و آیکون آن در **Tray** قرار می‌گیرد.
- از منوی Tray → **تنظیمات اسکنر**: انتخاب دستگاه WIA
- تنظیمات در `agentsettings.json`:
  ```json
  {
    "ServerUrl": "http://SERVER-IP:5002/scanhub",
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
| Client→Server | `RequestScanWithContext` | `machineName, isMultiPage, relationCode, inquiryCode, softwareCode, fullName` |
| Client→Server | `StartProcessing` / `CompleteScan` / `ReportError` | `requestId[, message]` |
| Client→Server | `UploadPage` | `requestId, fileName, contentType, data, pageNumber` |
| Server→All | `AgentStatusChanged`, `AgentsChanged`, `RequestsChanged` | — |
| Server→All | `ScanCompleted`, `StatusChanged`, `ScanError` | `requestId[, message]` |
| Server→Agent | `ScanRequested` | `machineName, requestId, isMultiPage` |

---

## نکات فنی

- **بدون EF Core** — همه دسترسی‌ها با Dapper و کوئری‌های Parametrized.
- تصاویر به‌صورت `VARBINARY(MAX)` + Thumbnail خودکار ذخیره می‌شوند.
- کدهای `RelationCode`، `InquiryCode`، `SoftwareCode` و `FullName` روی `ScanRequests` ذخیره می‌شوند و هنگام Insert تصویر، سه کد اول به `Images` کپی می‌شوند.
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
