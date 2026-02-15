# Consolidated Pre-Production Plan (2026-02-15)

This plan synthesizes all 9 recommendations across Claude, Gemini, and Codex, prioritized for maximum risk reduction in one day.

## Target Outcome By End Of Day
- Data constraints in place (or migration-ready) before production data.
- One canonical membership status path (including `IsApproved`) used across the app.
- Google sync reliability improved with a safe async boundary.
- Controller decomposition started with at least one high-value slice merged.
- `Program.cs` modularized if time remains.

## Phase 0 (30 min): Branch + Safety Baseline
1. Create working branch and checkpoint current tests.
2. Run `dotnet restore`, `dotnet build`, `dotnet test`.
3. Record baseline failures and current warning set.
4. Confirm migration state with `dotnet ef migrations list`.

Done criteria:
- Baseline captured in notes.
- No ambiguity about pre-change health.

## Phase 1 (90 min): Pre-Prod Data Integrity (Must Do)
This is the most time-sensitive work.

1. Add DB constraints/migrations:
- `google_resources`: enforce exactly one of `TeamId` or `UserId` is set.
- `role_assignments`: enforce `ValidTo > ValidFrom` when `ValidTo` exists.
2. Keep migrations explicit and reversible.
3. Enforce role-window overlap prevention in application logic (service/controller validation) instead of DB exclusion constraints.
4. Add lightweight integration tests validating constraint behavior and overlap validation behavior.

Files likely touched:
- `src/Humans.Infrastructure/Data/Configurations/GoogleResourceConfiguration.cs`
- `src/Humans.Infrastructure/Data/Configurations/RoleAssignmentConfiguration.cs`
- `src/Humans.Infrastructure/Migrations/*`

Done criteria:
- Migration applies cleanly on empty DB.
- Constraint tests pass.
- Role overlap prevention is covered by application-layer validation tests.
- This advances Claude #2 while intentionally deferring DB-level non-overlap enforcement.

## Phase 2 (2 hours): Canonical Membership Lifecycle (Must Do)
Unify logic and remove drift.

1. Create a single membership read/service model (for example, `MembershipSnapshotService`).
2. Make `IsApproved` an explicit gate for `Active` status in the canonical path.
3. Update these consumers to use it:
- `HomeController`
- `ConsentController` (for status/pending state)
- `ProfileController`
- `HumanController`
- `RoleAssignmentClaimsTransformation` (or keep claims-only role projection but avoid status duplication)
4. Keep `SystemTeamSyncJob` eligibility aligned with the same canonical rule set.

Files likely touched:
- `src/Humans.Infrastructure/Services/MembershipCalculator.cs`
- `src/Humans.Web/Controllers/HomeController.cs`
- `src/Humans.Web/Controllers/ConsentController.cs`
- `src/Humans.Web/Controllers/ProfileController.cs`
- `src/Humans.Web/Controllers/HumanController.cs`
- `src/Humans.Web/Authorization/RoleAssignmentClaimsTransformation.cs`

Done criteria:
- One source of truth for effective membership state.
- No duplicated status logic left in controllers.
- This closes the core of Gemini #3 and Codex #2.

## Phase 3 (2.5 hours): Controller Decomposition (Must Start, Partial Completion Acceptable)
Full decomposition is large; today do the highest-risk slice.

1. Extract Admin legal docs flow out of `AdminController` first (a good vertical slice with clear boundaries).
2. Add application service layer interface(s) for extracted behavior.
3. Keep routes unchanged.
4. Add tests around extracted service behavior.

Initial slice suggestion:
- `AdminLegalDocumentsController` + `IAdminLegalDocumentService`
- Move actions from current `AdminController` legal docs section.

Done criteria:
- At least one feature slice removed from `AdminController`.
- Controller is thinner and service-tested.
- This starts Claude #1 and Codex #1 in a safe, shippable increment.

## Phase 4 (2 hours): Google Sync Reliability Boundary (Must Start)
Do not attempt the full platform rewrite today. Ship the reliability foundation.

1. Introduce integration event/outbox table for team membership/resource-change events.
2. Change `TeamService` to enqueue events instead of direct sync calls in-request.
3. Add Hangfire worker to process events with retry + idempotency key.
4. Keep existing sync service behavior, but move invocation to worker boundary.

Files likely touched:
- `src/Humans.Infrastructure/Services/TeamService.cs`
- New outbox entity/configuration/migration
- New worker/job class in `src/Humans.Infrastructure/Jobs/`
- DI and schedule wiring in `src/Humans.Web/Program.cs`

Done criteria:
- DB commit no longer depends on external Google API success path.
- Retryable async processing exists.
- This starts Claude #3 and Codex #3 with real risk reduction today.

## Phase 5 (60-90 min, If Time): Program + Infra Modularization
1. Move service registration into `AddInfrastructure(...)`.
2. Move recurring jobs into `UseRecurringJobs(...)`.
3. Leave behavior unchanged.

Done criteria:
- Smaller `Program.cs`.
- Easier ongoing edits.
- Covers Gemini #2.

## What To Defer (Not Today Unless Ahead Of Schedule)
1. Full profile picture storage redesign (`byte[]` to external storage/table).
2. Full Admin/Profile decomposition in one pass.
3. Full Google sync service split into 3-4 dedicated services.

These are valuable, but not the best "today" ROI versus constraints + membership + outbox foundation.

## Suggested Commit Cadence Today
1. `feat(db): add pre-prod integrity constraints and migration`
2. `refactor(membership): centralize membership snapshot and approval gate`
3. `refactor(admin): extract legal docs feature slice service/controller`
4. `feat(sync): add outbox-backed google sync dispatcher`
5. `refactor(startup): move infra and recurring jobs to extensions` (optional)

## Definition Of Done For Today
1. All new migrations apply and rollback locally.
2. `dotnet build` is clean.
3. Critical tests pass.
4. New tests added for:
- membership status consistency,
- constraint enforcement,
- outbox dispatch behavior.
5. No route changes or user-facing regressions in admin/profile legal-doc flows.
