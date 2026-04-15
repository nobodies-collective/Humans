# Governance (Applications & Board Voting) — Section Invariants

## Concepts

- **Volunteer** is the standard membership tier. Nearly all humans are Volunteers. Becoming a Volunteer happens through the onboarding process — not through the application/voting workflow described here.
- **Colaborador** is an active contributor with project and event responsibilities. Requires an application and Board vote. 2-year term.
- **Asociado** is a voting member with governance rights (assemblies, elections). Requires an application and Board vote. 2-year term. A human must first be an approved Colaborador before applying for Asociado.
- **Application** is a formal request to become a Colaborador or Asociado. Never used for becoming a Volunteer.
- **Board Vote** is an individual Board member's vote on a tier application. Board votes are transient working data — they are deleted when the application is finalized, and only the collective decision note and meeting date are retained (GDPR data minimization).
- **Role Assignment** is a temporal assignment of a governance or admin role to a human, with valid-from and valid-to dates.
- **Term** — Colaborador and Asociado memberships have synchronized 2-year terms expiring on December 31 of odd years (2027, 2029, 2031...).

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| Any authenticated human | View own governance status (tier, active applications). Submit a Colaborador or Asociado application |
| Board | View all pending applications and role assignments. Cast individual votes on applications. View Board voting detail |
| Board, Admin | Approve or reject tier applications with a decision note and meeting date. Manage role assignments (all roles except Admin) |
| Admin | Assign the Admin role. All Board capabilities |

## Invariants

- Application status follows: Submitted then Approved, Rejected, or Withdrawn. No other transitions.
- Each Board member gets exactly one vote per application.
- On approval, the term expiry is set to the next December 31 of an odd year that is at least 2 years from the approval date.
- On approval, the human's membership tier is updated and they are added to the corresponding system team (Colaboradors or Asociados).
- On finalization (approval or rejection), all individual Board vote records for that application are deleted. Only the collective decision note and Board meeting date survive.
- Admin can assign all roles. Board and HumanAdmin can assign all roles except Admin.
- Role assignments track temporal membership with valid-from and optional valid-to dates.
- Volunteer onboarding is never blocked by tier applications — they are separate, parallel paths.

## Negative Access Rules

- Regular humans **cannot** view other humans' applications, cast Board votes, or manage role assignments.
- Board **cannot** assign the Admin role.
- HumanAdmin **cannot** assign the Admin role.
- Humans who already have a pending (Submitted) application for a tier cannot submit another for the same tier until the first is resolved.

## Triggers

- When an application is approved: the human's tier is updated on their profile, and they are added to the Colaboradors or Asociados system team.
- When an application is approved or rejected: all Board vote records for that application are deleted.
- A renewal reminder is sent 90 days before term expiry.
- On term expiry without renewal: the human reverts to Volunteer tier and is removed from the tier system team.

## Cross-Section Dependencies

- **Profiles**: Membership tier lives on the profile. Approval updates the profile.
- **Teams**: Tier approval or expiry adds or removes the human from Colaboradors/Asociados system teams.
- **Onboarding**: Tier applications are a separate, optional path — never blocks Volunteer onboarding.
- **Legal & Consent**: Consent checks are reviewed alongside (but independently of) tier applications.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `ApplicationDecisionService`
**Owned tables:** `applications`, `application_state_histories`, `board_votes`

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **`IApplicationRepository`** — owns `applications`, `application_state_histories`, `board_votes`
  - Aggregate-local navs kept: `Application.StateHistory`, `Application.BoardVotes`, `ApplicationStateHistory.Application` (back-ref), `BoardVote.Application` (back-ref)
  - Cross-domain navs stripped: `Application.User → Application.UserId`, `Application.ReviewedByUser → Application.ReviewedByUserId`, `ApplicationStateHistory.ChangedByUser → ApplicationStateHistory.ChangedByUserId`, `BoardVote.BoardMemberUser → BoardVote.BoardMemberUserId`
  - Note: `application_state_histories` is append-only per §12 — repository exposes `AddAsync` and `GetXxxAsync` but no `UpdateAsync`/`DeleteAsync`.

### Current violations

Observed in this section's service code as of 2026-04-15:

- **Cross-domain `.Include()` calls:**
  - `ApplicationDecisionService.cs:57–58` — `.Include(a => a.User).ThenInclude(u => u.Profile)` in `ApproveAsync` (navigates Governance → Users → Profiles — two domain boundaries in one chain)
  - `ApplicationDecisionService.cs:161` — `.Include(a => a.User)` in `RejectAsync` (navigates to Users domain)
  - `ApplicationDecisionService.cs:251` — `.Include(a => a.ReviewedByUser)` in `GetUserApplicationDetailAsync` (navigates to Users domain)
  - `ApplicationDecisionService.cs:253` — `.ThenInclude(h => h.ChangedByUser)` on `StateHistory` in `GetUserApplicationDetailAsync` (navigates to Users domain)
  - `ApplicationDecisionService.cs:345` — `.Include(a => a.User)` in `GetFilteredApplicationsAsync` (navigates to Users domain)
  - `ApplicationDecisionService.cs:377` — `.Include(a => a.User)` in `GetApplicationDetailAsync` (navigates to Users domain)
  - `ApplicationDecisionService.cs:378` — `.Include(a => a.ReviewedByUser)` in `GetApplicationDetailAsync` (navigates to Users domain)
  - `ApplicationDecisionService.cs:380` — `.ThenInclude(h => h.ChangedByUser)` on `StateHistory` in `GetApplicationDetailAsync` (navigates to Users domain)
  - (`.Include(a => a.BoardVotes)` at lines 59, 162 and `.Include(a => a.StateHistory)` at lines 60, 163, 252, 320, 379 are aggregate-local — kept, not violations.)
- **Cross-section direct DbContext reads:** None found — verified via `reforge dbset-usage Humans.Infrastructure.Services.ApplicationDecisionService` — all 11 DbContext reads are against owned tables (`Applications`, `BoardVotes`).
- **Inline `IMemoryCache` usage in service methods:**
  - `ApplicationDecisionService.cs:101` — `_cache.InvalidateNavBadgeCounts()` in `ApproveAsync`
  - `ApplicationDecisionService.cs:102` — `_cache.InvalidateNotificationMeters()` in `ApproveAsync`
  - `ApplicationDecisionService.cs:103` — `_cache.InvalidateUserProfile(application.UserId)` in `ApproveAsync`
  - `ApplicationDecisionService.cs:105` — `_cache.InvalidateVotingBadge(id)` in `ApproveAsync`
  - `ApplicationDecisionService.cs:192` — `_cache.InvalidateNavBadgeCounts()` in `RejectAsync`
  - `ApplicationDecisionService.cs:193` — `_cache.InvalidateNotificationMeters()` in `RejectAsync`
  - `ApplicationDecisionService.cs:195` — `_cache.InvalidateVotingBadge(id)` in `RejectAsync`
  - `ApplicationDecisionService.cs:288` — `_cache.InvalidateNavBadgeCounts()` in `SubmitAsync`
  - `ApplicationDecisionService.cs:289` — `_cache.InvalidateNotificationMeters()` in `SubmitAsync`
  - `ApplicationDecisionService.cs:331` — `_cache.InvalidateNavBadgeCounts()` in `WithdrawAsync`
  - `ApplicationDecisionService.cs:332` — `_cache.InvalidateNotificationMeters()` in `WithdrawAsync`
- **Cross-domain nav properties on this section's entities:**
  - `Application.User → Application.UserId` (Users is a separate domain) — `Application.cs:30`
  - `Application.ReviewedByUser → Application.ReviewedByUserId` (Users is a separate domain) — `Application.cs:95`
  - `ApplicationStateHistory.ChangedByUser → ApplicationStateHistory.ChangedByUserId` (Users is a separate domain) — `ApplicationStateHistory.cs:44`
  - `BoardVote.BoardMemberUser → BoardVote.BoardMemberUserId` (Users is a separate domain) — `BoardVote.cs:36`

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- When an `ApplicationDecisionService` method needs User or Profile data, keep the `.Include(a => a.User[.Profile])` / `.Include(a => a.ReviewedByUser)` / `.ThenInclude(h => h.ChangedByUser)` chains at lines 57–58, 161, 251, 253, 345, 377–378, 380 confined to their current methods — do not copy them into new methods. New methods should return the `Application` entity (with its FKs) and let the caller fetch User/Profile via `IUserService` / `IProfileService`.
- The existing `_cache.Invalidate*` calls at lines 101–105, 192–195, 288–289, 331–332 will be replaced by a `CachingApplicationDecisionService` decorator + `IApplicationStore` per §5. Do not add new `_cache.*` invalidation calls inline — if a new mutation needs invalidation, raise it in review so the decorator pattern can be applied consistently.
- Do not add new cross-domain navigation properties to `Application`, `ApplicationStateHistory`, or `BoardVote`. New relationships to other domains should be stored as FKs (GUIDs) only, to match the target `IApplicationRepository` shape.
- When `ApplicationDecisionService` is eventually moved to `Humans.Application/Services/`, its `HumansDbContext` dependency is replaced by `IApplicationRepository` + `IApplicationStore`. Any new method added today should keep its EF logic tight and isolated so the future extraction is mechanical.

### Known incoming violations (other sections reading Governance tables)

Per reforge analysis of ProfileService, `_dbContext.Applications` is read from multiple ProfileService methods. These are violations of §2c owned by the Profiles section to fix — noted here so future touch-and-clean on the Profiles side knows to call `IApplicationDecisionService` instead. See `docs/sections/Profiles.md` for details.
