using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace ScanSystem.Agent;

/// <summary>
/// پنجره «تنظیمات ایجنت و اسکنر»: لیست دستگاه‌های WIA موجود روی سیستم را نشان می‌دهد و به کاربر
/// اجازه می‌دهد یکی را برای اسکن انتخاب کند، همچنین تنظیمات سرور و اتصال و گزینه‌های رفتاری را کنترل می‌کند:
///   - آدرس Hub سرور و مهلت پاسخ‌دهی (Time Out) SignalR
///   - ساخت تصویر آزمایشی وقتی اسکنری یافت نشود (پیش‌فرض خاموش)
///   - نادیده گرفتن (عدم ارسال) صفحات کاملاً سفید/خالی
/// </summary>
public partial class ScannerSettingsWindow : Window
{
    /// <summary>آیتم نمایشی هر اسکنر در لیست، با پشتیبانی از انتخاب رادیویی.</summary>
    public sealed class ScannerItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly WiaScannerService _wia;
    private readonly AgentSettings _settings;

    public ObservableCollection<ScannerItem> Scanners { get; } = new();

    /// <summary>آیا کاربر روی «ذخیره» کلیک کرد (برای این‌که فراخوان بداند تنظیمات باید دوباره اعمال شود).</summary>
    public bool SettingsChanged { get; private set; }

    public ScannerSettingsWindow(WiaScannerService wia, AgentSettings settings)
    {
        InitializeComponent();
        _wia = wia;
        _settings = settings;

        lstScanners.ItemsSource = Scanners;
        chkPlaceholder.IsChecked = _settings.UsePlaceholderWhenNoScanner;
        chkSkipBlank.IsChecked = _settings.SkipBlankPages;
        txtServerUrl.Text = _settings.ServerUrl;
        txtTimeout.Text = _settings.ServerTimeoutSeconds.ToString();

        LoadScanners();
    }

    private void LoadScanners()
    {
        Scanners.Clear();
        List<WiaScannerService.ScannerInfo> found;
        try { found = _wia.ListScanners(); }
        catch { found = new List<WiaScannerService.ScannerInfo>(); }

        foreach (var s in found)
        {
            Scanners.Add(new ScannerItem
            {
                Id = s.Id,
                Name = s.Name,
                IsSelected = !string.IsNullOrEmpty(_settings.SelectedScannerId) &&
                             string.Equals(s.Id, _settings.SelectedScannerId, StringComparison.OrdinalIgnoreCase)
            });
        }

        // اگر هیچ‌کدام از قبل انتخاب نشده و فقط یک اسکنر وجود دارد، به‌صورت پیش‌فرض انتخابش کن.
        if (Scanners.Count > 0 && !Scanners.Any(s => s.IsSelected))
            Scanners[0].IsSelected = true;

        txtNoScanner.Visibility = Scanners.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadScanners();

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var selected = Scanners.FirstOrDefault(s => s.IsSelected);
        _settings.SelectedScannerId = selected?.Id ?? "";
        _settings.UsePlaceholderWhenNoScanner = chkPlaceholder.IsChecked == true;
        _settings.SkipBlankPages = chkSkipBlank.IsChecked == true;

        if (!string.IsNullOrWhiteSpace(txtServerUrl.Text))
            _settings.ServerUrl = txtServerUrl.Text.Trim();

        if (int.TryParse(txtTimeout.Text.Trim(), out var timeoutSec) && timeoutSec > 0)
            _settings.ServerTimeoutSeconds = timeoutSec;
        else
            _settings.ServerTimeoutSeconds = 120;

        AgentSettings.Save(_settings);
        SettingsChanged = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
