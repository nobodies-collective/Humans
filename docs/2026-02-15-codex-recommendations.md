# Codex Recommendations (2026-02-15)

## Context
This review prioritized refactor/simplification opportunities that are materially easier to do before production and that reduce long-term operational and regression risk.

## Top 3 Recommendations

### 1) Split Monolithic Web Controllers Into Feature Slices

**Why this is top priority**
- Current controller breadth is a major change-risk multiplier.
- `AdminController` is effectively a subsystem (`36` actions, `1318` lines, `12` constructor dependencies).
- `ProfileController` is similarly broad (`13` actions, `787` lines, `12` constructor dependencies).
- Large controllers mix data access, workflow rules, view-model mapping, and orchestration.

**Evidence**
- `src/Humans.Web/Controllers/AdminController.cs`
- `src/Humans.Web/Controllers/ProfileController.cs`

**Recommended refactor**
- Split by feature area while keeping routes stable:
  - `AdminMembersController`
  - `AdminApplicationsController`
  - `AdminTeamsController`
  - `AdminRolesController`
  - `AdminLegalDocumentsController`
  - `ProfileCoreController`
  - `ProfileEmailsController`
  - `ProfilePrivacyController`
- Move business workflows and query composition out of controllers into dedicated command/query handlers (or application services).
- Keep controllers thin: auth + input validation + dispatch + response.

**Pre-production benefit**
- Lower blast radius per change.
- Faster testing and safer parallel development.
- Better enforceable boundaries before behavior hardens in production.

---

### 2) Centralize Membership/Consent State Into a Single Read Model

**Why this is high leverage**
- Membership status and consent logic are currently implemented in multiple places with overlapping but not identical query paths.
- This creates drift risk (different pages or filters showing different status for the same user).

**Evidence**
- Home/dashboard computes membership-related state directly:
  - `src/Humans.Web/Controllers/HomeController.cs`
- Consent area computes related state directly:
  - `src/Humans.Web/Controllers/ConsentController.cs`
- Canonical logic also exists in:
  - `src/Humans.Infrastructure/Services/MembershipCalculator.cs`
- Profile view-shaping is duplicated between:
  - `src/Humans.Web/Controllers/ProfileController.cs`
  - `src/Humans.Web/Controllers/HumanController.cs`

**Recommended refactor**
- Introduce a single membership read model/service (for example: `MembershipSnapshotService`) that returns:
  - Effective membership status
  - Required consent versions
  - Missing/expired consent summary
  - Volunteer membership flag
  - Onboarding blockers
- Have Home, Consent, Profile, Human, and claims-related checks consume this one source.
- Extract shared profile projection/mapping used by both profile controllers.

**Pre-production benefit**
- One canonical definition of status and consent obligations.
- Eliminates class of inconsistent-user-state bugs.
- Simplifies future rule changes (new consent policies, grace-period behavior, role interactions).

---

### 3) Decouple Google Sync Side Effects Using an Outbox-Driven Integration Flow

**Why this matters before launch**
- Domain operations currently invoke Google sync directly after DB writes.
- External side effects are partially enabled/add-only, and key recurring sync automation is intentionally paused.
- This is a reliability and reconciliation risk once real production volume and edge cases appear.

**Evidence**
- Direct post-save Google side effects in team workflows:
  - `src/Humans.Infrastructure/Services/TeamService.cs`
- Sync service is broad and handles many concerns in one class:
  - `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs`
- Removal paths are currently disabled (add-only behavior):
  - `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs`
- Automated reconciliation jobs are commented out:
  - `src/Humans.Web/Program.cs`

**Recommended refactor**
- Add outbox events for membership/resource changes.
- Process integration events in background workers with retries, idempotency keys, and dead-letter visibility.
- Split Google integration into focused components:
  - Group membership sync
  - Drive permission sync
  - Drift preview/reconciliation
  - Credential/client factory
- Keep core domain writes independent from immediate external API success.

**Pre-production benefit**
- Better failure isolation and recovery.
- Safer eventual consistency for Google state.
- Much easier to operate/debug once production incidents happen.

---

## Suggested Sequencing

1. Controller decomposition (Recommendation 1)
2. Membership/consent read-model unification (Recommendation 2)
3. Outbox-based Google integration refactor (Recommendation 3)

This order delivers immediate maintainability gains first, then consistency, then operational resilience.

