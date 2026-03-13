using AwesomeAssertions;
using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NodaTime.Testing;
using NSubstitute;
using Xunit;

namespace Humans.Application.Tests.Services;

public class BarrioServiceTests : IDisposable
{
    private readonly HumansDbContext _dbContext;
    private readonly FakeClock _clock;
    private readonly BarrioService _service;
    private readonly IAuditLogService _auditLog;

    public BarrioServiceTests()
    {
        var options = new DbContextOptionsBuilder<HumansDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new HumansDbContext(options);
        _clock = new FakeClock(Instant.FromUtc(2026, 3, 13, 12, 0));
        _auditLog = Substitute.For<IAuditLogService>();

        _service = new BarrioService(
            _dbContext,
            _auditLog,
            _clock,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<BarrioService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    // ==========================================================================
    // CreateBarrioAsync
    // ==========================================================================

    [Fact]
    public async Task CreateBarrioAsync_NewCamp_CreatesBarrioWithPendingSeason()
    {
        await SeedSettingsAsync();
        var userId = Guid.NewGuid();

        var barrio = await _service.CreateBarrioAsync(
            userId, "Camp Funhouse", "camp@fun.com", "+34612345678",
            "https://instagram.com/funhouse", "DM us on Instagram",
            isSwissCamp: false, timesAtNowhere: 0,
            MakeSeasonData(), historicalNames: null, year: 2026);

        barrio.Slug.Should().Be("camp-funhouse");
        barrio.CreatedByUserId.Should().Be(userId);

        var season = await _dbContext.BarrioSeasons
            .FirstOrDefaultAsync(s => s.BarrioId == barrio.Id);
        season.Should().NotBeNull();
        season!.Status.Should().Be(BarrioSeasonStatus.Pending);
        season.Year.Should().Be(2026);
        season.Name.Should().Be("Camp Funhouse");

        var lead = await _dbContext.BarrioLeads
            .FirstOrDefaultAsync(l => l.BarrioId == barrio.Id);
        lead.Should().NotBeNull();
        lead!.UserId.Should().Be(userId);
        lead.Role.Should().Be(BarrioLeadRole.Primary);
    }

    [Fact]
    public async Task CreateBarrioAsync_ReservedSlug_ThrowsInvalidOperation()
    {
        await SeedSettingsAsync();
        var userId = Guid.NewGuid();

        var act = () => _service.CreateBarrioAsync(
            userId, "Register", "camp@test.com", "+34600000000",
            null, "email", false, 0, MakeSeasonData(), null, 2026);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*reserved*");
    }

    // ==========================================================================
    // ApproveSeasonAsync
    // ==========================================================================

    [Fact]
    public async Task ApproveSeasonAsync_PendingSeason_SetsActive()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var season = await _dbContext.BarrioSeasons.FirstAsync(s => s.BarrioId == barrio.Id);
        var adminId = Guid.NewGuid();

        await _service.ApproveSeasonAsync(season.Id, adminId, "Looks good");

        var updated = await _dbContext.BarrioSeasons.FindAsync(season.Id);
        updated!.Status.Should().Be(BarrioSeasonStatus.Active);
        updated.ReviewedByUserId.Should().Be(adminId);
        updated.ReviewNotes.Should().Be("Looks good");
        updated.ResolvedAt.Should().NotBeNull();
    }

    // ==========================================================================
    // RejectSeasonAsync
    // ==========================================================================

    [Fact]
    public async Task RejectSeasonAsync_PendingSeason_SetsRejected()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var season = await _dbContext.BarrioSeasons.FirstAsync(s => s.BarrioId == barrio.Id);

        await _service.RejectSeasonAsync(season.Id, Guid.NewGuid(), "Not a real camp");

        var updated = await _dbContext.BarrioSeasons.FindAsync(season.Id);
        updated!.Status.Should().Be(BarrioSeasonStatus.Rejected);
        updated.ReviewNotes.Should().Be("Not a real camp");
    }

    // ==========================================================================
    // OptInToSeasonAsync
    // ==========================================================================

    [Fact]
    public async Task OptInToSeasonAsync_ReturningCamp_AutoApproves()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var season = await _dbContext.BarrioSeasons.FirstAsync(s => s.BarrioId == barrio.Id);
        await _service.ApproveSeasonAsync(season.Id, Guid.NewGuid(), null);

        // Open 2027 season in settings
        var settings = await _dbContext.BarrioSettings.FirstAsync();
        settings.OpenSeasons = new List<int> { 2026, 2027 };
        await _dbContext.SaveChangesAsync();

        var newSeason = await _service.OptInToSeasonAsync(barrio.Id, 2027);

        newSeason.Status.Should().Be(BarrioSeasonStatus.Active);
        newSeason.Year.Should().Be(2027);
        newSeason.BlurbLong.Should().Be("A fun camp for everyone"); // copied
    }

    [Fact]
    public async Task OptInToSeasonAsync_PreviouslyRejected_GoesPending()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var season = await _dbContext.BarrioSeasons.FirstAsync(s => s.BarrioId == barrio.Id);
        await _service.RejectSeasonAsync(season.Id, Guid.NewGuid(), "nope");

        var settings = await _dbContext.BarrioSettings.FirstAsync();
        settings.OpenSeasons = new List<int> { 2026, 2027 };
        await _dbContext.SaveChangesAsync();

        var newSeason = await _service.OptInToSeasonAsync(barrio.Id, 2027);

        newSeason.Status.Should().Be(BarrioSeasonStatus.Pending);
    }

    // ==========================================================================
    // AddLeadAsync
    // ==========================================================================

    [Fact]
    public async Task AddLeadAsync_UnderMax_AddsCoLead()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var coLeadId = Guid.NewGuid();

        var lead = await _service.AddLeadAsync(barrio.Id, coLeadId, BarrioLeadRole.CoLead);

        lead.Role.Should().Be(BarrioLeadRole.CoLead);
        lead.UserId.Should().Be(coLeadId);
    }

    [Fact]
    public async Task AddLeadAsync_AtMaxLeads_Throws()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        // Add 4 co-leads (1 primary + 4 = 5 max)
        for (var i = 0; i < 4; i++)
            await _service.AddLeadAsync(barrio.Id, Guid.NewGuid(), BarrioLeadRole.CoLead);

        var act = () => _service.AddLeadAsync(barrio.Id, Guid.NewGuid(), BarrioLeadRole.CoLead);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*maximum*");
    }

    // ==========================================================================
    // TransferPrimaryLeadAsync
    // ==========================================================================

    [Fact]
    public async Task TransferPrimaryLeadAsync_ToCoLead_SwapsRoles()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var coLeadId = Guid.NewGuid();
        await _service.AddLeadAsync(barrio.Id, coLeadId, BarrioLeadRole.CoLead);

        await _service.TransferPrimaryLeadAsync(barrio.Id, coLeadId);

        var leads = await _dbContext.BarrioLeads
            .Where(l => l.BarrioId == barrio.Id && l.LeftAt == null)
            .ToListAsync();
        leads.First(l => l.UserId == coLeadId).Role.Should().Be(BarrioLeadRole.Primary);
    }

    // ==========================================================================
    // IsUserBarrioLeadAsync
    // ==========================================================================

    [Fact]
    public async Task IsUserBarrioLeadAsync_ActiveLead_ReturnsTrue()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var leadUserId = barrio.CreatedByUserId;

        var result = await _service.IsUserBarrioLeadAsync(leadUserId, barrio.Id);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserBarrioLeadAsync_NonLead_ReturnsFalse()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var result = await _service.IsUserBarrioLeadAsync(Guid.NewGuid(), barrio.Id);
        result.Should().BeFalse();
    }

    // ==========================================================================
    // ChangeSeasonNameAsync
    // ==========================================================================

    [Fact]
    public async Task ChangeSeasonNameAsync_LogsOldNameToHistory()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var season = await _dbContext.BarrioSeasons.FirstAsync(s => s.BarrioId == barrio.Id);
        await _service.ApproveSeasonAsync(season.Id, Guid.NewGuid(), null);

        await _service.ChangeSeasonNameAsync(season.Id, "New Name");

        var updated = await _dbContext.BarrioSeasons.FindAsync(season.Id);
        updated!.Name.Should().Be("New Name");

        var historical = await _dbContext.BarrioHistoricalNames
            .FirstOrDefaultAsync(h => h.BarrioId == barrio.Id && h.Source == BarrioNameSource.NameChange);
        historical.Should().NotBeNull();
        historical!.Name.Should().Be("Test Camp");
    }

    [Fact]
    public async Task ChangeSeasonNameAsync_AfterLockDate_Throws()
    {
        await SeedSettingsAsync();
        var barrio = await CreateTestBarrio();
        var season = await _dbContext.BarrioSeasons.FirstAsync(s => s.BarrioId == barrio.Id);
        await _service.ApproveSeasonAsync(season.Id, Guid.NewGuid(), null);

        // Set lock date in the past
        season.NameLockDate = new LocalDate(2026, 3, 1);
        await _dbContext.SaveChangesAsync();

        var act = () => _service.ChangeSeasonNameAsync(season.Id, "Too Late");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*locked*");
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private static BarrioSeasonData MakeSeasonData() => new(
        BlurbLong: "A fun camp for everyone",
        BlurbShort: "Fun camp",
        Languages: "English, Spanish",
        AcceptingMembers: YesNoMaybe.Yes,
        KidsWelcome: YesNoMaybe.Maybe,
        KidsVisiting: KidsVisitingPolicy.DaytimeOnly,
        KidsAreaDescription: null,
        HasPerformanceSpace: PerformanceSpaceStatus.Yes,
        PerformanceTypes: "Music, dance",
        Vibes: new List<BarrioVibe> { BarrioVibe.LiveMusic, BarrioVibe.ChillOut },
        AdultPlayspace: AdultPlayspacePolicy.No,
        MemberCount: 25,
        SpaceRequirement: SpaceSize.Sqm600,
        SoundZone: SoundZone.Yellow,
        ContainerCount: 1,
        ContainerNotes: null,
        ElectricalGrid: ElectricalGrid.Yellow);

    private async Task<Barrio> CreateTestBarrio()
    {
        return await _service.CreateBarrioAsync(
            Guid.NewGuid(), "Test Camp", "test@camp.com", "+34600000000",
            null, "email us", false, 1, MakeSeasonData(), null, 2026);
    }

    private async Task SeedSettingsAsync()
    {
        if (!await _dbContext.BarrioSettings.AnyAsync())
        {
            _dbContext.BarrioSettings.Add(new BarrioSettings
            {
                Id = Guid.Parse("00000000-0000-0000-0010-000000000001"),
                PublicYear = 2026,
                OpenSeasons = new List<int> { 2026 }
            });
            await _dbContext.SaveChangesAsync();
        }
    }
}
