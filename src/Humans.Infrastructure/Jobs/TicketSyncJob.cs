using Hangfire;
using Humans.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Humans.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job that syncs ticket data from the vendor.
/// Runs every 15 minutes by default. Can also be triggered manually.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
public class TicketSyncJob
{
    private readonly ITicketSyncService _syncService;
    private readonly ILogger<TicketSyncJob> _logger;

    public TicketSyncJob(
        ITicketSyncService syncService,
        ILogger<TicketSyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting ticket sync job");

        try
        {
            var result = await _syncService.SyncOrdersAndAttendeesAsync(cancellationToken);

            _logger.LogInformation(
                "Ticket sync job completed: {Orders} orders, {Attendees} attendees synced",
                result.OrdersSynced, result.AttendeesSynced);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ticket sync job failed");
            throw;
        }
    }
}
