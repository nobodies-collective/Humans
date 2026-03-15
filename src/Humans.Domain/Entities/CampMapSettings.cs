using NodaTime;

namespace Humans.Domain.Entities;

public class CampMapSettings
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The season year this row applies to. Unique.</summary>
    public int Year { get; init; }

    public bool IsPlacementOpen { get; set; }
    public Instant? OpenedAt { get; set; }
    public Instant? ClosedAt { get; set; }

    /// <summary>GeoJSON FeatureCollection defining the visual site boundary. Null until uploaded.</summary>
    public string? LimitZoneGeoJson { get; set; }

    public Instant UpdatedAt { get; set; }
}
