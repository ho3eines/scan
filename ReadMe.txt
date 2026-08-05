
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
  /         → لیست اسکن‌ها (سافتور کد، نوع تصویر، ریلیشن کد، کد کاربر، وضعیت، تصاویر)
              + فرم افزودن اسکن جدید
  /scan     → فرم اسکن — اطلاعات از مسیر URL دریافت می‌شود:
              /scan/{SoftwareCode}/{PicType}/{RelationCode}/{UserCode}
              /scan/SW-2024/Card/REL-100/U-200   (پارامتر خالی: «-»)

---

دیتابیس (متصل به دیتابیس اصلی پروژه — Schema: PDDImage):
  PDDImage.ImagesTable       → جدول اصلی عکس (فایل در ImageField) — دقیقاً مطابق DDL پروژه اصلی
  PDDImage.BaseImageGroups   → جدول گروه‌ها (دقیقاً مطابق DDL پروژه اصلی) — ارتباط 1 به n
  PDDImage.ImageThumbnails   → Thumbnail ها (جدا از جدول اصلی به دلیل حجم بالا)
  PDDImage.Agents            → دستگاه‌های اسکنر (داخلی)
  PDDImage.ScanRequests      → درخواست‌های اسکن (داخلی)
  PDDImage.ScanRequestImages → ارتباط درخواست ↔ تصویر (داخلی)

  - تاریخ (Date) و ساعت (ScanTime) به‌صورت شمسی ذخیره می‌شوند (مثلاً 1404/05/14 و 14:35)
  - حذف تصویر از جدول اصلی به‌صورت نرم است (ISDELETED = 1)

---

جریان کار:
  ۱. کاربر در صفحه لیست (/) اطلاعات را وارد و دکمه «افزودن و اسکن» را می‌زند
  ۲. به صفحه اسکن (/scan/SoftwareCode/PicType/RelationCode/UserCode) منتقل می‌شود
  ۳. کاربر دستگاه اسکنر (Agent) را انتخاب و دکمه «شروع اسکن» را می‌زند
  ۴. Hub درخواست را فقط به Agent انتخاب‌شده ارسال می‌کند
  ۵. Agent اسکن فیزیکی انجام می‌دهد و تصاویر را آپلود می‌کند
  ۶. تصاویر با کدهای صحیح (از ScanRequests کپی) در ImagesTable ذخیره می‌شوند و
     Thumbnail هر تصویر در ImageThumbnails ذخیره می‌شود
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
