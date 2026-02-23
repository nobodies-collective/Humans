using System.Reflection;
using Humans.Web.Helpers;
using SkiaSharp;
using Xunit;

namespace Humans.Integration.Tests;

public class ProfilePictureProcessorTests
{
    static ProfilePictureProcessorTests()
    {
        LibHeifResolver.Register();
    }

    private static byte[] LoadTestHeic()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(
            "Humans.Integration.Tests.TestData.sample.heic")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void DecodeHeifToSkBitmap_ReturnsValidBitmap_ForIPhoneHeic()
    {
        var heicData = LoadTestHeic();

        using var bitmap = ProfilePictureProcessor.DecodeHeifToSkBitmap(heicData);

        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
        Assert.Equal(SKColorType.Rgba8888, bitmap.ColorType);
    }

    [Fact]
    public void ResizeProfilePicture_ProducesJpeg_ForHeicInput()
    {
        var heicData = LoadTestHeic();

        var result = ProfilePictureProcessor.ResizeProfilePicture(heicData, "image/heic");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.True(result.Value.Data.Length > 0);

        // Verify the output is a valid JPEG (starts with FF D8)
        Assert.Equal(0xFF, result.Value.Data[0]);
        Assert.Equal(0xD8, result.Value.Data[1]);
    }

    [Fact]
    public void ResizeProfilePicture_ResizesLargeHeic_ToMaxDimension()
    {
        var heicData = LoadTestHeic();

        var result = ProfilePictureProcessor.ResizeProfilePicture(heicData, "image/heic");

        Assert.NotNull(result);

        // Verify the output dimensions are within the 1000px limit
        using var skData = SKData.CreateCopy(result.Value.Data);
        using var codec = SKCodec.Create(skData);
        Assert.NotNull(codec);
        var longSide = Math.Max(codec.Info.Width, codec.Info.Height);
        Assert.True(longSide <= 1000, $"Long side {longSide} exceeds 1000px limit");
    }

    [Fact]
    public void ResizeProfilePicture_ReturnsNull_ForCorruptData()
    {
        var corruptData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = ProfilePictureProcessor.ResizeProfilePicture(corruptData, "image/heic");

        Assert.Null(result);
    }

    [Fact]
    public void ResizeProfilePicture_StillWorksForJpeg()
    {
        // Create a small valid JPEG via SkiaSharp
        using var bitmap = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        var jpegData = encoded.ToArray();

        var result = ProfilePictureProcessor.ResizeProfilePicture(jpegData, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.True(result.Value.Data.Length > 0);
    }
}
