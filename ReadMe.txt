
سیستم Enterprise اسکن مدارک — pdd Scan
=========================================

معماری: Blazor Server + SignalR + WPF Agent + SQL Server (Dapper)

---

ساختار پروژه:
  src/ScanSystem.Web    → Blazor Server — صفحه لیست + فرم اسکن + SignalR Hub
  src/ScanSystem.Agent  → WPF Agent — Tray icon + صف اسکن + WIA Scanner
  src/ScanSystem.Shared → لایه مشترک — Dapper + Entity + Thumbnail

---

صفحات وب:
  /         → لیست اسکن‌ها (اینکویری کد، نام، سافتور کد، ریلیشن کد، وضعیت، تصاویر)
              + فرم افزودن اسکن جدید
  /scan     → فرم اسکن — اطلاعات از Query String دریافت می‌شود:
              /scan?relationCode=REL-100&inquiryCode=INQ-200&softwareCode=SW-300&fullName=علی

---

جریان کار:
  ۱. کاربر در صفحه لیست (/) اطلاعات را وارد و دکمه «افزودن و اسکن» را می‌زند
  ۲. به صفحه اسکن (/scan) منتقل می‌شود — اطلاعات به‌صورت Query String پاس داده می‌شود
  ۳. کاربر دستگاه اسکنر (Agent) را انتخاب و دکمه «شروع اسکن» را می‌زند
  ۴. Hub درخواست را فقط به Agent انتخاب‌شده ارسال می‌کند
  ۵. Agent اسکن فیزیکی انجام می‌دهد و تصاویر را آپلود می‌کند
  ۶. تصاویر با کدهای صحیح (از ScanRequests کپی) در DB ذخیره می‌شوند
  ۷. تمام کاربران متصل از طریق SignalR لیست را بروز می‌بینند

---

چند Agent همزمان:
  - هر Agent با MachineName شناسایی و فقط درخواست خودش را دریافت می‌کند
  - AgentConnectionRegistry از ConcurrentDictionary استفاده می‌کند (thread-safe)
  - صف داخلی Agent از Channel<ScanJob> استفاده می‌کند (سریال‌سازی)
  - Guid.NewGuid() برای هر درخواست — بدون تداخل

---

راه‌اندازی:
  ۱. اسکریپت database/01_CreateDatabase.sql را اجرا کنید
  ۲. Connection String را در appsettings.json تنظیم کنید
  ۳. سرور: cd src/ScanSystem.Web && dotnet run
  ۴. Agent: cd src/ScanSystem.Agent && dotnet run (روی کامپیوتر متصل به اسکنر)

---

جدول ScanRequests ستون‌های زمینه‌ای:
  RelationCode   NVARCHAR(100) NULL
  InquiryCode    NVARCHAR(100) NULL
  SoftwareCode   NVARCHAR(100) NULL
  FullName       NVARCHAR(200) NULL

جدول Images سه کد اول را از ScanRequests کپی می‌کند (هنگام INSERT از طریق JOIN).

---

Agent:
  - بدون دکمه اسکن دستی — تمام اسکن‌ها از وب ارسال می‌شوند
  - منوی Tray: نمایش پنجره / تنظیمات (اسکنر و اتصال) / شروع خودکار با ویندوز / خروج
  - قابلیت تنظیم مهلت پاسخ سرور (Time Out) در پنجره اصلی و پنجره تنظیمات (پیش‌فرض 120 ثانیه) برای جلوگیری از خطای Server timeout در SignalR
  - تنظیمات: agentsettings.json (ServerUrl, ServerTimeoutSeconds, AutoConnect, MaxPages, SelectedScannerId, ...)
  - اگر اسکنر تنظیم نباشد → خطای «اسکنر تنظیم نیست» نمایش داده می‌شود
