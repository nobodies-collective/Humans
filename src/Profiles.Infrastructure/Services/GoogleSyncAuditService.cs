using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Profiles.Application.Interfaces;
using Profiles.Domain.Entities;
using Profiles.Domain.Enums;
using Profiles.Infrastructure.Data;

namespace Profiles.Infrastructure.Services;

/// <summary>
/// Records Google sync audit entries by adding them to the DbContext.
/// Entries are NOT saved here — the caller's SaveChangesAsync persists them
/// atomically with the business operation.
/// </summary>
public class GoogleSyncAuditService : IGoogleSyncAuditService
{
    private readonly ProfilesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<GoogleSyncAuditService> _logger;

    public GoogleSyncAuditService(
        ProfilesDbContext dbContext,
        IClock clock,
        ILogger<GoogleSyncAuditService> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task LogAsync(
        Guid resourceId,
        Guid? userId,
        string userEmail,
        GoogleSyncAction action,
        string role,
        GoogleSyncSource source,
        bool success,
        string? errorMessage = null)
    {
        var entry = new GoogleSyncAuditEntry
        {
            Id = Guid.NewGuid(),
            ResourceId = resourceId,
            UserId = userId,
            UserEmail = userEmail,
            Action = action,
            Role = role,
            Source = source,
            Timestamp = _clock.GetCurrentInstant(),
            Success = success,
            ErrorMessage = errorMessage
        };

        _dbContext.GoogleSyncAuditEntries.Add(entry);

        _logger.LogDebug(
            "GoogleSyncAudit: {Action} {Role} for {Email} on resource {ResourceId} ({Source}, Success={Success})",
            action, role, userEmail, resourceId, source, success);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleSyncAuditEntry>> GetByResourceAsync(Guid resourceId)
    {
        return await _dbContext.GoogleSyncAuditEntries
            .AsNoTracking()
            .Include(e => e.User)
            .Where(e => e.ResourceId == resourceId)
            .OrderByDescending(e => e.Timestamp)
            .Take(200)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoogleSyncAuditEntry>> GetByUserAsync(Guid userId)
    {
        return await _dbContext.GoogleSyncAuditEntries
            .AsNoTracking()
            .Include(e => e.Resource)
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Timestamp)
            .Take(200)
            .ToListAsync();
    }
}
