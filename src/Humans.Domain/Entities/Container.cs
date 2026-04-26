using NodaTime;

namespace Humans.Domain.Entities;

public class Container
{
    public Guid Id { get; init; }

    public Guid? CampSeasonId { get; init; }
    public CampSeason? CampSeason { get; set; } // declared but not read by Containers code (design-rules §15i)

    public int Year { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageStoragePath { get; set; }
    public string? ImageContentType { get; set; }
    public string? ImageFileName { get; set; }
    public int SortOrder { get; set; }
    public Instant CreatedAt { get; init; }
    public Instant UpdatedAt { get; set; }
}
