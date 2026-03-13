using Humans.Domain.Entities;
using Humans.Domain.Enums;
using NodaTime;

namespace Humans.Application.Interfaces;

public interface IBarrioService
{
    // Registration
    Task<Barrio> CreateBarrioAsync(
        Guid createdByUserId,
        string name,
        string contactEmail,
        string contactPhone,
        string? webOrSocialUrl,
        string contactMethod,
        bool isSwissCamp,
        int timesAtNowhere,
        BarrioSeasonData seasonData,
        List<string>? historicalNames,
        int year,
        CancellationToken cancellationToken = default);

    // Queries
    Task<Barrio?> GetBarrioBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Barrio?> GetBarrioByIdAsync(Guid barrioId, CancellationToken cancellationToken = default);
    Task<List<Barrio>> GetBarriosForYearAsync(int year, CancellationToken cancellationToken = default);
    Task<BarrioSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<List<BarrioSeason>> GetPendingSeasonsAsync(CancellationToken cancellationToken = default);

    // Season management
    Task<BarrioSeason> OptInToSeasonAsync(Guid barrioId, int year, CancellationToken cancellationToken = default);
    Task UpdateSeasonAsync(Guid seasonId, BarrioSeasonData data, CancellationToken cancellationToken = default);
    Task ApproveSeasonAsync(Guid seasonId, Guid reviewedByUserId, string? notes, CancellationToken cancellationToken = default);
    Task RejectSeasonAsync(Guid seasonId, Guid reviewedByUserId, string notes, CancellationToken cancellationToken = default);
    Task WithdrawSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task SetSeasonFullAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task ReactivateSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task DeactivateSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // Barrio updates
    Task UpdateBarrioAsync(Guid barrioId, string contactEmail, string contactPhone,
        string? webOrSocialUrl, string contactMethod, bool isSwissCamp, int timesAtNowhere,
        CancellationToken cancellationToken = default);
    Task DeleteBarrioAsync(Guid barrioId, CancellationToken cancellationToken = default);

    // Lead management
    Task<BarrioLead> AddLeadAsync(Guid barrioId, Guid userId, BarrioLeadRole role, CancellationToken cancellationToken = default);
    Task RemoveLeadAsync(Guid leadId, CancellationToken cancellationToken = default);
    Task TransferPrimaryLeadAsync(Guid barrioId, Guid newPrimaryUserId, CancellationToken cancellationToken = default);

    // Authorization checks
    Task<bool> IsUserBarrioLeadAsync(Guid userId, Guid barrioId, CancellationToken cancellationToken = default);
    Task<bool> IsUserPrimaryLeadAsync(Guid userId, Guid barrioId, CancellationToken cancellationToken = default);

    // Images
    Task<BarrioImage> UploadImageAsync(Guid barrioId, Stream fileStream, string fileName, string contentType, long length, CancellationToken cancellationToken = default);
    Task DeleteImageAsync(Guid imageId, CancellationToken cancellationToken = default);
    Task ReorderImagesAsync(Guid barrioId, List<Guid> imageIdsInOrder, CancellationToken cancellationToken = default);

    // Settings (CampAdmin)
    Task SetPublicYearAsync(int year, CancellationToken cancellationToken = default);
    Task OpenSeasonAsync(int year, CancellationToken cancellationToken = default);
    Task CloseSeasonAsync(int year, CancellationToken cancellationToken = default);
    Task SetNameLockDateAsync(int year, LocalDate lockDate, CancellationToken cancellationToken = default);

    // Name change (handles historical name logging)
    Task ChangeSeasonNameAsync(Guid seasonId, string newName, CancellationToken cancellationToken = default);
}

public record BarrioSeasonData(
    string BlurbLong,
    string BlurbShort,
    string Languages,
    YesNoMaybe AcceptingMembers,
    YesNoMaybe KidsWelcome,
    KidsVisitingPolicy KidsVisiting,
    string? KidsAreaDescription,
    PerformanceSpaceStatus HasPerformanceSpace,
    string? PerformanceTypes,
    List<BarrioVibe> Vibes,
    AdultPlayspacePolicy AdultPlayspace,
    int MemberCount,
    SpaceSize? SpaceRequirement,
    SoundZone? SoundZone,
    int ContainerCount,
    string? ContainerNotes,
    ElectricalGrid? ElectricalGrid);
