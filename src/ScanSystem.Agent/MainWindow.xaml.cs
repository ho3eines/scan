using System.Threading.Channels;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;

namespace ScanSystem.Agent;

/// <summary>
/// پنجره اصلی Agent (پنجره تشخیص/لاگ). طبق طراحی، UI اصلی در Tray است:
/// - آیکون Tray با منو (نمایش / تنظیمات اسکنر / شروع خودکار / خروج)
/// - صف داخلی (Channel) برای سریال‌سازی درخواست‌های اسکن
/// - اسکن چندصفحه‌ای تا پایان Feeder و ارسال هر صفحه به‌محض آماده شدن (Streaming)
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>یک job اسکن در صف داخلی.</summary>
    private sealed record ScanJob(Guid RequestId, bool MultiPage);

    private HubConnection? _connection;
    // Agent با «نام ماشین» نزد سرور ثبت می‌شود — همان شناسه‌ای که Hub برای ارسال ScanRequested استفاده می‌کند.
    private readonly string _machineName = Environment.MachineName;
    private readonly WiaScannerService _wia = new();
    private readonly AgentSettings _settings = AgentSettings.Load();

    // صف داخلی درخواست‌های اسکن (سریال‌سازی + Auto-reconnect-safe)
    private readonly Channel<ScanJob> _queue = Channel.CreateUnbounded<ScanJob>();
    private readonly CancellationTokenSource _cts = new();
    private Task? _worker;

    // Tray
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;

    public MainWindow(bool startHidden)
    {
        InitializeComponent();

        txtServerUrl.Text = _settings.ServerUrl;
        ShowInTaskbar = !startHidden;

        CreateTrayIcon();

        // اعمال AutoStart طبق تنظیمات
        try { AutoStartHelper.Set(_settings.AutoStart); } catch { }

        Loaded += async (_, _) =>
        {
            Log($"pdd Scan شروع به کار کرد. نام ماشین: {_machineName}");
            DetectAndShowScanner();
            StartWorker();
            if (_settings.AutoConnect) await ConnectAsync();
        };
    }

    // ───────────────────────── اتصال به سرور ─────────────────────────

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            try { await _connection.StopAsync(); } catch { }
            UpdateStatus(false);
            return;
        }
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            btnConnect.IsEnabled = false;

            // ذخیره آدرس واردشده
            _settings.ServerUrl = txtServerUrl.Text.Trim();
            AgentSettings.Save(_settings);

            Log("در حال اتصال به سرور...");

            _connection = new HubConnectionBuilder()
                .WithUrl(_settings.ServerUrl)
                .WithAutomaticReconnect() // Auto-reconnect طبق نیازمندی
                .Build();

            // قرارداد Hub: ScanRequested(machineName, requestId, isMultiPage)
            _connection.On<string, Guid, bool>("ScanRequested", (machineName, requestId, isMultiPage) =>
            {
                if (!string.Equals(machineName, _machineName, StringComparison.OrdinalIgnoreCase))
                    return; // درخواست برای ماشین دیگری است
                Log($"درخواست اسکن دریافت شد: {requestId.ToString()[..8]} (چندصفحه‌ای={isMultiPage}) — به صف اضافه شد.");
                _queue.Writer.TryWrite(new ScanJob(requestId, isMultiPage));
            });

            _connection.On<Guid, string>("StatusChanged", (requestId, status) =>
                Log($"وضعیت {requestId.ToString()[..8]} ← {status}"));

            _connection.Closed += async _ =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatus(false);
                    Log("اتصال با سرور قطع شد (تلاش خودکار برای اتصال مجدد...).");
                });
            };

            _connection.Reconnected += async _ =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Log("اتصال مجدد برقرار شد؛ ثبت‌نام دوباره انجام می‌شود.");
                });
                // ConnectionId جدید است → باید دوباره RegisterAgent صدا زده شود.
                var registered = await RegisterAsync();
                await Dispatcher.InvokeAsync(() => UpdateStatus(registered));
                if (!registered)
                {
                    await Dispatcher.InvokeAsync(() => Log("ثبت‌نام پس از اتصال مجدد انجام نشد؛ اتصال برای تلاش بعدی بسته می‌شود."));
                    try { await _connection.StopAsync(); } catch { }
                }
            };

            await _connection.StartAsync();
            Log("اتصال برقرار شد.");

            var registered = await RegisterAsync();
            UpdateStatus(registered);

            if (!registered)
            {
                Log("اتصال برقرار است، اما سرور Agent را ثبت نکرد. اتصال بسته می‌شود تا تلاش بعدی تمیز انجام شود.");
                try { await _connection.StopAsync(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"خطای اتصال: {ex.Message}");

            System.Windows.MessageBox.Show($"خطا در اتصال:\n{ex.Message}", "pdd Scan",
                MessageBoxButton.OK, MessageBoxImage.Error);

            UpdateStatus(false);
        }
        finally
        {
            btnConnect.IsEnabled = true;
        }
    }

    private async Task<bool> RegisterAsync(int maxAttempts = 5)
    {
        if (_connection is null) return false;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // قرارداد Hub: RegisterAgent(machineName)
                await _connection.InvokeAsync("RegisterAgent", _machineName);
                Log($"ثبت‌نام انجام شد: {_machineName}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"ثبت‌نام ناموفق بود (تلاش {attempt}/{maxAttempts}): {ex.Message}");
                if (attempt < maxAttempts)
                    await Task.Delay(TimeSpan.FromMilliseconds(1200));
            }
        }

        return false;
    }

    // ───────────────────────── صف داخلی + حلقه اسکن ─────────────────────────

    private void StartWorker()
    {
        // مصرف‌کننده تک‌رشته‌ای: درخواست‌ها به ترتیب و بدون تداخل اجرا می‌شوند.
        _worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var job in _queue.Reader.ReadAllAsync(_cts.Token))
                {
                    await ProcessScanAsync(job);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log($"خطای worker: {ex.Message}"); }
        });
    }

    private async Task ProcessScanAsync(ScanJob job)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            Log("اتصال با سرور برقرار نیست؛ job نادیده گرفته شد.");
            return;
        }

        try
        {
            // اعلام شروع پردازش به سرور
            await _connection.InvokeAsync("StartProcessing", job.RequestId);
            Log("پردازش شروع شد...");

            int maxPages = job.MultiPage ? Math.Max(1, _settings.MaxPages) : 1;
            using var session = _wia.CreateSession(
                _machineName,
                maxPages,
                allowBlankPlaceholder: _settings.UsePlaceholderWhenNoScanner,
                preferredDeviceId: _settings.SelectedScannerId);

            int pageNumber = 0;
            int skippedBlank = 0;
            byte[]? data;

            // حلقه اسکن چندصفحه‌ای تا پایان Feeder — هر صفحه به‌محض اسکن ارسال می‌شود (Streaming).
            while ((data = session.NextPage()) is not null)
            {
                if (_settings.SkipBlankPages && WiaScannerService.IsBlankPage(data))
                {
                    skippedBlank++;
                    await Dispatcher.InvokeAsync(() => Log($"صفحه سفید/خالی شناسایی شد و ارسال نشد (مجموع نادیده‌گرفته‌شده: {skippedBlank})."));
                    continue;
                }

                pageNumber++;
                var fileName = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}_p{pageNumber}.jpg";
                int size = data.Length;

                await _connection.InvokeAsync("UploadPage", job.RequestId, fileName, "image/jpeg", data, pageNumber);

                await Dispatcher.InvokeAsync(() =>
                {
                    txtLastScan.Text = $"صفحه {pageNumber} ارسال شد | {DateTime.Now:HH:mm:ss} | {size:N0} بایت";
                    Log($"صفحه {pageNumber} آپلود شد ({size:N0} بایت).");
                });
            }

            // اسکنری یافت نشد و اجازه تصویر آزمایشی هم داده نشده → به سرور اعلام می‌کنیم «اسکنر تنظیم نیست».
            if (session.ScannerMissing)
            {
                await _connection.InvokeAsync("ReportError", job.RequestId, "اسکنر تنظیم نیست.");
                Log("خطا: اسکنر تنظیم نیست (هیچ دستگاهی یافت نشد و تصویر آزمایشی غیرفعال است).");
                return;
            }

            if (pageNumber == 0)
            {
                var reason = skippedBlank > 0
                    ? "تمام صفحات اسکن‌شده سفید/خالی بودند و ارسال نشدند."
                    : "هیچ صفحه‌ای اسکن نشد.";
                await _connection.InvokeAsync("ReportError", job.RequestId, reason);
                Log($"خطا: {reason}");
                return;
            }

            // اعلام پایان موفق → وضعیت درخواست Done می‌شود.
            await _connection.InvokeAsync("CompleteScan", job.RequestId);
            await Dispatcher.InvokeAsync(() => Log($"اسکن کامل شد: {pageNumber} صفحه ارسال شد" +
                (skippedBlank > 0 ? $" ({skippedBlank} صفحه سفید نادیده گرفته شد)." : ".")));
        }
        catch (Exception ex)
        {
            Log($"خطای اسکن: {ex.Message}");
            try
            {
                await _connection.InvokeAsync("ReportError", job.RequestId, ex.Message);
            }
            catch { }
        }
    }




    // ───────────────────────── Tray Icon ─────────────────────────

    private void CreateTrayIcon()
    {
        try
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "pdd Scan",
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip
            {
                // منوی فارسی و راست‌چین
                RightToLeft = System.Windows.Forms.RightToLeft.Yes,
                Font = new System.Drawing.Font("Segoe UI", 9F)
            };

            menu.Items.Add("نمایش پنجره", null, (_, _) => ShowWindow());

            menu.Items.Add("تنظیمات اسکنر", null, (_, _) => Dispatcher.Invoke(() =>
            {
                ShowWindow();
                BtnScannerSettings_Click(this, new RoutedEventArgs());
            }));

            var autoStartItem = new System.Windows.Forms.ToolStripMenuItem("شروع خودکار با ویندوز")
            {
                Checked = AutoStartHelper.IsEnabled()
            };
            autoStartItem.Click += (_, _) =>
            {
                var enable = !AutoStartHelper.IsEnabled();
                AutoStartHelper.Set(enable);
                autoStartItem.Checked = enable;
                _settings.AutoStart = enable;
                AgentSettings.Save(_settings);
                Log(enable ? "شروع خودکار با ویندوز فعال شد." : "شروع خودکار با ویندوز غیرفعال شد.");
            };
            menu.Items.Add(autoStartItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("خروج", null, (_, _) => ExitApp());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowWindow();
        }
        catch (Exception ex)
        {
            Log($"خطا در ساخت Tray icon: {ex.Message}");
        }
    }

    /// <summary>
    /// آیکون برنامه (pdd Scan) از Resource داخل اسمبلی (appicon.ico) خوانده می‌شود؛
    /// در صورت هر خطا، آیکون برنامه‌ای ساده به‌عنوان جایگزین ساخته می‌شود.
    /// </summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var sri = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/appicon.ico", UriKind.Absolute));
            if (sri?.Stream is not null)
            {
                using var stream = sri.Stream;
                using var icon = new System.Drawing.Icon(stream, 32, 32);
                return (System.Drawing.Icon)icon.Clone();
            }
        }
        catch { }
        return CreateTrayIconArt();
    }

    /// <summary>ساخت آیکون Tray به‌صورت برنامه‌ای (جایگزین — بدون نیاز به فایل ico).</summary>
    private static System.Drawing.Icon CreateTrayIconArt()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(27, 110, 194));
            g.FillRectangle(bg, 1, 1, 30, 30);
            g.DrawRectangle(System.Drawing.Pens.White, 5, 9, 22, 16);
            g.FillEllipse(System.Drawing.Brushes.White, 11, 12, 10, 10);
            g.FillEllipse(System.Drawing.Brushes.DodgerBlue, 13, 14, 6, 6);
        }
        var handle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
    }

    private void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        try { _cts.Cancel(); } catch { }
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        try { _connection?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch { }
        System.Windows.Application.Current.Shutdown();
    }

    // ───────────────────────── چرخه عمر پنجره ─────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // بستن پنجره → مخفی شدن در Tray (خروج واقعی فقط از منوی Tray).
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            Log("پنجره مخفی شد؛ Agent در Tray فعال است.");
        }
        else
        {
            base.OnClosing(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        try { _connection?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch { }
        base.OnClosed(e);
    }

    // ───────────────────────── UI helpers ─────────────────────────

    private void DetectAndShowScanner()
    {
        bool hasScanner = false;
        try { hasScanner = _wia.DetectScanner(_settings.SelectedScannerId); } catch { }

        if (hasScanner)
        {
            txtScanner.Text = "اسکنر شناسایی شد ✓";
        }
        else if (_settings.UsePlaceholderWhenNoScanner)
        {
            txtScanner.Text = "اسکنر یافت نشد — حالت تصویر آزمایشی فعال است.";
        }
        else
        {
            txtScanner.Text = "اسکنر تنظیم نیست — از «تنظیمات اسکنر» یک دستگاه انتخاب کنید.";
        }
    }

    private void BtnScannerSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new ScannerSettingsWindow(_wia, _settings) { Owner = this };
        if (win.ShowDialog() == true && win.SettingsChanged)
        {
            Log("تنظیمات اسکنر ذخیره شد.");
            DetectAndShowScanner();
        }
    }


    private void UpdateStatus(bool connected)
    {
        // رنگ‌های هماهنگ با طراحی جدید کارت وضعیت
        var onlineBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));   // #16A34A
        var offlineBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));   // #DC2626

        if (connected)
        {
            txtStatus.Text = "آنلاین";
            txtStatus.Foreground = onlineBrush;
            statusDot.Fill = onlineBrush;
            btnConnect.Content = "قطع اتصال";
        }
        else
        {
            txtStatus.Text = "آفلاین";
            txtStatus.Foreground = offlineBrush;
            statusDot.Fill = offlineBrush;
            btnConnect.Content = "اتصال";
        }
    }

    private void Log(string message)
    {
        void Append()
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            txtLog.ScrollToEnd();
        }
        if (Dispatcher.CheckAccess()) Append();
        else Dispatcher.Invoke(Append);
    }
}
