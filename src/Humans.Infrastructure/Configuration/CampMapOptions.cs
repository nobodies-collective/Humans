namespace Humans.Infrastructure.Configuration;

public class CampMapOptions
{
    public const string SectionName = "CampMap";

    /// <summary>
    /// Slug of the Team that has full map admin access (city planning team).
    /// Members of this team can always edit polygons and access the admin panel.
    /// </summary>
    public string CityPlanningTeamSlug { get; set; } = string.Empty;
}
