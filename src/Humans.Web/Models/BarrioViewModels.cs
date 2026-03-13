using Humans.Domain.Enums;

namespace Humans.Web.Models;

// Public listing
public class BarrioIndexViewModel
{
    public int Year { get; set; }
    public List<BarrioCardViewModel> Barrios { get; set; } = new();
    public BarrioFilterViewModel Filters { get; set; } = new();
}

public class BarrioCardViewModel
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BlurbShort { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public List<BarrioVibe> Vibes { get; set; } = new();
    public YesNoMaybe AcceptingMembers { get; set; }
    public YesNoMaybe KidsWelcome { get; set; }
    public SoundZone? SoundZone { get; set; }
    public BarrioSeasonStatus Status { get; set; }
    public int TimesAtNowhere { get; set; }
}

public class BarrioFilterViewModel
{
    public BarrioVibe? Vibe { get; set; }
    public SoundZone? SoundZone { get; set; }
    public bool? KidsFriendly { get; set; }
    public bool? AcceptingMembers { get; set; }
}

// Detail page
public class BarrioDetailViewModel
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactMethod { get; set; } = string.Empty;
    public string? WebOrSocialUrl { get; set; }
    public bool IsSwissCamp { get; set; }
    public int TimesAtNowhere { get; set; }
    public List<string> HistoricalNames { get; set; } = new();
    public List<string> ImageUrls { get; set; } = new();
    public List<BarrioLeadViewModel> Leads { get; set; } = new();
    public BarrioSeasonDetailViewModel? CurrentSeason { get; set; }
    public bool IsCurrentUserLead { get; set; }
    public bool IsCurrentUserPrimaryLead { get; set; }
    public bool IsCurrentUserCampAdmin { get; set; }
}

public class BarrioSeasonDetailViewModel
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public string Name { get; set; } = string.Empty;
    public BarrioSeasonStatus Status { get; set; }
    public string BlurbLong { get; set; } = string.Empty;
    public string BlurbShort { get; set; } = string.Empty;
    public string Languages { get; set; } = string.Empty;
    public YesNoMaybe AcceptingMembers { get; set; }
    public YesNoMaybe KidsWelcome { get; set; }
    public KidsVisitingPolicy KidsVisiting { get; set; }
    public string? KidsAreaDescription { get; set; }
    public PerformanceSpaceStatus HasPerformanceSpace { get; set; }
    public string? PerformanceTypes { get; set; }
    public List<BarrioVibe> Vibes { get; set; } = new();
    public AdultPlayspacePolicy AdultPlayspace { get; set; }
    public int MemberCount { get; set; }
    public SpaceSize? SpaceRequirement { get; set; }
    public SoundZone? SoundZone { get; set; }
    public int ContainerCount { get; set; }
    public string? ContainerNotes { get; set; }
    public ElectricalGrid? ElectricalGrid { get; set; }
    public bool IsNameLocked { get; set; }
}

public class BarrioLeadViewModel
{
    public Guid LeadId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public BarrioLeadRole Role { get; set; }
}

// Registration form
public class BarrioRegisterViewModel
{
    public string Name { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string? WebOrSocialUrl { get; set; }
    public string ContactMethod { get; set; } = string.Empty;
    public bool IsSwissCamp { get; set; }
    public int TimesAtNowhere { get; set; }
    public string? HistoricalNames { get; set; }
    public string BlurbLong { get; set; } = string.Empty;
    public string BlurbShort { get; set; } = string.Empty;
    public string Languages { get; set; } = string.Empty;
    public YesNoMaybe AcceptingMembers { get; set; }
    public YesNoMaybe KidsWelcome { get; set; }
    public KidsVisitingPolicy KidsVisiting { get; set; }
    public string? KidsAreaDescription { get; set; }
    public PerformanceSpaceStatus HasPerformanceSpace { get; set; }
    public string? PerformanceTypes { get; set; }
    public List<BarrioVibe> Vibes { get; set; } = new();
    public AdultPlayspacePolicy AdultPlayspace { get; set; }
    public int MemberCount { get; set; }
    public SpaceSize? SpaceRequirement { get; set; }
    public SoundZone? SoundZone { get; set; }
    public int ContainerCount { get; set; }
    public string? ContainerNotes { get; set; }
    public ElectricalGrid? ElectricalGrid { get; set; }
}

// Edit form
public class BarrioEditViewModel : BarrioRegisterViewModel
{
    public Guid BarrioId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public int Year { get; set; }
    public bool IsNameLocked { get; set; }
    public List<BarrioLeadViewModel> Leads { get; set; } = new();
    public List<BarrioImageViewModel> Images { get; set; } = new();
}

public class BarrioImageViewModel
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

// Admin dashboard
public class BarrioAdminViewModel
{
    public List<BarrioCardViewModel> PendingBarrios { get; set; } = new();
    public int PublicYear { get; set; }
    public List<int> OpenSeasons { get; set; } = new();
    public int TotalBarrios { get; set; }
    public int ActiveBarrios { get; set; }
}
