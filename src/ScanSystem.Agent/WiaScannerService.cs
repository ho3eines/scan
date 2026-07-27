using System.Runtime.InteropServices;

namespace ScanSystem.Agent;

/// <summary>
/// ارتباط واقعی با اسکنر از طریق WIA (با COM late-bound تا نیازی به interop DLL نباشد).
/// اگر دستگاهی نباشد یا خطا دهد، به اسکن شبیه‌سازی‌شده برمی‌گردد.
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

    /// <summary>یک اسکن واقعی انجام می‌دهد و بایت‌های تصویر JPEG برمی‌گرداند.</summary>
    public byte[]? Scan()
    {
        try
        {
            var dialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
            if (dialogType == null) return null;
            dynamic dialog = Activator.CreateInstance(dialogType)!;

            // ShowAcquireImage(ImageFile, ...) — برای حالت ساده:
            //لود دستگاه اول با کلیک کاربر. این پنجره‌ی انتخاب منبع را باز می‌کند.
            // پارامترها: ImageFile (null), FormatId (WiaImgFmt), Intent, Bias, MinDPI/X/Y, MaxDPI/X/Y, ...
            const string WiaFormatJpeg = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"; // JPEG
            dynamic? imageFile = dialog.ShowAcquireImage(
                WiaFormatJpeg, false, false, false, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            if (imageFile == null) return null;

            // تبدیل به JPEG و خروج به byte[]
            dynamic merged = imageFile;
            // اگر چند صفحه باشد، با Frame از طریق ArithmeticProcess تصویر می‌چسبد؛ ساده‌سازی: همان ImageFile را می‌گیریم.
            var imgData = merged.FileData; //普惠 Vector
            var arr = (Array)imgData.BinaryData;
            byte[] bytes = new byte[arr.Length];
            int i = 0;
            foreach (var b in arr) bytes[i++] = (byte)b;
            return bytes;
        }
        catch (COMException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>اسکن شبیه‌سازی‌شده برای زمانی که دستگاه WIA در دسترس نیست.</summary>
    public byte[] SimulateScan(string machineName)
    {
        var bitmap = new System.Drawing.Bitmap(800, 600);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.FillRectangle(System.Drawing.Brushes.White, 0, 0, 800, 600);
        graphics.DrawRectangle(System.Drawing.Pens.LightGray, 10, 10, 780, 580);

        var titleFont = new System.Drawing.Font("Segoe UI", 20, System.Drawing.FontStyle.Bold);
        var infoFont = new System.Drawing.Font("Segoe UI", 12);

        graphics.DrawString("SCAN DOCUMENT", titleFont, System.Drawing.Brushes.DarkBlue, 50, 50);
        graphics.DrawString($"Machine: {machineName}", infoFont, System.Drawing.Brushes.Black, 50, 100);
        graphics.DrawString($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", infoFont, System.Drawing.Brushes.Black, 50, 130);
        graphics.DrawString("Simulated scan (no WIA device)", new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Italic), System.Drawing.Brushes.Gray, 50, 180);

        using var ms = new System.IO.MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return ms.ToArray();
    }

    /// <summary>اجرای کامل اسکن: اول WIA، در صورت شکست شبیه‌سازی.</summary>
    public (byte[] data, bool real) ScanAny(string machineName)
    {
        var bytes = Scan();
        if (bytes != null && bytes.Length > 0) return (bytes, true);
        return (SimulateScan(machineName), false);
    }
}
