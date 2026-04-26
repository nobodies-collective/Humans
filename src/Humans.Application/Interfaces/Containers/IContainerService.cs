using NodaTime;

namespace Humans.Application.Interfaces.Containers;

public interface IContainerService
{
    Task<IReadOnlyList<ContainerDto>> GetBySeasonAsync(Guid campSeasonId, CancellationToken ct = default);
    Task<IReadOnlyList<ContainerDto>> GetOrgByYearAsync(int year, CancellationToken ct = default);
    Task<ContainerDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ContainerDto> CreateAsync(ContainerData data, CancellationToken ct = default);
    Task<ContainerDto> UpdateAsync(Guid id, ContainerData data, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task UploadImageAsync(Guid id, Stream stream, string fileName, string contentType, long length, CancellationToken ct = default);
    Task DeleteImageAsync(Guid id, CancellationToken ct = default);
}

public interface IContainerImageStorage
{
    Task<string> SaveImageAsync(Guid containerId, Stream stream, string contentType, CancellationToken ct = default);
    void DeleteImage(string storagePath);
}

public record ContainerDto(
    Guid Id,
    Guid? CampSeasonId,
    int Year,
    string Name,
    string? Description,
    string? ImageStoragePath,
    string? ImageContentType,
    string? ImageFileName,
    int SortOrder,
    Instant CreatedAt,
    Instant UpdatedAt
);

public record ContainerData(
    Guid? CampSeasonId,
    int Year,
    string Name,
    string? Description,
    int SortOrder
);
