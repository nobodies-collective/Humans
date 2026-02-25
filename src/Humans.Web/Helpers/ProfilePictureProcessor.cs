using System.Runtime.InteropServices;
using LibHeifSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
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
        if (!HeifContentTypes.Contains(contentType))
        {
            return ResizeWithImageSharp(imageData, logger);
        }

        // HEIF/AVIF path: decode with LibHeifSharp, resize with SkiaSharp
        var original = DecodeHeifToSkBitmap(imageData, logger);
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

    // ImageSharp preserves EXIF metadata (including the orientation tag) through
    // load → resize → save, so the output JPEG displays correctly in all browsers.
    private static (byte[] Data, string ContentType)? ResizeWithImageSharp(byte[] imageData, ILogger? logger)
    {
        try
        {
            using var ms = new MemoryStream(imageData);
            using var image = Image.Load(ms);

            var longSide = Math.Max(image.Width, image.Height);
            if (longSide > MaxProfilePictureLongSide)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxProfilePictureLongSide, MaxProfilePictureLongSide),
                    Mode = ResizeMode.Max,
                }));
            }

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = 85 });
            return (output.ToArray(), "image/jpeg");
        }
        catch (UnknownImageFormatException ex)
        {
            logger?.LogWarning(ex, "Unknown image format ({Length} bytes)", imageData.Length);
            return null;
        }
        catch (InvalidImageContentException ex)
        {
            logger?.LogWarning(ex, "Invalid image content ({Length} bytes)", imageData.Length);
            return null;
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
