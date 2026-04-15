# Onboarding — Section Invariants

## Concepts

- **Onboarding** is the process a new human goes through to become an active Volunteer: sign up via Google OAuth, complete their profile, consent to all required legal documents, and pass a profile review by a Consent Coordinator. The last two steps can happen in any order.
- The **Membership Gate** restricts most of the application to active Volunteers. Humans still onboarding are limited to their profile, consent, feedback, legal documents, camps (public), and the home dashboard. All admin and coordinator roles bypass this gate entirely.
- **Profileless accounts** are authenticated users with no Profile record (e.g., ticket holders, newsletter subscribers created by imports). They are redirected to the Guest dashboard instead of the Home dashboard and see a reduced nav: Guest Dashboard, Camps, Teams (public), Legal. They can create a profile to enter the standard onboarding flow.
- The **Profile Review** (consent check) and **Legal Document Signing** are independent, parallel tracks. A Consent Coordinator can clear the profile review before or after legal documents are signed. Admission to the Volunteers team only happens when both are complete.

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| Unauthenticated visitor | Sign up via Google OAuth (or magic link) |
| Authenticated human (pre-approval) | Complete profile, sign legal documents, submit a tier application (optional), submit feedback |
| ConsentCoordinator | Clear or flag consent checks in the onboarding review queue |
| VolunteerCoordinator | Read-only access to the onboarding review queue (cannot clear/flag or reject) |
| Board, Admin | All ConsentCoordinator capabilities. Reject signups. Manage Board voting on tier applications |

## Invariants

- Onboarding steps: (1) complete profile, (2a) consent to all required global legal documents, (2b) profile review by a Consent Coordinator — these two can happen in any order, (3) auto-approval as Volunteer when both 2a and 2b are complete.
- Volunteer onboarding is never blocked by tier applications — they are separate, parallel paths.
- The ActiveMember status is derived from membership in the Volunteers system team.
- All admin and coordinator roles bypass the membership gate entirely — they can access the full application regardless of membership status.
- OAuth login checks verified UserEmails, unverified UserEmails, and User.Email before creating a new account — preventing duplicate accounts when the same email exists on another user in any form.

## Negative Access Rules

- VolunteerCoordinator **cannot** clear or flag consent checks, and **cannot** reject signups. They have read-only access to the review queue.
- ConsentCoordinator **cannot** reject signups. Rejection requires Board or Admin.
- Regular humans still onboarding **cannot** access most of the application (teams, shifts, budget, tickets, governance, etc.) until they become active Volunteers.
- Profileless accounts **cannot** access the Home dashboard, City Planning, Budget, Shifts, Governance, or any member-only features. They are redirected to the Guest dashboard.

## Triggers

- When a human completes their profile and signs all required documents: their consent check status becomes Pending.
- When a profile review is cleared: the profile is approved. If all legal documents are also signed, the human is added to the Volunteers system team and a welcome email is sent. If documents are still pending, admission happens automatically when the last document is signed.
- When a legal document is signed: the system checks if the profile is also approved. If both conditions are met, the human is added to the Volunteers team.
- When a consent check is flagged: onboarding is blocked. Board or Admin must review.
- When a signup is rejected: the rejection reason and timestamp are recorded on the profile. The human is notified.

## Cross-Section Dependencies

- **Profiles**: Profile completion is step 1. Consent check status and membership tier live on the profile.
- **Legal & Consent**: Consent to all required global documents is step 2.
- **Teams**: Volunteer activation adds the human to the Volunteers system team.
- **Governance**: Tier applications are optional and independent of Volunteer onboarding.
- **Feedback**: Feedback submission is available during onboarding, before the human is active.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `OnboardingService`
**Owned tables:** None — OnboardingService is an orchestrator that coordinates Profiles, Legal, and Teams services.

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **None.** Onboarding is a pure orchestrator and owns no tables. It composes `IProfileService`, `IConsentService`, `ILegalDocumentService`, `ITeamService` (Volunteers system-team membership), `IRoleAssignmentService`, and any other services needed for the full onboarding flow. The target is a slim service class in `Humans.Application/Services/OnboardingService.cs` with only cross-service interface dependencies, no `HumansDbContext`, no repositories of its own.
- **No caching decorator** — Onboarding has no cached data of its own. Each called service owns its own caching.

### Current violations

Observed in this section's service code as of 2026-04-15 (`src/Humans.Infrastructure/Services/OnboardingService.cs`, 29 `_dbContext` reads, 11 cross-domain `.Include()` calls):

- **Cross-domain `.Include()` calls (all Includes here are cross-domain since section owns no entities):**
  - **Profiles** — `Profile → User` navigation at lines 59, 92, 162, 312 (4 calls). Should go through `IProfileService` / `IUserService`.
  - **Users/Identity** — `User → Profile` navigation at lines 389, 448, 501 (3 calls). Should go through `IProfileService.GetByUserIdAsync` + `IUserService`.
  - **Governance** — `Application → User` at lines 116, 151 and `Application → BoardVotes` at lines 117, 153 (4 calls). Should go through `IApplicationDecisionService` / a governance read facade.
- **Cross-section direct DbContext reads (all DbContext reads here are cross-section since section owns no tables):**
  - **Profiles** (7): `_dbContext.Profiles` at lines 58, 91, 161, 220, 311, 539, 588 — replace with `IProfileService` calls (review queue fetch, per-user profile lookup, approved-profile stats).
  - **Users/Identity** (9): `_dbContext.Users` at lines 134, 388, 447, 500, 602, 624, 644; `_dbContext.UserEmails` at lines 651, 652 — replace with `IUserService` / `IUserEmailService`. The delete-user flow (lines 644-652) should be a single `IUserService.DeleteAsync` call.
  - **Governance** (12): `_dbContext.Applications` at lines 65, 100, 115, 150, 198, 595, 606, 610; `_dbContext.BoardVotes` at lines 259, 265, 274, 287 — replace with `IApplicationDecisionService` / `IBoardVoteService`. The board-vote recording flow (lines 259-287) is an entire Governance workflow that should not live in Onboarding at all.
  - **Auth** (1): `_dbContext.RoleAssignments` at line 124 — replace with `IRoleAssignmentService.GetUsersInRoleAsync(BoardRole)`.
- **Inline `IMemoryCache` usage in service methods:** 28 `_cache.*` invalidation calls (lines 186-188, 192, 241-243, 248, 300, 335-337, 341, 408-410, 414, 467-468, 516, 520, 551-553, 667-669). All target caches owned by other sections: `InvalidateUserProfile` / `UpdateProfile` (Profiles), `InvalidateNavBadgeCounts` / `InvalidateNotificationMeters` (cross-section nav cache), `InvalidateVotingBadge` (Governance), `InvalidateActiveTeams` / `InvalidateProfiles` (Teams/Profiles). Every one of these should disappear — the owning service's caching decorator invalidates its own cache when Onboarding calls its write method.
- **Cross-domain nav properties on this section's entities:** N/A — Onboarding owns no entities.

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- **Onboarding is the highest-value migration target** and the proof-of-concept for §9: because it owns no tables, the end state is unambiguous — zero `_dbContext` references, zero `_cache` references, constructor takes only `I*Service` dependencies. Every DbContext read or `.Include()` you delete here is a net architectural win with no "but we own this table" escape hatch.
- When you touch any method in `OnboardingService.cs`, replace its `_dbContext.Profiles` / `_dbContext.Users` / `_dbContext.Applications` / `_dbContext.BoardVotes` / `_dbContext.RoleAssignments` reads with calls to `IProfileService` / `IUserService` / `IApplicationDecisionService` / `IRoleAssignmentService`. If the needed method doesn't exist yet, add it to the owning service — do not work around it by keeping the direct read.
- Delete every `_cache.*` call you pass through. Cache invalidation belongs to the owning service's caching decorator, not to Onboarding. If removing a cache call breaks a UI freshness expectation, that's a bug in the owning service's write path and should be fixed there.
- The board-vote recording flow (`RecordBoardVoteAsync`, lines ~259-305) is misplaced — it's pure Governance logic that happens to be called from an onboarding-adjacent UI. Move it wholesale into `ApplicationDecisionService` / a Governance service and have Onboarding call the interface, rather than porting its DbContext reads one by one.
