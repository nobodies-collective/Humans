using AwesomeAssertions;
using Humans.Application.Interfaces.Containers;
using Humans.Application.Services.Containers;
using Humans.Application.Tests.Infrastructure;
using Humans.Domain.Entities;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Repositories.Containers;
using Humans.Testing;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Testing;
using NSubstitute;

namespace Humans.Application.Tests.Services.Containers;

public class ContainerPlacementServiceTests : IDisposable
{
    private readonly HumansDbContext _dbContext;
    private readonly FakeClock _clock;
    private readonly ContainerService _sut;
    private readonly Instant _startTime = Instant.FromUtc(2026, 4, 26, 10, 0, 0);

    public ContainerPlacementServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<HumansDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new HumansDbContext(dbOptions);
        _clock = new FakeClock(_startTime);
        var repo = new ContainerRepository(new TestDbContextFactory(dbOptions));
        _sut = new ContainerService(repo, Substitute.For<IContainerImageStorage>(), _clock);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Container> SeedContainerAsync(Guid? campSeasonId = null)
    {
        var container = new Container
        {
            Id = Guid.NewGuid(),
            CampSeasonId = campSeasonId,
            Year = 2026,
            Name = "Container A",
            CreatedAt = _startTime,
            UpdatedAt = _startTime,
        };
        _dbContext.Containers.Add(container);
        await _dbContext.SaveChangesAsync();
        return container;
    }

    [HumansFact]
    public async Task SavePlacementAsync_SetsLocationGeoJsonAndUpdatedAt()
    {
        var container = await SeedContainerAsync();
        var geoJson = """{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[]]},"properties":{"center_lng":-0.137,"center_lat":41.699,"rotation_degrees":0}}""";
        _clock.AdvanceSeconds(60);

        var result = await _sut.SavePlacementAsync(container.Id, geoJson);

        result.LocationGeoJson.Should().Be(geoJson);
        result.UpdatedAt.Should().Be(_clock.GetCurrentInstant());
    }

    [HumansFact]
    public async Task SavePlacementAsync_ThrowsWhenContainerNotFound()
    {
        var act = async () => await _sut.SavePlacementAsync(Guid.NewGuid(), "{}");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Container not found.");
    }

    [HumansFact]
    public async Task ClearPlacementAsync_SetsLocationGeoJsonToNull()
    {
        var container = await SeedContainerAsync();
        var geoJson = """{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[]]},"properties":{"center_lng":-0.137,"center_lat":41.699,"rotation_degrees":0}}""";
        await _sut.SavePlacementAsync(container.Id, geoJson);

        await _sut.ClearPlacementAsync(container.Id);

        var updated = await _sut.GetByIdAsync(container.Id);
        updated!.LocationGeoJson.Should().BeNull();
    }

    [HumansFact]
    public async Task ClearPlacementAsync_ThrowsWhenContainerNotFound()
    {
        var act = async () => await _sut.ClearPlacementAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Container not found.");
    }
}
