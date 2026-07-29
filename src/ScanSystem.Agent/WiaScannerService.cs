using System.Reflection;
using System.Runtime.InteropServices;

namespace ScanSystem.Agent;

/// <summary>
/// ارتباط با اسکنر از طریق WIA (COM late-bound — بدون نیاز به Interop DLL).
/// - اسکن تک‌صفحه‌ای یا چندصفحه‌ای از Feeder/ADF (حلقه تا پایان کاغذ)
/// - لیست کردن اسکنرهای موجود روی سیستم برای انتخاب توسط کاربر
/// - تصویر شبیه‌سازی‌شده فقط زمانی ساخته می‌شود که کاربر صراحتاً آن را در تنظیمات فعال کرده باشد؛
///   در غیر این صورت وقتی اسکنری یافت نشود، خطای «اسکنر تنظیم نیست» گزارش می‌شود.
/// </summary>
public class WiaScannerService
{
    /// <summary>اطلاعات نمایشی یک دستگاه WIA (برای پر کردن لیست انتخاب در UI).</summary>
    public sealed record ScannerInfo(string Id, string Name);

    /// <summary>آیا اسکنر مورد نظر (یا هر اسکنری، اگر شناسه مشخص نشده) روی سیستم موجود است؟</summary>
    public bool DetectScanner(string? preferredDeviceId = null)
    {
        try
        {
            var scanners = ListScanners();
            if (scanners.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(preferredDeviceId)) return true;
            return scanners.Any(s => string.Equals(s.Id, preferredDeviceId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>لیست تمام دستگاه‌های اسکنر شناسایی‌شده توسط WIA روی این سیستم.</summary>
    public List<ScannerInfo> ListScanners()
    {
        var result = new List<ScannerInfo>();
        try
        {
            var deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (deviceManagerType == null) return result;
            dynamic manager = Activator.CreateInstance(deviceManagerType)!;

            foreach (dynamic info in manager.DeviceInfos)
            {
                if ((int)info.Type != 1) continue; // فقط Scanner

                string id;
                try { id = (string)info.DeviceID; }
                catch { continue; }

                var name = WiaCom.TryGetNamedProperty(info, "Name") ?? id;
                result.Add(new ScannerInfo(id, name));
            }
        }
        catch
        {
            // در صورت هر خطا، لیست خالی برگردانده می‌شود.
        }
        return result;
    }

    /// <summary>
    /// ساخت یک نشست اسکن. هر بار NextPage() یک صفحه برمی‌گرداند؛
    /// وقتی feeder تمام شود یا به سقف صفحات برسیم، null برمی‌گردد.
    /// </summary>
    /// <param name="machineName">نام ماشین (برای تصویر آزمایشی).</param>
    /// <param name="maxPages">حداکثر تعداد صفحات.</param>
    /// <param name="allowBlankPlaceholder">
    /// اگر true باشد و اسکنری یافت نشود، به‌جای گزارش خطا یک تصویر آزمایشی ساخته می‌شود.
    /// اگر false باشد (پیش‌فرض)، نبود اسکنر باعث می‌شود <see cref="ScanSession.ScannerMissing"/> برابر true شود
    /// و هیچ تصویری تولید نگردد.
    /// </param>
    /// <param name="preferredDeviceId">شناسه اسکنر انتخاب‌شده توسط کاربر؛ خالی/نال = انتخاب خودکار اولین دستگاه.</param>
    public ScanSession CreateSession(string machineName, int maxPages, bool allowBlankPlaceholder = false, string? preferredDeviceId = null)
        => new(machineName, maxPages, allowBlankPlaceholder, preferredDeviceId);

    /// <summary>ساخت تصویر شبیه‌سازی‌شده (فقط وقتی صریحاً در تنظیمات Agent فعال شده باشد).</summary>
    public static byte[] SimulateScan(string machineName, int pageNumber)
    {
        using var bitmap = new System.Drawing.Bitmap(800, 1050); // نسبت A4
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.FillRectangle(System.Drawing.Brushes.White, 0, 0, 800, 1050);
        graphics.DrawRectangle(System.Drawing.Pens.LightGray, 10, 10, 780, 1030);

        using var titleFont = new System.Drawing.Font("Segoe UI", 22, System.Drawing.FontStyle.Bold);
        using var infoFont = new System.Drawing.Font("Segoe UI", 12);
        using var noteFont = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Italic);

        graphics.DrawString("SCAN DOCUMENT", titleFont, System.Drawing.Brushes.DarkBlue, 50, 50);
        graphics.DrawString($"Machine: {machineName}", infoFont, System.Drawing.Brushes.Black, 50, 110);
        graphics.DrawString($"Page: {pageNumber}", infoFont, System.Drawing.Brushes.Black, 50, 140);
        graphics.DrawString($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", infoFont, System.Drawing.Brushes.Black, 50, 170);
        graphics.DrawString("Simulated scan (no WIA device)", noteFont, System.Drawing.Brushes.Gray, 50, 220);

        using var ms = new System.IO.MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return ms.ToArray();
    }

    /// <summary>
    /// آیا تصویر داده‌شده عملاً «سفید/خالی» است؟ بر اساس انحراف معیار روشنایی پیکسل‌های نمونه‌برداری‌شده.
    /// </summary>
    /// <param name="jpegBytes">داده تصویر JPEG.</param>
    /// <param name="stdDevThreshold">آستانه انحراف معیار روشنایی؛ کمتر از این مقدار یعنی صفحه یکنواخت (خالی) است.</param>
    /// <param name="minBrightness">حداقل میانگین روشنایی لازم برای این‌که یکنواختی به‌معنای «سفید» باشد (نه یکدست تیره).</param>
    public static bool IsBlankPage(byte[] jpegBytes, double stdDevThreshold = 8.0, double minBrightness = 200.0)
    {
        if (jpegBytes is null || jpegBytes.Length == 0) return false;
        try
        {
            using var ms = new System.IO.MemoryStream(jpegBytes);
            using var bitmap = new System.Drawing.Bitmap(ms);

            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width <= 0 || height <= 0) return false;

            // نمونه‌برداری شبکه‌ای برای عملکرد بهتر روی تصاویر بزرگ (حداکثر ~200 نمونه در هر بعد).
            int stepX = Math.Max(1, width / 200);
            int stepY = Math.Max(1, height / 200);

            double sum = 0;
            double sumSq = 0;
            long count = 0;

            using var locked = new LockedBitmapReader(bitmap);
            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    var gray = locked.GetGray(x, y);
                    sum += gray;
                    sumSq += gray * gray;
                    count++;
                }
            }

            if (count == 0) return false;

            double mean = sum / count;
            double variance = (sumSq / count) - (mean * mean);
            double stdDev = Math.Sqrt(Math.Max(0, variance));

            return stdDev < stdDevThreshold && mean >= minBrightness;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>خواننده سریع پیکسل‌ها با LockBits (سریع‌تر از GetPixel).</summary>
    private sealed class LockedBitmapReader : IDisposable
    {
        private readonly System.Drawing.Bitmap _bitmap;
        private readonly System.Drawing.Imaging.BitmapData _data;
        private readonly int _bytesPerPixel;

        public LockedBitmapReader(System.Drawing.Bitmap bitmap)
        {
            _bitmap = bitmap;
            var format = System.Drawing.Imaging.PixelFormat.Format24bppRgb;
            _data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                format);
            _bytesPerPixel = System.Drawing.Image.GetPixelFormatSize(format) / 8;
        }

        public double GetGray(int x, int y)
        {
            unsafe
            {
                byte* row = (byte*)_data.Scan0 + (y * _data.Stride);
                byte* pixel = row + (x * _bytesPerPixel);
                // ترتیب BGR
                byte b = pixel[0];
                byte g = pixel[1];
                byte r = pixel[2];
                return (0.299 * r) + (0.587 * g) + (0.114 * b);
            }
        }

        public void Dispose() => _bitmap.UnlockBits(_data);
    }
}

/// <summary>
/// نشست اسکن: اتصال به دستگاه WIA، پیکربندی Feeder و انتقال صفحه‌به‌صفحه.
/// ترتیب تلاش:
///   1) حلقه ADF/Feeder (تا زمانی که وضعیت FEED_READY برقرار است)
///   2) دیالوگ تک‌صفحه‌ای WIA (برای اسکنرهای Flatbed)
///   3) تصویر شبیه‌سازی‌شده — فقط اگر <see cref="ScanSession(string,int,bool,string?)"/> با allowBlankPlaceholder=true ساخته شده باشد.
/// اگر هیچ اسکنری (یا اسکنر انتخاب‌شده) یافت نشود و پلیس‌هولدر مجاز نباشد، <see cref="ScannerMissing"/> برابر true می‌شود
/// و NextPage() بدون تولید هیچ تصویری null برمی‌گرداند.
/// </summary>
public sealed class ScanSession : IDisposable
{
    // WIA Automation constants
    private const string WiaFormatJpeg = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
    private const int PropDocumentHandlingSelect = 307830;  // WIA_DPS_DOCUMENT_HANDLING_SELECT
    private const int PropDocumentHandlingStatus = 307831;  // WIA_DPS_DOCUMENT_HANDLING_STATUS
    private const int HandlingSelectFeeder = 0x00000001;    // FEEDER
    private const int HandlingStatusFeedReady = 0x00000001; // FEED_READY

    private readonly string _machineName;
    private readonly int _maxPages;
    private readonly bool _allowBlankPlaceholder;
    private dynamic? _device;
    private bool _simulate;
    private int _pageCount;

    /// <summary>true یعنی اسکنر (یا اسکنر انتخاب‌شده) یافت نشد و هیچ تصویر آزمایشی هم تولید نشده است.</summary>
    public bool ScannerMissing { get; private set; }

    /// <summary>true یعنی نشست در حالت تصویر آزمایشی است (هیچ دستگاهی متصل نشده و کاربر تصویر تستی را مجاز کرده).</summary>
    public bool IsSimulated => _simulate && _device is null;

    internal ScanSession(string machineName, int maxPages, bool allowBlankPlaceholder, string? preferredDeviceId)
    {
        _machineName = machineName;
        _maxPages = Math.Max(1, maxPages);
        _allowBlankPlaceholder = allowBlankPlaceholder;
        InitDevice(preferredDeviceId);
    }

    private void InitDevice(string? preferredDeviceId)
    {
        try
        {
            var dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (dmType is null) throw new InvalidOperationException("WIA در دسترس نیست");

            dynamic dm = Activator.CreateInstance(dmType)!;

            dynamic? matched = null;
            dynamic? first = null;
            var hasPreferred = !string.IsNullOrWhiteSpace(preferredDeviceId);

            foreach (dynamic info in dm.DeviceInfos)
            {
                if ((int)info.Type != 1) continue; // فقط Scanner
                first ??= info;

                if (hasPreferred)
                {
                    string devId;
                    try { devId = (string)info.DeviceID; }
                    catch { continue; }

                    if (string.Equals(devId, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = info;
                        break;
                    }
                }
            }

            // اگر کاربر اسکنر خاصی انتخاب کرده اما یافت نشد → همان را «نبود اسکنر» تلقی می‌کنیم
            // (به‌جای برگشت خاموش به اولین دستگاه دیگر).
            dynamic? chosen = hasPreferred ? matched : first;

            if (chosen is null) throw new InvalidOperationException("اسکنری یافت نشد");

            _device = chosen.Connect();

            // انتخاب حالت Feeder (برای اسکنرهای بدون ADF بی‌اثر و بی‌خطا رد می‌شود).
            WiaCom.TrySetProperty(_device.Properties, PropDocumentHandlingSelect, HandlingSelectFeeder);
        }
        catch
        {
            _device = null;
            // فقط وقتی کاربر صراحتاً اجازه داده، به حالت تصویر آزمایشی سوییچ می‌کنیم.
            _simulate = _allowBlankPlaceholder;
            // «اسکنر گمشده» فقط یعنی نه دستگاهی متصل شد و نه تصویر آزمایشی مجاز است.
            // در حالت آزمایشی، تصاویر تستی تولید و آپلود می‌شوند و اسکن باید با موفقیت تمام شود.
            ScannerMissing = !_simulate;
        }
    }

    /// <summary>صفحه بعدی (byte[] JPEG) یا null در پایان اسکن / نبود اسکنر.</summary>
    public byte[]? NextPage()
    {
        if (_pageCount >= _maxPages) return null;

        byte[]? bytes = null;

        if (_device is not null)
            bytes = TryScanFromFeeder();

        // صفحه اول هنوز گرفته نشده و feeder جواب نداد → یک‌بار دیالوگ/flatbed
        if (bytes is null && _pageCount == 0 && _device is not null)
            bytes = TryDialogScan();

        // هیچ دستگاهی در دسترس نبود، اما تصویر آزمایشی مجاز است → یک تصویر تستی (مطابق رفتار قبلی برنامه)
        if (bytes is null && _pageCount == 0 && _simulate)
            bytes = WiaScannerService.SimulateScan(_machineName, 1);

        if (bytes is null) return null; // feeder تمام شد، یا اسکنری در دسترس نیست

        _pageCount++;
        return bytes;
    }

    /// <summary>تلاش برای گرفتن یک صفحه از Feeder/ADF.</summary>
    private byte[]? TryScanFromFeeder()
    {
        try
        {
            if (_device is null) return null;

            var status = WiaCom.TryGetProperty(_device.Properties, PropDocumentHandlingStatus);
            if (status is not int s || (s & HandlingStatusFeedReady) == 0)
                return null; // کاغذی در feeder نیست

            dynamic? item = WiaCom.GetIndexedMember(_device.Items, 1);
            if (item is null) return null;

            dynamic imageFile = item.Transfer(WiaFormatJpeg);
            return ImageFileToBytes(imageFile);
        }
        catch (COMException) { return null; }
        catch { return null; }
    }

    /// <summary>دیالوگ استاندارد WIA برای اسکن تک‌صفحه‌ای (Flatbed).</summary>
    private byte[]? TryDialogScan()
    {
        try
        {
            var dialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
            if (dialogType is null) return null;
            dynamic dialog = Activator.CreateInstance(dialogType)!;

            // ShowAcquireImage(DeviceType, Intent, Bias, FormatID, AlwaysSelectDevice, UseNewUI)
            dynamic? imageFile = dialog.ShowAcquireImage(
                1,              // WiaDeviceType.Scanner
                0,              // WiaImageIntent.Unspecified
                0,              // WiaImageBias.MinimizeSize
                WiaFormatJpeg,  // خروجی JPEG
                false,          // AlwaysSelectDevice
                false);         // UseNewUI

            if (imageFile is null) return null;
            return ImageFileToBytes(imageFile);
        }
        catch (COMException) { return null; } // کاربر لغو کرد
        catch { return null; }
    }

    private static byte[] ImageFileToBytes(dynamic imageFile)
    {
        Array arr = (Array)imageFile.FileData.BinaryData;
        var bytes = new byte[arr.Length];
        Array.Copy(arr, bytes, arr.Length);
        return bytes;
    }

    public void Dispose()
    {
        // رها کردن مرجع COM
        _device = null;
    }
}

/// <summary>توابع کمکی مشترک برای کار با COM late-bound (WIA).</summary>
internal static class WiaCom
{
    /// <summary>خواندن یک عضو indexed از COM collection (مثل DeviceItems.Item(1) یا Properties.Item("Name")).</summary>
    public static object? GetIndexedMember(object collection, object index)
    {
        try
        {
            return collection.GetType().InvokeMember("Item",
                BindingFlags.GetProperty | BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, collection, new[] { index });
        }
        catch { return null; }
    }

    /// <summary>خواندن مقدار یک WIA Property با شناسه عددی.</summary>
    public static object? TryGetProperty(object properties, int propertyId)
    {
        try
        {
            var prop = GetIndexedMember(properties, propertyId);
            if (prop is null) return null;
            return prop.GetType().InvokeMember("Value",
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null, prop, null);
        }
        catch { return null; }
    }

    /// <summary>خواندن مقدار یک WIA Property با نام (مثل "Name" روی DeviceInfo).</summary>
    public static string? TryGetNamedProperty(object infoOrProperties, string propertyName)
    {
        try
        {
            var props = infoOrProperties.GetType().InvokeMember("Properties",
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null, infoOrProperties, null);
            if (props is null) return null;

            var prop = GetIndexedMember(props, propertyName);
            if (prop is null) return null;

            var value = prop.GetType().InvokeMember("Value",
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null, prop, null);
            return value?.ToString();
        }
        catch { return null; }
    }

    /// <summary>تنظیم مقدار یک WIA Property (مثلاً انتخاب Feeder).</summary>
    public static void TrySetProperty(object properties, int propertyId, object value)
    {
        try
        {
            var prop = GetIndexedMember(properties, propertyId);
            if (prop is null) return;
            prop.GetType().InvokeMember("Value",
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance,
                null, prop, new[] { value });
        }
        catch
        {
            // اسکنرهای Flatbed این Property را ندارند — نادیده گرفته می‌شود.
        }
    }
}
