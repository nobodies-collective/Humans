using Humans.Application.Interfaces;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Humans.Infrastructure.Jobs;

/// <summary>
/// Daily job that cancels signups on deactivated shifts after a 7-day grace period.
/// </summary>
public class SignupGarbageCollectionJob
{
    private static readonly Duration GracePeriod = Duration.FromDays(7);

    private readonly HumansDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly HumansMetricsService _metrics;
    private readonly ILogger<SignupGarbageCollectionJob> _logger;
    private readonly IClock _clock;

    public SignupGarbageCollectionJob(
        HumansDbContext dbContext,
        IAuditLogService auditLogService,
        HumansMetricsService metrics,
        ILogger<SignupGarbageCollectionJob> logger,
        IClock clock)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _metrics = metrics;
        _logger = logger;
        _clock = clock;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetCurrentInstant();
        var cutoff = now.Minus(GracePeriod);

        _logger.LogInformation("Starting signup garbage collection at {Time}", now);

        try
        {
            var staleSignups = await _dbContext.ShiftSignups
                .Include(d => d.Shift)
                .Where(d =>
                    (d.Status == SignupStatus.Confirmed || d.Status == SignupStatus.Pending) &&
                    !d.Shift.IsActive &&
                    d.Shift.UpdatedAt < cutoff)
                .ToListAsync(cancellationToken);

            if (staleSignups.Count == 0)
            {
                _metrics.RecordJobRun("signup_garbage_collection", "success");
                _logger.LogInformation("No stale signups found");
                return;
            }

            foreach (var signup in staleSignups)
            {
                signup.Cancel(_clock, "Shift deactivated (auto-cleanup)");

                await _auditLogService.LogAsync(
                    AuditAction.ShiftSignupCancelled,
                    nameof(Domain.Entities.ShiftSignup), signup.Id,
                    $"Auto-cancelled: shift '{signup.Shift.Title}' was deactivated",
                    nameof(SignupGarbageCollectionJob));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _metrics.RecordJobRun("signup_garbage_collection", "success");
            _logger.LogInformation(
                "Cancelled {Count} stale signups on deactivated shifts",
                staleSignups.Count);
        }
        catch (Exception ex)
        {
            _metrics.RecordJobRun("signup_garbage_collection", "failure");
            _logger.LogError(ex, "Error during signup garbage collection");
            throw;
        }
    }
}
