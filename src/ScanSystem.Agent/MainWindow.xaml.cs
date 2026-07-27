using System.IO;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using ScanSystem.Shared;

namespace ScanSystem.Agent;

public partial class MainWindow : Window
{
    private HubConnection? _connection;
    // Agent با «نام ماشین خالص» نزد سرور ثبت می‌شود — همان مقداری که Hub برای جستجوی ConnectionId و ارسال ScanRequested استفاده می‌کند.
    private readonly string _clientId = Environment.MachineName;
    private readonly WiaScannerService _wia = new();
    private readonly AgentSettings _settings = AgentSettings.Load();
    private bool _isScanning = false;

    public MainWindow()
    {
        InitializeComponent();
        // آدرس سرور را از تنظیمات پیش‌فرض می‌گذاریم
        txtServerUrl.Text = _settings.ServerUrl;
        Log($"Agent started. ClientId: {_clientId}");
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            btnConnect.IsEnabled = false;
            Log("Connecting to server...");

            _connection = new HubConnectionBuilder()
                .WithUrl(txtServerUrl.Text)
                .WithAutomaticReconnect()
                .Build();

            // رویداد درخواست اسکن — سرور (machineName, requestId) می‌فرستد.
            _connection.On<string, Guid>("ScanRequested", async (clientId, requestId) =>
            {
                // پذیرش فقط اگر درخواست برای همین ماشین باشد (مقایسه با نام ماشین خالص).
                if (!string.Equals(clientId, _clientId, StringComparison.OrdinalIgnoreCase)) return;

                await Dispatcher.InvokeAsync(async () =>
                {
                    Log($"Scan requested: {requestId}");
                    await ProcessScan(requestId);
                });
            });

            // رویداد تغییر وضعیت
            _connection.On<Guid, string>("StatusChanged", (requestId, status) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log($"Status changed: {requestId} -> {status}");
                });
            });

            // رویداد اتصال Agent
            _connection.On<string>("AgentConnected", (connectionId) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log($"Agent connected: {connectionId}");
                });
            });

            _connection.Closed += async (error) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatus(false);
                    Log("Connection closed.");
                });
            };

            _connection.Reconnected += (connectionId) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatus(true);
                    Log($"Reconnected: {connectionId}");
                });
                // پس از بازایجاد اتصال، دوباره ثبت می‌کنیم چون ConnectionId جدید است.
                _ = _connection!.InvokeAsync("RegisterAgent", _clientId,
                    string.IsNullOrWhiteSpace(_settings.DisplayName) ? _clientId : _settings.DisplayName,
                    _wia.DetectScanner());
                return Task.CompletedTask;
            };

            await _connection.StartAsync();
            UpdateStatus(true);
            Log("Connected successfully!");

            // ثبت Agent نزد سرور: نام ماشین + displayName + hasScanner.
            // بدون این مرحله، سرور نمی‌داند این Agent آنلاین است و درخواست اسکن رد می‌شود.
            try
            {
                var hasScanner = _wia.DetectScanner();
                var displayName = string.IsNullOrWhiteSpace(_settings.DisplayName) ? _clientId : _settings.DisplayName;
                await _connection.InvokeAsync("RegisterAgent", _clientId, displayName, hasScanner);
                Log($"Registered as '{_clientId}' (hasScanner={hasScanner}).");
            }
            catch (Exception exReg)
            {
                Log($"RegisterAgent failed: {exReg.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"Connection error: {ex.Message}");
            MessageBox.Show($"خطا در اتصال:\n{ex.Message}", "خطا",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnConnect.IsEnabled = true;
        }
    }

    private async Task ProcessScan(Guid requestId)
    {
        if (_isScanning || _connection is null) return;

        _isScanning = true;
        btnScan.IsEnabled = false;

        try
        {
            // اعلام شروع پردازش
            await _connection.InvokeAsync("StartProcessing", requestId);
            Log("Processing started...");

            // اسکن واقعی با WIA؛ در صورت نبود دستگاه به شبیه‌سازی برمی‌گردد.
            var (data, real) = _wia.ScanAny(_clientId);
            if (data == null || data.Length == 0)
            {
                await _connection.InvokeAsync("ReportError", requestId, "Scan returned empty data");
                Log("Scan failed: empty data");
                return;
            }
            Log(real ? "WIA scan completed." : "Using simulated scan (no device).");

            // آپلود نتیجه
            var fileName = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            await _connection.InvokeAsync("Upload", requestId, fileName, "image/jpeg", data);
            Log($"Scan completed. Size: {data.Length} bytes");

            await Dispatcher.InvokeAsync(() =>
            {
                txtLastScan.Text = $"آخرین اسکن: {DateTime.Now:HH:mm:ss} | حجم: {data.Length:N0} بایت";
            });
        }
        catch (Exception ex)
        {
            try
            {
                await _connection.InvokeAsync("ReportError", requestId, ex.Message);
            }
            catch { }
            Log($"Scan error: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
            await Dispatcher.InvokeAsync(() =>
            {
                btnScan.IsEnabled = _connection?.State == HubConnectionState.Connected;
            });
        }
    }

    private byte[]? Scan()
    {
        // عملیات اسکن به WiaScannerService منتقل شده است؛ این متد فقط برای دسترس‌پذیری نگه داشته شده و در فرآیند استفاده نمی‌شود.
        return null;
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        // اسکن دستی (درخواست از سرور)
        _ = Task.Run(async () =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (_connection?.State == HubConnectionState.Connected)
                {
                    Log("Manual scan requested...");
                    await _connection.InvokeAsync("RequestScan", _clientId);
                }
                else
                {
                    Log("Cannot scan: not connected to server.");
                }
            });
        });
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
        var log = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        txtLog.AppendText(log);
        txtLog.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        _connection?.DisposeAsync().AsTask().Wait();
        base.OnClosed(e);
    }
}
