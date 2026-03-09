using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NodaTime.Testing;
using NSubstitute;
using Humans.Application.Interfaces;
using Humans.Domain.Enums;
using Humans.Infrastructure.Jobs;
using Humans.Infrastructure.Services;
using Xunit;

namespace Humans.Application.Tests.Jobs;

public class GoogleResourceReconciliationJobTests : IDisposable
{
    private readonly IGoogleSyncService _googleSyncService;
    private readonly ISyncSettingsService _syncSettingsService;
    private readonly FakeClock _clock;
    private readonly HumansMetricsService _metrics;
    private readonly GoogleResourceReconciliationJob _job;

    public GoogleResourceReconciliationJobTests()
    {
        _googleSyncService = Substitute.For<IGoogleSyncService>();
        _syncSettingsService = Substitute.For<ISyncSettingsService>();
        _clock = new FakeClock(Instant.FromUtc(2026, 3, 9, 2, 0));
        _metrics = new HumansMetricsService(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<HumansMetricsService>>());

        _job = new GoogleResourceReconciliationJob(
            _googleSyncService,
            _syncSettingsService,
            _metrics,
            NullLogger<GoogleResourceReconciliationJob>.Instance,
            _clock);
    }

    public void Dispose()
    {
        _metrics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsAllServices_WhenAllModesAreNone()
    {
        _syncSettingsService.GetModeAsync(Arg.Any<SyncServiceType>(), Arg.Any<CancellationToken>())
            .Returns(SyncMode.None);

        await _job.ExecuteAsync();

        await _googleSyncService.DidNotReceive()
            .SyncResourcesByTypeAsync(Arg.Any<GoogleResourceType>(), Arg.Any<SyncAction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddOnly_WhenDriveModeIsAddOnly()
    {
        _syncSettingsService.GetModeAsync(SyncServiceType.GoogleDrive, Arg.Any<CancellationToken>())
            .Returns(SyncMode.AddOnly);
        _syncSettingsService.GetModeAsync(SyncServiceType.GoogleGroups, Arg.Any<CancellationToken>())
            .Returns(SyncMode.None);

        await _job.ExecuteAsync();

        await _googleSyncService.Received(1)
            .SyncResourcesByTypeAsync(GoogleResourceType.DriveFolder, SyncAction.AddOnly, Arg.Any<CancellationToken>());
        await _googleSyncService.DidNotReceive()
            .SyncResourcesByTypeAsync(GoogleResourceType.Group, Arg.Any<SyncAction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddAndRemove_WhenGroupsModeIsAddAndRemove()
    {
        _syncSettingsService.GetModeAsync(SyncServiceType.GoogleDrive, Arg.Any<CancellationToken>())
            .Returns(SyncMode.None);
        _syncSettingsService.GetModeAsync(SyncServiceType.GoogleGroups, Arg.Any<CancellationToken>())
            .Returns(SyncMode.AddAndRemove);

        await _job.ExecuteAsync();

        await _googleSyncService.DidNotReceive()
            .SyncResourcesByTypeAsync(GoogleResourceType.DriveFolder, Arg.Any<SyncAction>(), Arg.Any<CancellationToken>());
        await _googleSyncService.Received(1)
            .SyncResourcesByTypeAsync(GoogleResourceType.Group, SyncAction.AddAndRemove, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SyncsMultipleServices_WhenBothHaveModes()
    {
        _syncSettingsService.GetModeAsync(SyncServiceType.GoogleDrive, Arg.Any<CancellationToken>())
            .Returns(SyncMode.AddOnly);
        _syncSettingsService.GetModeAsync(SyncServiceType.GoogleGroups, Arg.Any<CancellationToken>())
            .Returns(SyncMode.AddAndRemove);

        await _job.ExecuteAsync();

        await _googleSyncService.Received(1)
            .SyncResourcesByTypeAsync(GoogleResourceType.DriveFolder, SyncAction.AddOnly, Arg.Any<CancellationToken>());
        await _googleSyncService.Received(1)
            .SyncResourcesByTypeAsync(GoogleResourceType.Group, SyncAction.AddAndRemove, Arg.Any<CancellationToken>());
    }
}
