using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Humans.Application.DTOs;
using Humans.Application.Interfaces;
using Humans.Domain.Constants;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;

namespace Humans.Infrastructure.Jobs;

/// <summary>
/// Nightly job that emails each Board member a summary of the previous UTC day's approvals.
/// </summary>
public class SendBoardDailyDigestJob
{
    private readonly HumansDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly HumansMetricsService _metrics;
    private readonly ILogger<SendBoardDailyDigestJob> _logger;
    private readonly IClock _clock;

    public SendBoardDailyDigestJob(
        HumansDbContext dbContext,
        IEmailService emailService,
        HumansMetricsService metrics,
        ILogger<SendBoardDailyDigestJob> logger,
        IClock clock)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _metrics = metrics;
        _logger = logger;
        _clock = clock;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetCurrentInstant();
        var todayUtc = now.InUtc().Date;
        var yesterdayUtc = todayUtc.PlusDays(-1);
        var windowStart = yesterdayUtc.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var windowEnd = todayUtc.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var dateLabel = yesterdayUtc.ToString("yyyy-MM-dd", null);

        _logger.LogInformation(
            "Starting Board daily digest job for {Date} (window {Start} to {End})",
            dateLabel, windowStart, windowEnd);

        try
        {
            var groups = new List<BoardDigestTierGroup>();

            // 1. Volunteer approvals — from audit log (VolunteerApproved action)
            var volunteerUserIds = await _dbContext.AuditLogEntries
                .Where(e => e.Action == AuditAction.VolunteerApproved
                    && e.OccurredAt >= windowStart
                    && e.OccurredAt < windowEnd)
                .Select(e => e.EntityId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (volunteerUserIds.Count > 0)
            {
                var volunteerNames = await _dbContext.Users
                    .Where(u => volunteerUserIds.Contains(u.Id))
                    .Select(u => u.DisplayName)
                    .OrderBy(n => n)
                    .ToListAsync(cancellationToken);

                groups.Add(new BoardDigestTierGroup("Volunteer", volunteerNames));
            }

            // 2. Tier application approvals (Colaborador/Asociado)
            var tierApprovals = await _dbContext.Applications
                .Include(a => a.User)
                .Where(a => a.Status == ApplicationStatus.Approved
                    && a.ResolvedAt != null
                    && a.ResolvedAt.Value >= windowStart
                    && a.ResolvedAt.Value < windowEnd)
                .Select(a => new { a.MembershipTier, DisplayName = a.User.DisplayName })
                .ToListAsync(cancellationToken);

            foreach (var tierGroup in tierApprovals
                .GroupBy(a => a.MembershipTier)
                .OrderBy(g => g.Key))
            {
                var tierLabel = tierGroup.Key.ToString();
                var names = tierGroup.Select(a => a.DisplayName).OrderBy(n => n, StringComparer.Ordinal).ToList();
                groups.Add(new BoardDigestTierGroup(tierLabel, names));
            }

            // Nothing to report
            if (groups.Count == 0)
            {
                _logger.LogInformation("No approvals on {Date}, skipping Board digest", dateLabel);
                _metrics.RecordJobRun("board_daily_digest", "skipped");
                return;
            }

            // 3. Find active Board members
            var boardMembers = await _dbContext.RoleAssignments
                .Include(ra => ra.User)
                    .ThenInclude(u => u.UserEmails)
                .Where(ra => ra.RoleName == RoleNames.Board
                    && ra.ValidFrom <= now
                    && (ra.ValidTo == null || ra.ValidTo.Value > now))
                .Select(ra => ra.User)
                .Distinct()
                .ToListAsync(cancellationToken);

            var sentCount = 0;
            foreach (var member in boardMembers)
            {
                var email = member.GetEffectiveEmail();
                if (email == null)
                {
                    _logger.LogWarning("Board member {UserId} ({Name}) has no effective email, skipping digest",
                        member.Id, member.DisplayName);
                    continue;
                }

                await _emailService.SendBoardDailyDigestAsync(
                    email, member.DisplayName, dateLabel, groups,
                    member.PreferredLanguage, cancellationToken);
                sentCount++;
            }

            _metrics.RecordJobRun("board_daily_digest", "success");
            _logger.LogInformation(
                "Board daily digest sent to {Count} Board members for {Date} ({TierCount} tier groups, {TotalApprovals} approvals)",
                sentCount, dateLabel, groups.Count, groups.Sum(g => g.DisplayNames.Count));
        }
        catch (Exception ex)
        {
            _metrics.RecordJobRun("board_daily_digest", "failure");
            _logger.LogError(ex, "Error sending Board daily digest for {Date}", dateLabel);
            throw;
        }
    }
}
