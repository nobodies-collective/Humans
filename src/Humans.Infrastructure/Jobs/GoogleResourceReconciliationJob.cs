using Microsoft.Extensions.Logging;
using NodaTime;
using Humans.Application.Interfaces;
using Humans.Domain.Enums;
using Humans.Infrastructure.Services;

namespace Humans.Infrastructure.Jobs;

/// <summary>
/// Scheduled job that reconciles Google resources based on per-service sync mode settings.
/// </summary>
public class GoogleResourceReconciliationJob
{
    private readonly IGoogleSyncService _googleSyncService;
    private readonly ISyncSettingsService _syncSettingsService;
    private readonly HumansMetricsService _metrics;
    private readonly ILogger<GoogleResourceReconciliationJob> _logger;
    private readonly IClock _clock;

    public GoogleResourceReconciliationJob(
        IGoogleSyncService googleSyncService,
        ISyncSettingsService syncSettingsService,
        HumansMetricsService metrics,
        ILogger<GoogleResourceReconciliationJob> logger,
        IClock clock)
    {
        _googleSyncService = googleSyncService;
        _syncSettingsService = syncSettingsService;
        _metrics = metrics;
        _logger = logger;
        _clock = clock;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Google resource reconciliation at {Time}", _clock.GetCurrentInstant());

        try
        {
            await SyncServiceAsync(SyncServiceType.GoogleDrive, GoogleResourceType.DriveFolder, cancellationToken);
            await SyncServiceAsync(SyncServiceType.GoogleGroups, GoogleResourceType.Group, cancellationToken);

            _metrics.RecordJobRun("google_resource_reconciliation", "success");
            _logger.LogInformation("Completed Google resource reconciliation");
        }
        catch (Exception ex)
        {
            _metrics.RecordJobRun("google_resource_reconciliation", "failure");
            _logger.LogError(ex, "Error during Google resource reconciliation");
            throw;
        }
    }

    private async Task SyncServiceAsync(SyncServiceType serviceType, GoogleResourceType resourceType, CancellationToken ct)
    {
        var mode = await _syncSettingsService.GetModeAsync(serviceType, ct);
        if (mode == SyncMode.None)
        {
            _logger.LogInformation("Skipping {ServiceType} sync — mode is None", serviceType);
            return;
        }

        var action = mode switch
        {
            SyncMode.AddOnly => SyncAction.AddOnly,
            SyncMode.AddAndRemove => SyncAction.AddAndRemove,
            _ => SyncAction.Preview
        };

        _logger.LogInformation("Syncing {ServiceType} resources with action {Action}", serviceType, action);
        await _googleSyncService.SyncResourcesByTypeAsync(resourceType, action, ct);
    }
}
