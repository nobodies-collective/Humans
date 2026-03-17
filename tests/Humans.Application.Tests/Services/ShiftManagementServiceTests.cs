using AwesomeAssertions;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace Humans.Application.Tests.Services;

public class ShiftManagementServiceTests : IDisposable
{
    private readonly HumansDbContext _dbContext;
    private readonly FakeClock _clock;
    private readonly ShiftManagementService _service;

    private static readonly Instant TestNow = Instant.FromUtc(2026, 6, 15, 12, 0);

    public ShiftManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<HumansDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new HumansDbContext(options);
        _clock = new FakeClock(TestNow);

        _service = new ShiftManagementService(
            _dbContext,
            new MemoryCache(new MemoryCacheOptions()),
            _clock,
            NullLogger<ShiftManagementService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    // ============================================================
    // CreateBuildStrikeShiftsAsync
    // ============================================================

    [Fact]
    public async Task CreateBuildStrikeShifts_CreatesOneAllDayShiftPerDay()
    {
        // Arrange: rota with Period=Build, staffing grid for days -3 to -1
        var (es, rota) = SeedRotaScenario(RotaPeriod.Build);
        await _dbContext.SaveChangesAsync();

        var staffing = new Dictionary<int, (int Min, int Max)>
        {
            [-3] = (2, 5),
            [-2] = (2, 5),
            [-1] = (2, 5)
        };

        // Act
        await _service.CreateBuildStrikeShiftsAsync(rota.Id, staffing);

        // Assert: 3 shifts created, all IsAllDay=true, correct DayOffsets
        var shifts = await _dbContext.Shifts.Where(s => s.RotaId == rota.Id).ToListAsync();
        shifts.Should().HaveCount(3);
        shifts.Should().AllSatisfy(s =>
        {
            s.IsAllDay.Should().BeTrue();
            s.StartTime.Should().Be(new LocalTime(0, 0));
            s.Duration.Should().Be(Duration.FromHours(24));
        });
        shifts.Select(s => s.DayOffset).Should().BeEquivalentTo(new[] { -3, -2, -1 });
    }

    [Fact]
    public async Task CreateBuildStrikeShifts_SetsCorrectMinMaxPerDay()
    {
        // Arrange: staffing grid with varying min/max per day
        var (es, rota) = SeedRotaScenario(RotaPeriod.Build);
        await _dbContext.SaveChangesAsync();

        var staffing = new Dictionary<int, (int Min, int Max)>
        {
            [-3] = (1, 3),
            [-2] = (4, 8),
            [-1] = (2, 6)
        };

        // Act
        await _service.CreateBuildStrikeShiftsAsync(rota.Id, staffing);

        // Assert: each shift has the min/max from its corresponding day in the grid
        var shifts = await _dbContext.Shifts
            .Where(s => s.RotaId == rota.Id)
            .OrderBy(s => s.DayOffset)
            .ToListAsync();

        shifts[0].MinVolunteers.Should().Be(1);
        shifts[0].MaxVolunteers.Should().Be(3);
        shifts[1].MinVolunteers.Should().Be(4);
        shifts[1].MaxVolunteers.Should().Be(8);
        shifts[2].MinVolunteers.Should().Be(2);
        shifts[2].MaxVolunteers.Should().Be(6);
    }

    [Fact]
    public async Task CreateBuildStrikeShifts_RejectsEventPeriodRota()
    {
        // Arrange: rota with Period=Event
        var (es, rota) = SeedRotaScenario(RotaPeriod.Event);
        await _dbContext.SaveChangesAsync();

        var staffing = new Dictionary<int, (int Min, int Max)>
        {
            [0] = (2, 5)
        };

        // Act + Assert: throws InvalidOperationException
        var act = () => _service.CreateBuildStrikeShiftsAsync(rota.Id, staffing);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ============================================================
    // GenerateEventShiftsAsync
    // ============================================================

    [Fact]
    public async Task GenerateEventShifts_CreatesCartesianProduct()
    {
        // Arrange: event rota, days 0-2, slots [(08:00, 4h), (14:00, 4h)]
        var (es, rota) = SeedRotaScenario(RotaPeriod.Event);
        await _dbContext.SaveChangesAsync();

        var timeSlots = new List<(LocalTime StartTime, double DurationHours)>
        {
            (new LocalTime(8, 0), 4),
            (new LocalTime(14, 0), 4)
        };

        // Act
        await _service.GenerateEventShiftsAsync(rota.Id, 0, 2, timeSlots);

        // Assert: 6 shifts (3 days × 2 slots), none IsAllDay
        var shifts = await _dbContext.Shifts.Where(s => s.RotaId == rota.Id).ToListAsync();
        shifts.Should().HaveCount(6);
        shifts.Should().AllSatisfy(s => s.IsAllDay.Should().BeFalse());

        // Verify correct day offsets
        shifts.Select(s => s.DayOffset).Distinct().Should().BeEquivalentTo(new[] { 0, 1, 2 });

        // Verify correct start times
        var startTimes = shifts.Select(s => s.StartTime).Distinct().ToList();
        startTimes.Should().HaveCount(2);
        startTimes.Should().Contain(new LocalTime(8, 0));
        startTimes.Should().Contain(new LocalTime(14, 0));
    }

    [Fact]
    public async Task GenerateEventShifts_RejectsBuildPeriodRota()
    {
        // Arrange: rota with Period=Build
        var (es, rota) = SeedRotaScenario(RotaPeriod.Build);
        await _dbContext.SaveChangesAsync();

        var timeSlots = new List<(LocalTime StartTime, double DurationHours)>
        {
            (new LocalTime(8, 0), 4)
        };

        // Act + Assert: throws InvalidOperationException
        var act = () => _service.GenerateEventShiftsAsync(rota.Id, 0, 2, timeSlots);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ============================================================
    // Helpers
    // ============================================================

    private (EventSettings es, Rota rota) SeedRotaScenario(RotaPeriod period)
    {
        var es = new EventSettings
        {
            Id = Guid.NewGuid(),
            EventName = "Test Event 2026",
            TimeZoneId = "Europe/Madrid",
            GateOpeningDate = new LocalDate(2026, 7, 1),
            BuildStartOffset = -14,
            EventEndOffset = 6,
            StrikeEndOffset = 9,
            IsShiftBrowsingOpen = true,
            IsActive = true,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };
        _dbContext.EventSettings.Add(es);

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Test Department",
            Slug = "test-dept",
            SystemTeamType = SystemTeamType.None,
            ParentTeamId = null,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };
        _dbContext.Teams.Add(team);

        var rota = new Rota
        {
            Id = Guid.NewGuid(),
            EventSettingsId = es.Id,
            TeamId = team.Id,
            Name = "Test Rota",
            Priority = ShiftPriority.Normal,
            Policy = SignupPolicy.Public,
            Period = period,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };
        _dbContext.Rotas.Add(rota);

        rota.EventSettings = es;
        rota.Team = team;

        return (es, rota);
    }
}
