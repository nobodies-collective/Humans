using Humans.Domain.Enums;

namespace Humans.Application.Interfaces.Users;

/// <summary>
/// Consolidated storage mutation for profile-side onboarding state carried by
/// <see cref="UserInfo"/>. Callers own workflow policy and audit text;
/// <c>IUserService</c> owns the profile row write.
/// </summary>
public sealed record UserProfileOnboardingCommand(
    UserProfileOnboardingMutation Mutation,
    Guid? ActorUserId = null,
    ConsentCheckStatus? ConsentCheckStatus = null,
    string? Notes = null,
    string? RejectionReason = null,
    bool? Suspended = null);

public enum UserProfileOnboardingMutation
{
    RecordConsentCheck,
    RejectSignup,
    ApproveVolunteer,
    SetSuspension,
    SetConsentCheckPending,
}
