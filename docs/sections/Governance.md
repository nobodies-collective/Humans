# Governance (Applications & Board Voting) — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Any authenticated user | View own governance status (tier, active applications); submit Colaborador/Asociado tier application |
| Board, Admin | View all role assignments; approve/reject tier applications; cast Board votes; finalize applications |
| Board only | Cast individual BoardVotes on applications |
| Admin only | Assign the Admin role to other users |

## Invariants

- `GovernanceController` requires `[Authorize]` at the controller level. The Roles action requires `RoleGroups.BoardOrAdmin`.
- `ApplicationController` requires `[Authorize]` at the controller level — accessible during onboarding.
- Application approve/reject actions require `RoleGroups.BoardOrAdmin`.
- Application entity is for Colaborador/Asociado tier applications ONLY — never for becoming a Volunteer.
- Application status follows: Submitted -> Approved/Rejected/Withdrawn. No other transitions.
- BoardVote records are TRANSIENT — deleted when the application is finalized (GDPR data minimization). Only `Application.DecisionNote` and `BoardMeetingDate` survive.
- Each Board member gets exactly one vote per application (unique constraint on ApplicationId + BoardMemberUserId).
- On approval, `Application.TermExpiresAt` is set to the next Dec 31 of an odd year >= 2 years from approval.
- MembershipTier on Profile is updated to match the approved tier; user is added to the corresponding system team.
- Admins can assign all roles; Board/HumanAdmin can assign all except Admin.
- RoleAssignment tracks temporal role memberships (ValidFrom/ValidTo).
- OnboardingReviewController gates: Consent clearing requires ConsentCoordinator/Board/Admin; signup rejection requires Board/Admin; Board voting detail requires Board.

## Triggers

- When an application is approved: MembershipTier is updated on Profile; user is added to Colaboradors or Asociados system team.
- When an application is approved/rejected: BoardVote records for that application are deleted.
- `TermRenewalReminderJob` sends reminders 90 days before Colaborador/Asociado term expiry.
- On term expiry without renewal: user reverts to Volunteer tier; removed from tier system team.

## Cross-Section Dependencies

- **Profiles**: MembershipTier lives on Profile. Approval updates the Profile entity.
- **Teams**: Tier approval/expiry adds/removes user from Colaboradors/Asociados system teams.
- **Onboarding**: Tier applications are a separate, optional path — never blocks Volunteer onboarding.
- **Legal & Consent**: Consent checks are reviewed in OnboardingReviewController alongside (but independent of) tier applications.
