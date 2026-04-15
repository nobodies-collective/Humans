# Feedback — Section Invariants

## Concepts

- A **Feedback Report** is an in-app submission from a human — a bug report, feature request, or question. It captures the page URL, optional screenshot, and conversation thread between the reporter and admins.
- **Feedback status** tracks the lifecycle: Open, Acknowledged, Resolved, or WontFix.

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| Any authenticated human | Submit feedback (with optional screenshot). View and reply to their own feedback reports. Accessible even during onboarding (before becoming an active member) |
| FeedbackAdmin, Admin | View all feedback reports. Update status. Assign to humans or teams. Add admin notes. Send email responses to reporters. Link GitHub issues. Reply to any report |
| API (key auth) | Full CRUD on feedback reports via the REST API (no user session required) |

## Invariants

- Every feedback report is linked to the human who submitted it.
- Screenshots are validated for allowed file types (JPEG, PNG, WebP) before storage.
- Feedback status follows: Open then Acknowledged then Resolved or WontFix.
- Regular humans can only see their own feedback reports. FeedbackAdmin and Admin can see all reports.
- A report tracks whether it needs a reply (the reporter sent a message that the admin has not yet responded to).
- A report can optionally be assigned to a human and/or a team. Both assignments are independent and nullable.
- Assignment changes are audit-logged.

## Negative Access Rules

- Regular humans **cannot** view other humans' feedback reports.
- Regular humans **cannot** update feedback status, assign reports, add admin notes, link GitHub issues, or send admin responses.
- FeedbackAdmin **cannot** perform system administration tasks — their elevated access is scoped to feedback only.

## Triggers

- When an admin sends a response, an email is queued to the reporter via the email outbox.

## Cross-Section Dependencies

- **Admin**: GitHub issue linking connects feedback reports to the external issue tracker.
- **Email**: Response emails are queued through the email outbox system.
- **Onboarding**: Feedback submission is available during onboarding, before the human is an active member.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `FeedbackService`
**Owned tables:** `feedback_reports`, `feedback_messages`

> **Note:** `feedback_messages` (DbSet `FeedbackMessages`, entity `FeedbackMessage`, migration `20260324014417_FeedbackUpgrade`) is owned by Feedback but is **not yet listed in `design-rules.md` §8 Table Ownership Map**. The §8 map should be updated to add `feedback_messages` under the Feedback row the next time that doc is edited.

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **`IFeedbackRepository`** — owns `feedback_reports`, `feedback_messages`
  - Aggregate-local navs kept: `FeedbackReport.Messages` (collection of `FeedbackMessage`), `FeedbackMessage.FeedbackReport` (inverse). Both sides of the aggregate live in Feedback-owned tables, so `.Include(f => f.Messages)` is legal inside the repository.
  - Cross-domain navs stripped: `FeedbackReport.User`, `FeedbackReport.ResolvedByUser`, `FeedbackReport.AssignedToUser`, `FeedbackReport.AssignedToTeam`, `FeedbackMessage.SenderUser`. The repository must return only Feedback-owned columns (FK ids) and let the application layer hydrate human/team display data via `IUserService` / `ITeamService` projections.

Feedback is not a cached domain under §4 — reports are per-user and admin-triaged, not hot read paths. No `IFeedbackStore` or `CachingFeedbackService` decorator is planned. The existing nav-badge-count invalidation must move off `IMemoryCache` into a shared nav-badge cache abstraction (owned by whichever service ends up holding the badge counts) — see violations below.

### Current violations

Observed in this section's service code as of 2026-04-15 (`src/Humans.Infrastructure/Services/FeedbackService.cs`):

- **Cross-domain `.Include()` calls** (13 total — all read paths pull User/Team graphs instead of calling `IUserService` / `ITeamService`):
  - `FeedbackService.cs:119` `.Include(f => f.User)` — Users
  - `FeedbackService.cs:120` `.Include(f => f.ResolvedByUser)` — Users
  - `FeedbackService.cs:121` `.Include(f => f.AssignedToUser)` — Users
  - `FeedbackService.cs:122` `.Include(f => f.AssignedToTeam)` — Teams
  - `FeedbackService.cs:136` `.Include(f => f.User)` — Users
  - `FeedbackService.cs:137` `.Include(f => f.ResolvedByUser)` — Users
  - `FeedbackService.cs:138` `.Include(f => f.AssignedToUser)` — Users
  - `FeedbackService.cs:139` `.Include(f => f.AssignedToTeam)` — Teams
  - `FeedbackService.cs:227` `.Include(f => f.User)` — Users
  - `FeedbackService.cs:299` `.Include(m => m.SenderUser)` — Users
  - `FeedbackService.cs:311` `.Include(f => f.AssignedToUser)` — Users
  - `FeedbackService.cs:312` `.Include(f => f.AssignedToTeam)` — Teams
  - `FeedbackService.cs:386` `.Include(f => f.User)` — Users
  - Aggregate-local (legal under §6, retain through migration): `FeedbackService.cs:123` `.Include(f => f.Messages.OrderBy(...))`, `FeedbackService.cs:140` `.Include(f => f.Messages)`.
- **Cross-section direct DbContext reads:** None found. All 11 `_dbContext.*` hits are on `FeedbackReports` / `FeedbackMessages` (both Feedback-owned).
- **Inline `IMemoryCache` usage in service methods:** `FeedbackService` injects `IMemoryCache _cache` (`FeedbackService.cs:22`, `:48`) and calls the `InvalidateNavBadgeCounts()` extension at `:108`, `:207`, `:290`. Under §4/§5 a service may not touch `IMemoryCache` directly. The nav-badge count cache is a cross-cutting concern shared with `RoleAssignmentService`, `ApplicationDecisionService`, `OnboardingService`, and `ProfileService` — it needs its own owner (likely a small `INavBadgeCache` abstraction) and Feedback should depend on that interface instead of `IMemoryCache`.
- **Cross-domain nav properties on this section's entities:**
  - `FeedbackReport.User` / `FeedbackReport.UserId` → `AspNetUsers` (Users section)
  - `FeedbackReport.ResolvedByUser` / `FeedbackReport.ResolvedByUserId` → Users
  - `FeedbackReport.AssignedToUser` / `FeedbackReport.AssignedToUserId` → Users
  - `FeedbackReport.AssignedToTeam` / `FeedbackReport.AssignedToTeamId` → Teams section (`teams`)
  - `FeedbackMessage.SenderUser` / `FeedbackMessage.SenderUserId` → Users
  - Keep the FK ids; drop the reference nav properties as part of the migration. `FeedbackReport.Messages` / `FeedbackMessage.FeedbackReport` are aggregate-local and stay.

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- When adding a new read path, do **not** introduce additional `.Include(f => f.User | f.ResolvedByUser | f.AssignedToUser | f.AssignedToTeam)` calls (see `FeedbackService.cs:118-140`, `:226-227`, `:310-312`, `:385-386`). Select the FK ids and resolve display data via `IUserService` / `ITeamService` in the caller — this is the shape the repository will expose post-migration.
- Aggregate-local `.Include(f => f.Messages)` (`FeedbackService.cs:123`, `:140`) is fine to keep and to copy for new queries — `feedback_messages` is Feedback-owned.
- Do **not** add new direct `IMemoryCache` usage. The existing `_cache.InvalidateNavBadgeCounts()` calls at `FeedbackService.cs:108`, `:207`, `:290` are already §5 violations; new invalidation points should go through whatever nav-badge cache interface is introduced (coordinate with the owner of the other `InvalidateNavBadgeCounts` call sites).
- New tables that logically belong to Feedback must be added to `design-rules.md` §8 alongside `feedback_messages`; do not silently grow the section's footprint.
