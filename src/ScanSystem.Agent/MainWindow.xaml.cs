using System.Threading.Channels;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;

namespace ScanSystem.Agent;

/// <summary>
/// پنجره اصلی Agent (پنجره تشخیص/لاگ). طبق طراحی، UI اصلی در Tray است:
/// - آیکون Tray با منو (نمایش / اسکن دستی / شروع خودکار / خروج)
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
            Log($"Agent شروع به کار کرد. نام ماشین: {_machineName}");
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
                await RegisterAsync();
                await Dispatcher.InvokeAsync(() => UpdateStatus(true));
            };

            await _connection.StartAsync();
            Log("اتصال برقرار شد.");

            await RegisterAsync();
            UpdateStatus(true);
        }
        catch (Exception ex)
        {
            Log($"خطای اتصال: {ex.Message}");
            MessageBox.Show($"خطا در اتصال:\n{ex.Message}", "خطا",
                MessageBoxButton.OK, MessageBoxImage.Error);

        }
        finally
        {
            btnConnect.IsEnabled = true;
        }
    }

    private async Task RegisterAsync()
    {
        if (_connection is null) return;
        try
        {
            // قرارداد Hub: RegisterAgent(machineName)
            await _connection.InvokeAsync("RegisterAgent", _machineName);
            Log($"ثبت‌نام انجام شد: {_machineName}");
        }
        catch (Exception ex)
        {
            Log($"ثبت‌نام ناموفق بود: {ex.Message}");
        }
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

        await Dispatcher.InvokeAsync(() => btnScan.IsEnabled = false);
        try
        {
            // اعلام شروع پردازش به سرور
            await _connection.InvokeAsync("StartProcessing", job.RequestId);
            Log("پردازش شروع شد...");

            int maxPages = job.MultiPage ? Math.Max(1, _settings.MaxPages) : 1;
            using var session = _wia.CreateSession(_machineName, maxPages);

            int pageNumber = 0;
            byte[]? data;

            // حلقه اسکن چندصفحه‌ای تا پایان Feeder — هر صفحه به‌محض اسکن ارسال می‌شود (Streaming).
            while ((data = session.NextPage()) is not null)
            {
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

            if (pageNumber == 0)
            {
                await _connection.InvokeAsync("ReportError", job.RequestId, "هیچ صفحه‌ای اسکن نشد.");
                Log("خطا: هیچ صفحه‌ای اسکن نشد.");
                return;
            }

            // اعلام پایان موفق → وضعیت درخواست Done می‌شود.
            await _connection.InvokeAsync("CompleteScan", job.RequestId);
            await Dispatcher.InvokeAsync(() => Log($"اسکن کامل شد: {pageNumber} صفحه ارسال شد."));
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
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                btnScan.IsEnabled = _connection?.State == HubConnectionState.Connected;
            });
        }
    }

    /// <summary>اسکن دستی: درخواست از سرور (مسیر استاندارد UI → Hub → Agent).</summary>
    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            Log("ابتدا به سرور متصل شوید.");
            return;
        }
        Log("درخواست اسکن دستی ارسال شد...");
        try
        {
            await _connection.InvokeAsync("RequestScan", _machineName, false);
        }
        catch (Exception ex)
        {
            Log($"خطا در ارسال درخواست: {ex.Message}");
        }
    }

    private async Task RequestManualScanAsync()
    {
        await Dispatcher.InvokeAsync(() => BtnScan_Click(this, new RoutedEventArgs()));
    }

    // ───────────────────────── Tray Icon ─────────────────────────

    private void CreateTrayIcon()
    {
        try
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = CreateTrayIconArt(),
                Text = "ScanSystem Agent",
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();

            menu.Items.Add("نمایش پنجره", null, (_, _) => ShowWindow());

            menu.Items.Add("اسکن دستی", null, (_, _) => _ = RequestManualScanAsync());

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

    /// <summary>ساخت آیکون Tray به‌صورت برنامه‌ای (بدون نیاز به فایل ico).</summary>
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
        try { hasScanner = _wia.DetectScanner(); } catch { }
        txtScanner.Text = hasScanner
            ? "اسکنر WIA شناسایی شد ✓"
            : "اسکنر WIA یافت نشد — حالت شبیه‌سازی فعال است.";
    }

    private void UpdateStatus(bool connected)
    {
        if (connected)
        {
            txtStatus.Text = "آنلاین";
            txtStatus.Foreground = System.Windows.Media.Brushes.Green;
            btnScan.IsEnabled = true;
            btnConnect.Content = "قطع اتصال";
        }
        else
        {
            txtStatus.Text = "آفلاین";
            txtStatus.Foreground = System.Windows.Media.Brushes.Red;
            btnScan.IsEnabled = false;
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
