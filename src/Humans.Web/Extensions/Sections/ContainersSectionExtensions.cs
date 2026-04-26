using Humans.Application.Interfaces.Containers;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Services.Containers;
using Humans.Infrastructure.Repositories.Containers;
using Humans.Infrastructure.Services;

namespace Humans.Web.Extensions.Sections;

internal static class ContainersSectionExtensions
{
    internal static IServiceCollection AddContainersSection(this IServiceCollection services)
    {
        services.AddSingleton<IContainerRepository, ContainerRepository>();
        services.AddSingleton<IContainerImageStorage, ContainerImageStorage>();
        services.AddScoped<IContainerService, ContainerService>();

        return services;
    }
}
