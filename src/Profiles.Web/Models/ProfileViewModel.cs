using System.ComponentModel.DataAnnotations;

namespace Profiles.Web.Models;

public class ProfileViewModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "Date of Birth")]
    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }

    [Phone]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Address Line 1")]
    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [Display(Name = "Address Line 2")]
    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [Display(Name = "Postal Code")]
    [StringLength(20)]
    public string? PostalCode { get; set; }

    [Display(Name = "Country")]
    [StringLength(2)]
    public string? CountryCode { get; set; }

    [StringLength(1000)]
    [DataType(DataType.MultilineText)]
    public string? Bio { get; set; }

    public string MembershipStatus { get; set; } = "None";
    public bool HasPendingConsents { get; set; }
    public int PendingConsentCount { get; set; }
}
