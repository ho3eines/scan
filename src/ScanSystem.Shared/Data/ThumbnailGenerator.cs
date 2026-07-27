using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScanSystem.Shared.Data;

/// <summary>
/// ساخت Thumbnail از تصاویر اسکن‌شده با System.Drawing.
/// (در środowindo Windows از ScanSystem.Web استفاده می‌شود)
/// </summary>
public static class ThumbnailGenerator
{
    public const int ThumbnailWidth = 240;
    public const int ThumbnailHeight = 320;

    /// <summary>ساخت Thumbnail JPEG از byte‌های تصویر ورودی.</summary>
    public static byte[]? Generate(byte[] imageBytes, int width = ThumbnailWidth, int height = ThumbnailHeight, int quality = 75)
    {
        if (imageBytes is null || imageBytes.Length == 0) return null;
        try
        {
            using var msIn = new MemoryStream(imageBytes);
            using var src = Image.FromStream(msIn);
            return Resize(src, width, height, quality);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>چرخش تصویر (90/180/270) و بازگرداندن byte‌های JPEG.</summary>
    public static byte[]? Rotate(byte[] imageBytes, int angle)
    {
        if (imageBytes is null || imageBytes.Length == 0) return null;
        try
        {
            using var msIn = new MemoryStream(imageBytes);
            using var src = Image.FromStream(msIn);
            var rotType = angle switch
            {
                90  => RotateFlipType.Rotate90FlipNone,
                180 => RotateFlipType.Rotate180FlipNone,
                270 => RotateFlipType.Rotate270FlipNone,
                _   => RotateFlipType.RotateNoneFlipNone
            };
            src.RotateFlip(rotType);
            using var msOut = new MemoryStream();
            var jpegCodec = GetJpegEncoder();
            var parms = GetEncoderParams(85);
            src.Save(msOut, jpegCodec, parms);
            return msOut.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Resize(Image src, int width, int height, int quality)
    {
        // حفظ نسبت تصویر در محدوده width×height
        double ratio = Math.Min((double)width / src.Width, (double)height / src.Height);
        int newW = Math.Max(1, (int)(src.Width * ratio));
        int newH = Math.Max(1, (int)(src.Height * ratio));

        using var bmp = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.White);
        g.DrawImage(src, 0, 0, newW, newH);

        using var msOut = new MemoryStream();
        var codec = GetJpegEncoder();
        var parms = GetEncoderParams(quality);
        bmp.Save(msOut, codec, parms);
        return msOut.ToArray();
    }

    private static ImageCodecInfo GetJpegEncoder()
        => ImageCodecInfo.GetImageEncoders()
            .First(c => c.MimeType == "image/jpeg");

    private static EncoderParameters GetEncoderParams(int quality)
    {
        var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        return p;
    }
}
