using Hangfire;
using Humans.Infrastructure.Jobs;
using Microsoft.AspNetCore.Builder;

namespace Humans.Web.Extensions;

public static class RecurringJobExtensions
{
    public static void UseHumansRecurringJobs(this WebApplication app)
    {
        _ = app;

        // Google permission-modifying jobs are currently DISABLED (SystemTeamSyncJob,
        // GoogleResourceReconciliationJob). They could be destructive if upstream
        // membership/consent data is incorrect during rollout.
        //
        // RecurringJob.AddOrUpdate<SystemTeamSyncJob>(
        //     "system-team-sync",
        //     job => job.ExecuteAsync(CancellationToken.None),
        //     Cron.Hourly);
        //
        // RecurringJob.AddOrUpdate<GoogleResourceReconciliationJob>(
        //     "google-resource-reconciliation",
        //     job => job.ExecuteAsync(CancellationToken.None),
        //     "0 3 * * *");

        RecurringJob.AddOrUpdate<ProcessAccountDeletionsJob>(
            "process-account-deletions",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily);

        RecurringJob.AddOrUpdate<SyncLegalDocumentsJob>(
            "legal-document-sync",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 4 * * *");

        RecurringJob.AddOrUpdate<SuspendNonCompliantMembersJob>(
            "suspend-non-compliant-members",
            job => job.ExecuteAsync(CancellationToken.None),
            "30 4 * * *");

        RecurringJob.AddOrUpdate<ProcessGoogleSyncOutboxJob>(
            "process-google-sync-outbox",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Minutely);

        RecurringJob.AddOrUpdate<DriveActivityMonitorJob>(
            "drive-activity-monitor",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly);
    }
}
