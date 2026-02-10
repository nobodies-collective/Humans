using System.ComponentModel.DataAnnotations;
using NodaTime;

namespace Humans.Web.Models;

public class LegalDocumentListViewModel
{
    public List<LegalDocumentListItemViewModel> Documents { get; set; } = [];
    public List<TeamSelectItem> Teams { get; set; } = [];
    public Guid? FilterTeamId { get; set; }
}

public class LegalDocumentListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; }
    public int GracePeriodDays { get; set; }
    public string? CurrentVersion { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int VersionCount { get; set; }
}

public class LegalDocumentEditViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid TeamId { get; set; }

    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;

    [Range(1, 365)]
    public int GracePeriodDays { get; set; } = 7;

    [MaxLength(512)]
    public string? GitHubFolderPath { get; set; }

    public List<TeamSelectItem> Teams { get; set; } = [];

    // Read-only display fields for edit
    public string? CurrentVersion { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int VersionCount { get; set; }
}

public class TeamSelectItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
