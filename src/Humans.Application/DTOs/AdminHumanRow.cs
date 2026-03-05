namespace Humans.Application.DTOs;

public record AdminHumanRow(
    Guid UserId,
    string Email,
    string DisplayName,
    string? ProfilePictureUrl,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool HasProfile,
    bool IsApproved,
    string MembershipStatus);
