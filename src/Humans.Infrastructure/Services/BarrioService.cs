using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Humans.Infrastructure.Services;

public class BarrioService : IBarrioService
{
    private readonly HumansDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly IClock _clock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BarrioService> _logger;

    private const string CacheKeyPrefix = "barrios_year_";

    public BarrioService(
        HumansDbContext dbContext,
        IAuditLogService auditLogService,
        IClock clock,
        IMemoryCache cache,
        ILogger<BarrioService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _clock = clock;
        _cache = cache;
        _logger = logger;
    }

    // ==========================================================================
    // Registration
    // ==========================================================================

    public async Task<Barrio> CreateBarrioAsync(
        Guid createdByUserId, string name, string contactEmail, string contactPhone,
        string? webOrSocialUrl, string contactMethod, bool isSwissCamp, int timesAtNowhere,
        BarrioSeasonData seasonData, List<string>? historicalNames, int year,
        CancellationToken cancellationToken = default)
    {
        var slug = SlugHelper.GenerateSlug(name);
        if (SlugHelper.IsReservedBarrioSlug(slug))
            throw new InvalidOperationException($"The name '{name}' generates a reserved slug.");

        // Ensure unique slug
        var baseSlug = slug;
        var suffix = 2;
        while (await _dbContext.Barrios.AnyAsync(b => b.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        var now = _clock.GetCurrentInstant();
        var barrio = new Barrio
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            WebOrSocialUrl = webOrSocialUrl,
            ContactMethod = contactMethod,
            IsSwissCamp = isSwissCamp,
            TimesAtNowhere = timesAtNowhere,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.Barrios.Add(barrio);

        var season = CreateSeasonFromData(barrio.Id, year, name, seasonData, now);
        _dbContext.BarrioSeasons.Add(season);

        var lead = new BarrioLead
        {
            Id = Guid.NewGuid(),
            BarrioId = barrio.Id,
            UserId = createdByUserId,
            Role = BarrioLeadRole.Primary,
            JoinedAt = now
        };

        _dbContext.BarrioLeads.Add(lead);

        if (historicalNames is { Count: > 0 })
        {
            foreach (var oldName in historicalNames)
            {
                _dbContext.BarrioHistoricalNames.Add(new BarrioHistoricalName
                {
                    Id = Guid.NewGuid(),
                    BarrioId = barrio.Id,
                    Name = oldName,
                    Source = BarrioNameSource.Manual,
                    CreatedAt = now
                });
            }
        }

        await _auditLogService.LogAsync(
            AuditAction.BarrioCreated, nameof(Barrio), barrio.Id,
            $"Registered camp '{name}' for {year}",
            createdByUserId, createdByUserId.ToString());

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(year);

        return barrio;
    }

    // ==========================================================================
    // Queries
    // ==========================================================================

    public async Task<Barrio?> GetBarrioBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Barrios
            .Include(b => b.Seasons)
            .Include(b => b.Leads.Where(l => l.LeftAt == null))
                .ThenInclude(l => l.User)
            .Include(b => b.HistoricalNames)
            .Include(b => b.Images.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(b => b.Slug == slug, cancellationToken);
    }

    public async Task<Barrio?> GetBarrioByIdAsync(Guid barrioId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Barrios
            .Include(b => b.Seasons)
            .Include(b => b.Leads.Where(l => l.LeftAt == null))
                .ThenInclude(l => l.User)
            .Include(b => b.HistoricalNames)
            .Include(b => b.Images.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(b => b.Id == barrioId, cancellationToken);
    }

    public async Task<List<Barrio>> GetBarriosForYearAsync(int year, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{year}";
        if (_cache.TryGetValue(cacheKey, out List<Barrio>? cached) && cached is not null)
            return cached;

        var barrios = await _dbContext.Barrios
            .Include(b => b.Seasons.Where(s => s.Year == year &&
                (s.Status == BarrioSeasonStatus.Active || s.Status == BarrioSeasonStatus.Full)))
            .Include(b => b.Images.OrderBy(i => i.SortOrder))
            .Include(b => b.HistoricalNames)
            .Where(b => b.Seasons.Any(s => s.Year == year &&
                (s.Status == BarrioSeasonStatus.Active || s.Status == BarrioSeasonStatus.Full)))
            .ToListAsync(cancellationToken);

        _cache.Set(cacheKey, barrios, TimeSpan.FromMinutes(5));
        return barrios;
    }

    public async Task<BarrioSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.BarrioSettings.FirstAsync(cancellationToken);
    }

    public async Task<List<BarrioSeason>> GetPendingSeasonsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.BarrioSeasons
            .Include(s => s.Barrio)
                .ThenInclude(b => b.Leads.Where(l => l.LeftAt == null))
                    .ThenInclude(l => l.User)
            .Where(s => s.Status == BarrioSeasonStatus.Pending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    // ==========================================================================
    // Season management
    // ==========================================================================

    public async Task<BarrioSeason> OptInToSeasonAsync(Guid barrioId, int year,
        CancellationToken cancellationToken = default)
    {
        // Verify season is open
        var settings = await GetSettingsAsync(cancellationToken);
        if (!settings.OpenSeasons.Contains(year))
            throw new InvalidOperationException($"Season {year} is not open for registration.");

        // Check no existing season for this year
        var existing = await _dbContext.BarrioSeasons
            .AnyAsync(s => s.BarrioId == barrioId && s.Year == year, cancellationToken);
        if (existing)
            throw new InvalidOperationException($"Barrio already has a season for {year}.");

        // Copy from most recent season
        var previousSeason = await _dbContext.BarrioSeasons
            .Where(s => s.BarrioId == barrioId)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No previous season to copy from.");

        // Auto-approve if any prior season was not Rejected (Withdrawn is OK for auto-approve)
        var hasNonRejectedSeason = await _dbContext.BarrioSeasons
            .AnyAsync(s => s.BarrioId == barrioId && s.Status != BarrioSeasonStatus.Rejected,
                cancellationToken);

        var now = _clock.GetCurrentInstant();
        var newSeason = new BarrioSeason
        {
            Id = Guid.NewGuid(),
            BarrioId = barrioId,
            Year = year,
            Name = previousSeason.Name,
            Status = hasNonRejectedSeason ? BarrioSeasonStatus.Active : BarrioSeasonStatus.Pending,
            BlurbLong = previousSeason.BlurbLong,
            BlurbShort = previousSeason.BlurbShort,
            Languages = previousSeason.Languages,
            AcceptingMembers = previousSeason.AcceptingMembers,
            KidsWelcome = previousSeason.KidsWelcome,
            KidsVisiting = previousSeason.KidsVisiting,
            KidsAreaDescription = previousSeason.KidsAreaDescription,
            HasPerformanceSpace = previousSeason.HasPerformanceSpace,
            PerformanceTypes = previousSeason.PerformanceTypes,
            Vibes = new List<BarrioVibe>(previousSeason.Vibes),
            AdultPlayspace = previousSeason.AdultPlayspace,
            MemberCount = previousSeason.MemberCount,
            SpaceRequirement = previousSeason.SpaceRequirement,
            SoundZone = previousSeason.SoundZone,
            ContainerCount = previousSeason.ContainerCount,
            ContainerNotes = previousSeason.ContainerNotes,
            ElectricalGrid = previousSeason.ElectricalGrid,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.BarrioSeasons.Add(newSeason);

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonCreated, nameof(BarrioSeason), newSeason.Id,
            $"Opted in to season {year} (auto-approved: {hasNonRejectedSeason})",
            "BarrioService",
            relatedEntityId: barrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(year);

        return newSeason;
    }

    public async Task UpdateSeasonAsync(Guid seasonId, BarrioSeasonData data,
        CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");

        var now = _clock.GetCurrentInstant();
        season.BlurbLong = data.BlurbLong;
        season.BlurbShort = data.BlurbShort;
        season.Languages = data.Languages;
        season.AcceptingMembers = data.AcceptingMembers;
        season.KidsWelcome = data.KidsWelcome;
        season.KidsVisiting = data.KidsVisiting;
        season.KidsAreaDescription = data.KidsAreaDescription;
        season.HasPerformanceSpace = data.HasPerformanceSpace;
        season.PerformanceTypes = data.PerformanceTypes;
        season.Vibes = new List<BarrioVibe>(data.Vibes);
        season.AdultPlayspace = data.AdultPlayspace;
        season.MemberCount = data.MemberCount;
        season.SpaceRequirement = data.SpaceRequirement;
        season.SoundZone = data.SoundZone;
        season.ContainerCount = data.ContainerCount;
        season.ContainerNotes = data.ContainerNotes;
        season.ElectricalGrid = data.ElectricalGrid;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioUpdated, nameof(BarrioSeason), seasonId,
            $"Updated season {season.Year} details",
            "BarrioService",
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    public async Task ApproveSeasonAsync(Guid seasonId, Guid reviewedByUserId, string? notes,
        CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");
        if (season.Status != BarrioSeasonStatus.Pending)
            throw new InvalidOperationException($"Cannot approve a season with status {season.Status}.");

        var now = _clock.GetCurrentInstant();
        season.Status = BarrioSeasonStatus.Active;
        season.ReviewedByUserId = reviewedByUserId;
        season.ReviewNotes = notes;
        season.ResolvedAt = now;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonApproved, nameof(BarrioSeason), seasonId,
            $"Approved season {season.Year}",
            reviewedByUserId, reviewedByUserId.ToString(),
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    public async Task RejectSeasonAsync(Guid seasonId, Guid reviewedByUserId, string notes,
        CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");
        if (season.Status != BarrioSeasonStatus.Pending)
            throw new InvalidOperationException($"Cannot reject a season with status {season.Status}.");

        var now = _clock.GetCurrentInstant();
        season.Status = BarrioSeasonStatus.Rejected;
        season.ReviewedByUserId = reviewedByUserId;
        season.ReviewNotes = notes;
        season.ResolvedAt = now;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonRejected, nameof(BarrioSeason), seasonId,
            $"Rejected season {season.Year}: {notes}",
            reviewedByUserId, reviewedByUserId.ToString(),
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    public async Task WithdrawSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");
        if (season.Status != BarrioSeasonStatus.Pending && season.Status != BarrioSeasonStatus.Active)
            throw new InvalidOperationException($"Cannot withdraw a season with status {season.Status}.");

        var now = _clock.GetCurrentInstant();
        season.Status = BarrioSeasonStatus.Withdrawn;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonWithdrawn, nameof(BarrioSeason), seasonId,
            $"Withdrew from season {season.Year}",
            "BarrioService",
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    public async Task SetSeasonFullAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");
        if (season.Status != BarrioSeasonStatus.Active)
            throw new InvalidOperationException($"Cannot set full on a season with status {season.Status}.");

        var now = _clock.GetCurrentInstant();
        season.Status = BarrioSeasonStatus.Full;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonStatusChanged, nameof(BarrioSeason), seasonId,
            $"Season {season.Year} marked as full",
            "BarrioService",
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    public async Task ReactivateSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");
        if (season.Status != BarrioSeasonStatus.Full && season.Status != BarrioSeasonStatus.Inactive)
            throw new InvalidOperationException($"Cannot reactivate a season with status {season.Status}.");

        var now = _clock.GetCurrentInstant();
        season.Status = BarrioSeasonStatus.Active;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonStatusChanged, nameof(BarrioSeason), seasonId,
            $"Season {season.Year} reactivated",
            "BarrioService",
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    public async Task DeactivateSeasonAsync(Guid seasonId, CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");
        if (season.Status != BarrioSeasonStatus.Active && season.Status != BarrioSeasonStatus.Full)
            throw new InvalidOperationException($"Cannot deactivate a season with status {season.Status}.");

        var now = _clock.GetCurrentInstant();
        season.Status = BarrioSeasonStatus.Inactive;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioSeasonStatusChanged, nameof(BarrioSeason), seasonId,
            $"Season {season.Year} deactivated",
            "BarrioService",
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    // ==========================================================================
    // Barrio updates
    // ==========================================================================

    public async Task UpdateBarrioAsync(Guid barrioId, string contactEmail, string contactPhone,
        string? webOrSocialUrl, string contactMethod, bool isSwissCamp, int timesAtNowhere,
        CancellationToken cancellationToken = default)
    {
        var barrio = await _dbContext.Barrios.FindAsync([barrioId], cancellationToken)
            ?? throw new InvalidOperationException("Barrio not found.");

        barrio.ContactEmail = contactEmail;
        barrio.ContactPhone = contactPhone;
        barrio.WebOrSocialUrl = webOrSocialUrl;
        barrio.ContactMethod = contactMethod;
        barrio.IsSwissCamp = isSwissCamp;
        barrio.TimesAtNowhere = timesAtNowhere;
        barrio.UpdatedAt = _clock.GetCurrentInstant();

        await _auditLogService.LogAsync(
            AuditAction.BarrioUpdated, nameof(Barrio), barrioId,
            $"Updated barrio '{barrio.Slug}'",
            "BarrioService");

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteBarrioAsync(Guid barrioId, CancellationToken cancellationToken = default)
    {
        var barrio = await _dbContext.Barrios.FindAsync([barrioId], cancellationToken)
            ?? throw new InvalidOperationException("Barrio not found.");

        // Delete images from filesystem
        var images = await _dbContext.BarrioImages
            .Where(i => i.BarrioId == barrioId).ToListAsync(cancellationToken);
        foreach (var img in images)
        {
            var fullPath = Path.Combine("wwwroot", img.StoragePath);
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }

        // Get years for cache invalidation
        var years = await _dbContext.BarrioSeasons
            .Where(s => s.BarrioId == barrioId)
            .Select(s => s.Year)
            .Distinct()
            .ToListAsync(cancellationToken);

        await _auditLogService.LogAsync(
            AuditAction.BarrioDeleted, nameof(Barrio), barrioId,
            $"Barrio '{barrio.Slug}' permanently deleted",
            "BarrioService");

        _dbContext.Barrios.Remove(barrio); // cascade deletes children
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var year in years)
            InvalidateCache(year);
    }

    // ==========================================================================
    // Lead management
    // ==========================================================================

    public async Task<BarrioLead> AddLeadAsync(Guid barrioId, Guid userId, BarrioLeadRole role,
        CancellationToken cancellationToken = default)
    {
        var activeCount = await _dbContext.BarrioLeads
            .CountAsync(l => l.BarrioId == barrioId && l.LeftAt == null, cancellationToken);
        if (activeCount >= 5)
            throw new InvalidOperationException("Barrio already has the maximum of 5 leads.");

        var now = _clock.GetCurrentInstant();
        var lead = new BarrioLead
        {
            Id = Guid.NewGuid(),
            BarrioId = barrioId,
            UserId = userId,
            Role = role,
            JoinedAt = now
        };

        _dbContext.BarrioLeads.Add(lead);

        await _auditLogService.LogAsync(
            AuditAction.BarrioLeadAdded, nameof(BarrioLead), lead.Id,
            $"Added as {role}",
            userId, userId.ToString(),
            relatedEntityId: barrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return lead;
    }

    public async Task RemoveLeadAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        var lead = await _dbContext.BarrioLeads.FindAsync([leadId], cancellationToken)
            ?? throw new InvalidOperationException("Lead not found.");
        if (lead.Role == BarrioLeadRole.Primary)
            throw new InvalidOperationException("Cannot remove primary lead. Transfer primary role first.");

        lead.LeftAt = _clock.GetCurrentInstant();

        await _auditLogService.LogAsync(
            AuditAction.BarrioLeadRemoved, nameof(BarrioLead), leadId,
            "Removed from barrio leads",
            lead.UserId, lead.UserId.ToString(),
            relatedEntityId: lead.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task TransferPrimaryLeadAsync(Guid barrioId, Guid newPrimaryUserId,
        CancellationToken cancellationToken = default)
    {
        var leads = await _dbContext.BarrioLeads
            .Where(l => l.BarrioId == barrioId && l.LeftAt == null)
            .ToListAsync(cancellationToken);

        var currentPrimary = leads.FirstOrDefault(l => l.Role == BarrioLeadRole.Primary)
            ?? throw new InvalidOperationException("No current primary lead found.");
        var newPrimary = leads.FirstOrDefault(l => l.UserId == newPrimaryUserId)
            ?? throw new InvalidOperationException("Target user is not an active lead.");

        currentPrimary.Role = BarrioLeadRole.CoLead;
        newPrimary.Role = BarrioLeadRole.Primary;

        await _auditLogService.LogAsync(
            AuditAction.BarrioPrimaryLeadTransferred, nameof(BarrioLead), barrioId,
            $"Primary transferred from {currentPrimary.UserId} to {newPrimaryUserId}",
            newPrimaryUserId, newPrimaryUserId.ToString(),
            relatedEntityId: currentPrimary.UserId, relatedEntityType: nameof(User));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // ==========================================================================
    // Authorization checks
    // ==========================================================================

    public async Task<bool> IsUserBarrioLeadAsync(Guid userId, Guid barrioId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.BarrioLeads
            .AnyAsync(l => l.BarrioId == barrioId && l.UserId == userId && l.LeftAt == null,
                cancellationToken);
    }

    public async Task<bool> IsUserPrimaryLeadAsync(Guid userId, Guid barrioId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.BarrioLeads
            .AnyAsync(l => l.BarrioId == barrioId && l.UserId == userId
                && l.Role == BarrioLeadRole.Primary && l.LeftAt == null,
                cancellationToken);
    }

    // ==========================================================================
    // Images
    // ==========================================================================

    public async Task<BarrioImage> UploadImageAsync(Guid barrioId, Stream fileStream,
        string fileName, string contentType, long length,
        CancellationToken cancellationToken = default)
    {
        var imageCount = await _dbContext.BarrioImages
            .CountAsync(i => i.BarrioId == barrioId, cancellationToken);
        if (imageCount >= 5)
            throw new InvalidOperationException("Maximum 5 images per barrio.");

        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(contentType))
            throw new InvalidOperationException("Only JPEG, PNG, and WebP images are allowed.");

        if (length > 10 * 1024 * 1024)
            throw new InvalidOperationException("Image must be under 10MB.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid()}{ext}";
        var relativePath = Path.Combine("uploads", "barrios", barrioId.ToString(), storedFileName);
        var fullPath = Path.Combine("wwwroot", relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var stream = new FileStream(fullPath, FileMode.Create);
        await fileStream.CopyToAsync(stream, cancellationToken);

        var image = new BarrioImage
        {
            Id = Guid.NewGuid(),
            BarrioId = barrioId,
            FileName = fileName,
            StoragePath = relativePath,
            ContentType = contentType,
            SortOrder = imageCount,
            UploadedAt = _clock.GetCurrentInstant()
        };

        _dbContext.BarrioImages.Add(image);

        await _auditLogService.LogAsync(
            AuditAction.BarrioImageUploaded, nameof(BarrioImage), image.Id,
            $"Uploaded image '{fileName}'",
            "BarrioService",
            relatedEntityId: barrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return image;
    }

    public async Task DeleteImageAsync(Guid imageId, CancellationToken cancellationToken = default)
    {
        var image = await _dbContext.BarrioImages.FindAsync([imageId], cancellationToken)
            ?? throw new InvalidOperationException("Image not found.");

        var fullPath = Path.Combine("wwwroot", image.StoragePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        _dbContext.BarrioImages.Remove(image);

        await _auditLogService.LogAsync(
            AuditAction.BarrioImageDeleted, nameof(BarrioImage), imageId,
            $"Deleted image '{image.FileName}'",
            "BarrioService",
            relatedEntityId: image.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderImagesAsync(Guid barrioId, List<Guid> imageIdsInOrder,
        CancellationToken cancellationToken = default)
    {
        var images = await _dbContext.BarrioImages
            .Where(i => i.BarrioId == barrioId)
            .ToListAsync(cancellationToken);

        for (var i = 0; i < imageIdsInOrder.Count; i++)
        {
            var image = images.FirstOrDefault(img => img.Id == imageIdsInOrder[i]);
            if (image is not null)
                image.SortOrder = i;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // ==========================================================================
    // Settings (CampAdmin)
    // ==========================================================================

    public async Task SetPublicYearAsync(int year, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.BarrioSettings.FirstAsync(cancellationToken);
        settings.PublicYear = year;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task OpenSeasonAsync(int year, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.BarrioSettings.FirstAsync(cancellationToken);
        if (!settings.OpenSeasons.Contains(year))
        {
            settings.OpenSeasons.Add(year);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task CloseSeasonAsync(int year, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.BarrioSettings.FirstAsync(cancellationToken);
        if (settings.OpenSeasons.Remove(year))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SetNameLockDateAsync(int year, LocalDate lockDate,
        CancellationToken cancellationToken = default)
    {
        var seasons = await _dbContext.BarrioSeasons
            .Where(s => s.Year == year)
            .ToListAsync(cancellationToken);

        foreach (var season in seasons)
        {
            season.NameLockDate = lockDate;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // ==========================================================================
    // Name change
    // ==========================================================================

    public async Task ChangeSeasonNameAsync(Guid seasonId, string newName,
        CancellationToken cancellationToken = default)
    {
        var season = await _dbContext.BarrioSeasons.FindAsync([seasonId], cancellationToken)
            ?? throw new InvalidOperationException("Season not found.");

        // Check name lock
        if (season.NameLockDate.HasValue)
        {
            var today = _clock.GetCurrentInstant().InUtc().Date;
            if (today >= season.NameLockDate.Value)
                throw new InvalidOperationException("Season name is locked and cannot be changed.");
        }

        var oldName = season.Name;
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        var now = _clock.GetCurrentInstant();

        // Log old name to history
        _dbContext.BarrioHistoricalNames.Add(new BarrioHistoricalName
        {
            Id = Guid.NewGuid(),
            BarrioId = season.BarrioId,
            Name = oldName,
            Year = season.Year,
            Source = BarrioNameSource.NameChange,
            CreatedAt = now
        });

        season.Name = newName;
        season.UpdatedAt = now;

        await _auditLogService.LogAsync(
            AuditAction.BarrioNameChanged, nameof(BarrioSeason), seasonId,
            $"Name changed from '{oldName}' to '{newName}'",
            "BarrioService",
            relatedEntityId: season.BarrioId, relatedEntityType: nameof(Barrio));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache(season.Year);
    }

    // ==========================================================================
    // Private helpers
    // ==========================================================================

    private void InvalidateCache(int year)
    {
        _cache.Remove($"{CacheKeyPrefix}{year}");
    }

    private static BarrioSeason CreateSeasonFromData(Guid barrioId, int year, string name,
        BarrioSeasonData data, Instant now)
    {
        return new BarrioSeason
        {
            Id = Guid.NewGuid(),
            BarrioId = barrioId,
            Year = year,
            Name = name,
            Status = BarrioSeasonStatus.Pending,
            BlurbLong = data.BlurbLong,
            BlurbShort = data.BlurbShort,
            Languages = data.Languages,
            AcceptingMembers = data.AcceptingMembers,
            KidsWelcome = data.KidsWelcome,
            KidsVisiting = data.KidsVisiting,
            KidsAreaDescription = data.KidsAreaDescription,
            HasPerformanceSpace = data.HasPerformanceSpace,
            PerformanceTypes = data.PerformanceTypes,
            Vibes = new List<BarrioVibe>(data.Vibes),
            AdultPlayspace = data.AdultPlayspace,
            MemberCount = data.MemberCount,
            SpaceRequirement = data.SpaceRequirement,
            SoundZone = data.SoundZone,
            ContainerCount = data.ContainerCount,
            ContainerNotes = data.ContainerNotes,
            ElectricalGrid = data.ElectricalGrid,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
