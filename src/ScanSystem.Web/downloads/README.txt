پوشه توزیع Agent
==================
فایل ScanSystem.Agent.zip را در این پوشه قرار دهید تا از صفحه «مدیریت Agentها»
و مسیر /api/agent/download.zip قابل دانلود باشد.

نحوه ساخت:
  1) در ماشین ویندوزی:  dotnet publish src\ScanSystem.Agent\ScanSystem.Agent.csproj -c Release -r win-x64 --self-contained false -o publish\agent
  2) محتوای publish\agent را با نام ScanSystem.Agent.zip فشرده کنید.
  3) فایل ZIP را در این پوشه (کنار فایل‌های اجرایی وب) کپی کنید.

توضیح: این پوشه جزو wwwroot نیست و فقط توسط endpoint دانلود Agent خوانده می‌شود.
