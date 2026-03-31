# Onboarding — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Unauthenticated user | Sign up via Google OAuth |
| Authenticated (pre-approval) user | Complete profile, sign legal documents, submit tier application (optional) |
| ConsentCoordinator | Clear or flag consent checks |
| VolunteerCoordinator | Read-only access to onboarding review queue |
| Board, Admin | All consent coordinator capabilities; reject signups; manage Board voting |

## Invariants

- Onboarding steps: (1) complete profile, (2) consent to all required legal documents, (3) consent check by Coordinator, (4) auto-approval as Volunteer.
- MembershipRequiredFilter blocks non-active-members from most of the app; exempt controllers: Home, Account, Application, Consent, Profile, Camp, Legal, Feedback, and controllers with their own role gates.
- Users with any admin/coordinator role bypass MembershipRequiredFilter entirely (`RoleChecks.BypassesMembershipRequirement`).
- `OnboardingReviewController` requires `RoleGroups.ReviewQueueAccess` (ConsentCoordinator, VolunteerCoordinator, Board, Admin) at the controller level.
- Consent clearing (ClearConsentCheck, FlagConsentCheck) requires `RoleGroups.ConsentCoordinatorBoardOrAdmin`.
- Signup rejection requires `RoleGroups.BoardOrAdmin`.
- Board voting actions require `RoleNames.Board` or `RoleGroups.BoardOrAdmin`.
- Volunteer onboarding is NEVER blocked by tier applications — they are separate, parallel paths.
- The ActiveMember claim is set by `RoleAssignmentClaimsTransformation` based on Volunteers system team membership.

## Triggers

- When profile is completed + all consents signed: `ConsentCheckStatus` = Pending.
- When consent check is cleared: user becomes active Volunteer; added to Volunteers system team; ActiveMember claim becomes available.
- When consent check is flagged: onboarding is blocked; Board/Admin must review.
- When signup is rejected: `RejectionReason`, `RejectedAt`, `RejectedByUserId` are set on Profile.

## Cross-Section Dependencies

- **Profiles**: Profile completion is step 1. ConsentCheckStatus and MembershipTier live on Profile.
- **Legal & Consent**: Consent to all required documents is step 2.
- **Teams**: Volunteer activation = addition to Volunteers system team.
- **Governance**: Tier applications are optional and independent of Volunteer onboarding.
