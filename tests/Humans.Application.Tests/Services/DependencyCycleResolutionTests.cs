using AwesomeAssertions;
using Humans.Application.Interfaces;
using Humans.Application.Interfaces.Caching;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Services.Auth;
using Humans.Application.Services.Shifts;
using Humans.Application.Services.Users;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Humans.Application.Tests.Services;

public class DependencyCycleResolutionTests
{
    [Fact]
    public void IUserService_Resolves_WhenTeamServiceAndRoleAssignmentServiceAreRegistered()
    {
        var options = new DbContextOptionsBuilder<HumansDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();

        services.AddScoped(_ => new HumansDbContext(options));
        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));

        services.AddScoped<IUserRepository>(_ => Substitute.For<IUserRepository>());
        services.AddScoped<IUserEmailRepository>(_ => Substitute.For<IUserEmailRepository>());
        services.AddScoped<IFullProfileInvalidator>(_ => Substitute.For<IFullProfileInvalidator>());
        services.AddScoped<IRoleAssignmentRepository>(_ => Substitute.For<IRoleAssignmentRepository>());
        services.AddScoped<IShiftManagementRepository>(_ => Substitute.For<IShiftManagementRepository>());
        services.AddScoped<IAuditLogService>(_ => Substitute.For<IAuditLogService>());
        services.AddScoped<IEmailService>(_ => Substitute.For<IEmailService>());
        services.AddScoped<INotificationEmitter>(_ => Substitute.For<INotificationEmitter>());
        services.AddScoped<ISystemTeamSync>(_ => Substitute.For<ISystemTeamSync>());
        services.AddScoped<INavBadgeCacheInvalidator>(_ => Substitute.For<INavBadgeCacheInvalidator>());
        services.AddScoped<IRoleAssignmentClaimsCacheInvalidator>(_ => Substitute.For<IRoleAssignmentClaimsCacheInvalidator>());
        services.AddScoped<NodaTime.IClock>(_ => Substitute.For<NodaTime.IClock>());

        services.AddScoped<UserService>();
        services.AddScoped<IUserService>(sp => sp.GetRequiredService<UserService>());

        services.AddScoped<RoleAssignmentService>();
        services.AddScoped<IRoleAssignmentService>(sp => sp.GetRequiredService<RoleAssignmentService>());

        services.AddScoped<ShiftManagementService>();
        services.AddScoped<IShiftManagementService>(sp => sp.GetRequiredService<ShiftManagementService>());

        services.AddScoped<TeamService>();
        services.AddScoped<ITeamService>(sp => sp.GetRequiredService<TeamService>());

        services.AddScoped<Microsoft.Extensions.Logging.ILogger<UserService>>(_ => NullLogger<UserService>.Instance);
        services.AddScoped<Microsoft.Extensions.Logging.ILogger<RoleAssignmentService>>(_ => NullLogger<RoleAssignmentService>.Instance);
        services.AddScoped<Microsoft.Extensions.Logging.ILogger<ShiftManagementService>>(_ => NullLogger<ShiftManagementService>.Instance);
        services.AddScoped<Microsoft.Extensions.Logging.ILogger<TeamService>>(_ => NullLogger<TeamService>.Instance);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IUserService>();

        resolve.Should().NotThrow();
        resolve().Should().BeOfType<UserService>();
    }
}
