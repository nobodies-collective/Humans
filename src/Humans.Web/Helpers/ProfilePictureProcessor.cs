using System.Runtime.InteropServices;
using LibHeifSharp;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Humans.Web.Helpers;

internal static class ProfilePictureProcessor
{
    private const int MaxProfilePictureLongSide = 1000;

    private static readonly HashSet<string> HeifContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/heic",
        "image/heif",
        "image/avif"
    };

    internal static (byte[] Data, string ContentType)? ResizeProfilePicture(
        byte[] imageData, string contentType, ILogger? logger = null)
    {
        SKBitmap? original;
        if (HeifContentTypes.Contains(contentType))
        {
            original = DecodeHeifToSkBitmap(imageData, logger);
        }
        else
        {
            using var skData = SKData.CreateCopy(imageData);
            using var codec = SKCodec.Create(skData);
            if (codec == null)
            {
                return null;
            }

            original = SKBitmap.Decode(codec);
        }

        if (original == null)
        {
            return null;
        }

        using (original)
        {
            var width = original.Width;
            var height = original.Height;
            var longSide = Math.Max(width, height);

            if (longSide > MaxProfilePictureLongSide)
            {
                var scale = (float)MaxProfilePictureLongSide / longSide;
                width = (int)(width * scale);
                height = (int)(height * scale);

                using var resized = original.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
                using var image = SKImage.FromBitmap(resized);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
                return (encoded.ToArray(), "image/jpeg");
            }

            // Image is already small enough — re-encode as JPEG for consistent storage
            using var smallImage = SKImage.FromBitmap(original);
            using var smallEncoded = smallImage.Encode(SKEncodedImageFormat.Jpeg, 85);
            return (smallEncoded.ToArray(), "image/jpeg");
        }
    }

    internal static SKBitmap? DecodeHeifToSkBitmap(byte[] imageData, ILogger? logger = null)
    {
        try
        {
            using var context = new HeifContext(imageData);
            using var handle = context.GetPrimaryImageHandle();
            using var heifImage = handle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32);

            var width = heifImage.Width;
            var height = heifImage.Height;
            var plane = heifImage.GetPlane(HeifChannel.Interleaved);
            var srcStride = plane.Stride;

            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var dstPtr = bitmap.GetPixels();
            var dstStride = bitmap.RowBytes;
            var rowBytes = width * 4; // RGBA = 4 bytes per pixel

            var rowBuffer = new byte[rowBytes];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(plane.Scan0 + y * srcStride, rowBuffer, 0, rowBytes);
                Marshal.Copy(rowBuffer, 0, dstPtr + y * dstStride, rowBytes);
            }

            return bitmap;
        }
        catch (HeifException ex)
        {
            logger?.LogWarning(ex, "Failed to decode HEIF image ({Length} bytes)", imageData.Length);
            return null;
        }
    }
}
