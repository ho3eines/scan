using System.Reflection;
using System.Runtime.InteropServices;

namespace ScanSystem.Agent;

/// <summary>
/// ارتباط با اسکنر از طریق WIA (COM late-bound — بدون نیاز به Interop DLL).
/// - اسکن تک‌صفحه‌ای یا چندصفحه‌ای از Feeder/ADF (حلقه تا پایان کاغذ)
/// - اگر دستگاه WIA موجود نباشد، به اسکن شبیه‌سازی‌شده برمی‌گردد (مناسب تست).
/// </summary>
public class WiaScannerService
{
    private bool? _hasScanner;

    /// <summary>آیا حداقل یک WIA device (اسکنر) روی سیستم موجود است؟</summary>
    public bool DetectScanner()
    {
        if (_hasScanner.HasValue) return _hasScanner.Value;
        try
        {
            var deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (deviceManagerType == null) { _hasScanner = false; return false; }
            dynamic manager = Activator.CreateInstance(deviceManagerType)!;

            int scannerCount = 0;
            foreach (dynamic info in manager.DeviceInfos)
            {
                // Type == 1 یعنی Scanner
                if ((int)info.Type == 1) scannerCount++;
            }
            _hasScanner = scannerCount > 0;
        }
        catch
        {
            _hasScanner = false;
        }
        return _hasScanner.Value;
    }

    /// <summary>
    /// ساخت یک نشست اسکن. هر بار NextPage() یک صفحه برمی‌گرداند؛
    /// وقتی feeder تمام شود یا به سقف صفحات برسیم، null برمی‌گردد.
    /// </summary>
    public ScanSession CreateSession(string machineName, int maxPages)
        => new(machineName, maxPages);

    /// <summary>ساخت تصویر شبیه‌سازی‌شده (زمانی که دستگاه WIA در دسترس نیست).</summary>
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
}

/// <summary>
/// نشست اسکن: اتصال به دستگاه WIA، پیکربندی Feeder و انتقال صفحه‌به‌صفحه.
/// ترتیب تلاش:
///   1) حلقه ADF/Feeder (تا زمانی که وضعیت FEED_READY برقرار است)
///   2) دیالوگ تک‌صفحه‌ای WIA (برای اسکنرهای Flatbed)
///   3) تصویر شبیه‌سازی‌شده (وقتی هیچ دستگاهی نیست)
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
    private dynamic? _device;
    private bool _simulate;
    private bool _simulateDone;
    private int _pageCount;

    internal ScanSession(string machineName, int maxPages)
    {
        _machineName = machineName;
        _maxPages = Math.Max(1, maxPages);
        InitDevice();
    }

    private void InitDevice()
    {
        try
        {
            var dmType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (dmType is null) throw new InvalidOperationException("WIA در دسترس نیست");

            dynamic dm = Activator.CreateInstance(dmType)!;
            foreach (dynamic info in dm.DeviceInfos)
            {
                if ((int)info.Type == 1) // Scanner
                {
                    _device = info.Connect();
                    break;
                }
            }
            if (_device is null) throw new InvalidOperationException("اسکنری یافت نشد");

            // انتخاب حالت Feeder (برای اسکنرهای بدون ADF بی‌اثر و بی‌خطا رد می‌شود).
            TrySetProperty(_device.Properties, PropDocumentHandlingSelect, HandlingSelectFeeder);
        }
        catch
        {
            _device = null;
            _simulate = true;
        }
    }

    /// <summary>صفحه بعدی (byte[] JPEG) یا null در پایان اسکن.</summary>
    public byte[]? NextPage()
    {
        if (_pageCount >= _maxPages) return null;

        byte[]? bytes = null;

        if (!_simulate)
            bytes = TryScanFromFeeder();

        // صفحه اول هنوز گرفته نشده و feeder جواب نداد → یک‌بار دیالوگ/flatbed
        if (bytes is null && _pageCount == 0 && !_simulate)
            bytes = TryDialogScan();

        // هیچ دستگاهی در دسترس نبود → شبیه‌سازی
        if (bytes is null && _pageCount == 0)
            bytes = WiaScannerService.SimulateScan(_machineName, 1);

        if (bytes is null) return null; // feeder تمام شد

        _pageCount++;
        return bytes;
    }

    /// <summary>تلاش برای گرفتن یک صفحه از Feeder/ADF.</summary>
    private byte[]? TryScanFromFeeder()
    {
        try
        {
            if (_device is null) return null;

            var status = TryGetProperty(_device.Properties, PropDocumentHandlingStatus);
            if (status is not int s || (s & HandlingStatusFeedReady) == 0)
                return null; // کاغذی در feeder نیست

            dynamic? item = GetIndexedMember(_device.Items, 1);
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

    // ─────────── helpers برای COM late-bound ───────────

    /// <summary>خواندن یک عضو indexed از COM collection (مثل DeviceItems.Item(1)).</summary>
    private static object? GetIndexedMember(object collection, object index)
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
    private static object? TryGetProperty(object properties, int propertyId)
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

    /// <summary>تنظیم مقدار یک WIA Property (مثلاً انتخاب Feeder).</summary>
    private static void TrySetProperty(object properties, int propertyId, object value)
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

    public void Dispose()
    {
        // رها کردن مرجع COM
        _device = null;
    }
}
