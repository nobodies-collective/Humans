using Humans.Application.Interfaces.Gate;
using Humans.Application.Interfaces.Gdpr;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Interfaces.Users;
using Humans.Application.Services.Gate;
using Humans.Infrastructure.Jobs;
using Humans.Infrastructure.Repositories.Gate;

namespace Humans.Web.Extensions.Sections;

internal static class GateSectionExtensions
{
    internal static IServiceCollection AddGateSection(this IServiceCollection services)
    {
        // Repository: Singleton with a short-lived DbContext via IDbContextFactory,
        // matching the other section repositories.
        services.AddSingleton<IGateRepository, GateRepository>();

        // Service: Scoped — it composes cross-section reads and the gate repository.
        // No caching decorator: gate reads must be live (a stale verdict admits or
        // blocks the wrong person), mirroring the read-through Scanner section.
        // Registered as its concrete type so the GDPR-contributor and account-merge
        // forwarding factories resolve the same section service (§8a / merge fan-out).
        services.AddScoped<GateService>();
        services.AddScoped<IGateService>(sp => sp.GetRequiredService<GateService>());
        services.AddScoped<IUserDataContributor>(sp => sp.GetRequiredService<GateService>());
        services.AddScoped<IUserMerge>(sp => sp.GetRequiredService<GateService>());

        // Retention purge (daily recurring job, see RecurringJobExtensions).
        services.AddScoped<GateRetentionJob>();

        // Best-effort vendor check-in mirror (enqueued by the controller on admit).
        services.AddScoped<GateVendorCheckInJob>();

        return services;
    }
}
