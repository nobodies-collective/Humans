using Humans.Application.Interfaces.Containers;
using Humans.Application.Interfaces.Repositories;
using Humans.Domain.Entities;
using NodaTime;

namespace Humans.Application.Services.Containers;

public sealed class ContainerService : IContainerService
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private const long MaxImageBytes = 10 * 1024 * 1024;

    private readonly IContainerRepository _repo;
    private readonly IContainerImageStorage _imageStorage;
    private readonly IClock _clock;

    public ContainerService(
        IContainerRepository repo,
        IContainerImageStorage imageStorage,
        IClock clock)
    {
        _repo = repo;
        _imageStorage = imageStorage;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ContainerDto>> GetBySeasonAsync(Guid campSeasonId, CancellationToken ct = default)
    {
        var containers = await _repo.GetBySeasonAsync(campSeasonId, ct);
        return containers.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ContainerDto>> GetOrgByYearAsync(int year, CancellationToken ct = default)
    {
        var containers = await _repo.GetOrgByYearAsync(year, ct);
        return containers.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ContainerDto>> GetAllByYearAsync(int year, CancellationToken ct = default)
    {
        var containers = await _repo.GetAllByYearAsync(year, ct);
        return containers.Select(ToDto).ToList();
    }

    public async Task<ContainerDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var container = await _repo.GetByIdAsync(id, ct);
        return container is null ? null : ToDto(container);
    }

    public async Task<ContainerDto> CreateAsync(ContainerData data, CancellationToken ct = default)
    {
        var now = _clock.GetCurrentInstant();
        var container = new Container
        {
            Id = Guid.NewGuid(),
            CampSeasonId = data.CampSeasonId,
            Year = data.Year,
            Name = data.Name,
            Description = data.Description,
            CreatedAt = now,
            UpdatedAt = now
        };

        var created = await _repo.AddAsync(container, ct);
        return ToDto(created);
    }

    public async Task<ContainerDto> UpdateAsync(Guid id, ContainerData data, CancellationToken ct = default)
    {
        var container = await _repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Container not found.");

        container.Name = data.Name;
        container.Description = data.Description;
        container.UpdatedAt = _clock.GetCurrentInstant();

        var updated = await _repo.UpdateAsync(container, ct);
        return ToDto(updated);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var container = await _repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Container not found.");

        if (container.ImageStoragePath is not null)
            _imageStorage.DeleteImage(container.ImageStoragePath);

        await _repo.DeleteAsync(id, ct);
    }

    public async Task UploadImageAsync(
        Guid id, Stream stream, string fileName, string contentType, long length,
        CancellationToken ct = default)
    {
        if (!AllowedContentTypes.Contains(contentType))
            throw new InvalidOperationException("Only JPEG, PNG, and WebP images are allowed.");
        if (length > MaxImageBytes)
            throw new InvalidOperationException("Image must be under 10 MB.");

        var container = await _repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Container not found.");

        if (container.ImageStoragePath is not null)
            _imageStorage.DeleteImage(container.ImageStoragePath);

        var storagePath = await _imageStorage.SaveImageAsync(id, stream, contentType, ct);
        container.ImageStoragePath = storagePath;
        container.ImageContentType = contentType;
        container.ImageFileName = fileName;
        container.UpdatedAt = _clock.GetCurrentInstant();

        await _repo.UpdateAsync(container, ct);
    }

    public async Task DeleteImageAsync(Guid id, CancellationToken ct = default)
    {
        var container = await _repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Container not found.");

        if (container.ImageStoragePath is null)
            return;

        _imageStorage.DeleteImage(container.ImageStoragePath);
        container.ImageStoragePath = null;
        container.ImageContentType = null;
        container.ImageFileName = null;
        container.UpdatedAt = _clock.GetCurrentInstant();

        await _repo.UpdateAsync(container, ct);
    }

    public async Task<ContainerDto> SavePlacementAsync(Guid id, string geoJson, CancellationToken ct = default)
    {
        var container = await _repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Container not found.");

        container.LocationGeoJson = geoJson;
        container.UpdatedAt = _clock.GetCurrentInstant();

        var updated = await _repo.UpdateAsync(container, ct);
        return ToDto(updated);
    }

    public async Task ClearPlacementAsync(Guid id, CancellationToken ct = default)
    {
        var container = await _repo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Container not found.");

        container.LocationGeoJson = null;
        container.UpdatedAt = _clock.GetCurrentInstant();

        await _repo.UpdateAsync(container, ct);
    }

    private static ContainerDto ToDto(Container c) => new(
        c.Id,
        c.CampSeasonId,
        c.Year,
        c.Name,
        c.Description,
        c.ImageStoragePath is not null ? $"/{c.ImageStoragePath}" : null,
        c.ImageContentType,
        c.ImageFileName,
        c.LocationGeoJson,
        c.CreatedAt,
        c.UpdatedAt);
}
