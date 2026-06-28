using Humans.Application.Interfaces.Gate;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Services.Gate;
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
        services.AddScoped<IGateService, GateService>();

        return services;
    }
}
