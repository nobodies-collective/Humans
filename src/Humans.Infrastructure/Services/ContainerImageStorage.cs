using Humans.Application.Interfaces.Containers;

namespace Humans.Infrastructure.Services;

public sealed class ContainerImageStorage : IContainerImageStorage
{
    private static string Root => Path.Combine("wwwroot");

    public async Task<string> SaveImageAsync(
        Guid containerId,
        Stream fileStream,
        string contentType,
        CancellationToken ct = default)
    {
        var ext = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("Unsupported content type.")
        };

        var storedFileName = $"{Guid.NewGuid()}{ext}";
        var relativePath = Path.Combine("uploads", "containers", containerId.ToString(), storedFileName);
        var fullPath = Path.Combine(Root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var stream = new FileStream(fullPath, FileMode.Create);
        await fileStream.CopyToAsync(stream, ct);

        return relativePath;
    }

    public void DeleteImage(string storagePath)
    {
        var fullPath = Path.Combine(Root, storagePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
