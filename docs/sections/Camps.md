# Camps — Section Invariants

## Concepts

- A **Camp** (also called "Barrio") is a themed community camp. Each camp has a unique URL slug, one or more leads, and optional images.
- A **Camp Season** is a per-year registration for a camp, containing the year-specific name, description, community info, and placement details.
- A **Camp Lead** is a human responsible for managing a camp. Leads have a role: Primary or CoLead.
- **Camp Settings** is a singleton controlling which year is public (shown in the directory) and which seasons accept new registrations.

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| Anyone (including anonymous) | Browse the camps directory, view camp details and season details |
| Any authenticated human | Register a new camp (which creates a new season in Pending status) |
| Camp lead | Edit their camp's details, manage season registrations, manage co-leads, upload/manage images, manage historical names |
| CampAdmin, Admin | All camp lead capabilities on all camps. Approve/reject season registrations. Manage camp settings (public year, open seasons, name lock dates). View withdrawn and rejected seasons. Export camp data |
| Admin | Delete camps |

## Invariants

- Each camp has a unique slug used for URL routing.
- Camp season status follows: Pending then Active, Full, Rejected, or Withdrawn. Only CampAdmin can approve or reject a season.
- Only camp leads or CampAdmin can edit a camp.
- Camp images are stored on disk; metadata and display order are tracked per camp.
- Historical names are recorded when a camp is renamed.
- Camp settings control which year is shown publicly and which seasons accept registrations.

## Negative Access Rules

- Regular humans **cannot** edit camps they do not lead.
- Camp leads **cannot** approve or reject season registrations — that requires CampAdmin or Admin.
- CampAdmin **cannot** delete camps. Only Admin can delete a camp.
- Anonymous visitors **cannot** register camps or edit any camp data.

## Triggers

- When a camp is registered, its initial season is created with Pending status.
- Season approval or rejection is performed by CampAdmin.

## Cross-Section Dependencies

- **Profiles**: Camp leads are linked to humans. Lead assignment requires a valid human account.
- **Admin**: Camp settings management is restricted to CampAdmin and Admin.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `CampService`, `CampContactService`
**Owned tables:** `camps`, `camp_seasons`, `camp_leads`, `camp_images`, `camp_historical_names`, `camp_settings`

**Incoming violations (other services querying Camp-owned tables):**
- `ProfileService` queries `CampLeads` directly
- `CityPlanningService` queries `CampSeasons` directly

These are tracked in the Profiles and City Planning section docs.

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **`ICampRepository`** — owns `camps`, `camp_seasons`, `camp_leads`, `camp_images`, `camp_historical_names`, `camp_settings`
  - Aggregate-local navs kept: `Camp.Seasons`, `Camp.Leads`, `Camp.HistoricalNames`, `Camp.Images`, `CampSeason.Camp`, `CampLead.Camp`, `CampImage.Camp`, `CampHistoricalName.Camp`
  - Cross-domain navs stripped: `Camp.CreatedByUser` (keep `CreatedByUserId`), `CampLead.User` (keep `UserId`), `CampSeason.ReviewedByUser` (keep `ReviewedByUserId`)
  - Exposes within-section join method `GetCampLeadSeasonIdForYearAsync(userId, year)` instead of the raw `CampLeads.Join(CampSeasons, ...)` currently inline in `CampService`
- **`CachingCampService`** decorator (Scrutor `.Decorate<>`) — owns `CacheKeys.CampSeasonsByYear`, `CacheKeys.CampSettings`, and the `CampContactRateLimit` reserve/invalidate. Removes `IMemoryCache` from `CampService` and `CampContactService`.
- `CampContactService` has no persistent state of its own — no repository needed. Its audit-log writes go through `IAuditLogService` already.

### Current violations

Observed in this section's service code as of 2026-04-15:

- **Cross-domain `.Include()` calls:** None found. All `.Include()` calls in `CampService.cs` (lines 132–147, 276–291, 326–327, 344–346, 366, 974, 986) are aggregate-local (`Camp.Seasons`, `Camp.Leads`, `Camp.Images`, `Camp.HistoricalNames`, `CampSeason.Camp`).
- **Cross-section direct DbContext reads:** None found. All 57 `_dbContext.*` hits in `CampService.cs` target Camp-owned DbSets (`Camps`, `CampSeasons`, `CampLeads`, `CampImages`, `CampHistoricalNames`, `CampSettings`).
- **Within-section cross-service direct DbContext reads:** None found. `CampContactService` does not inject `HumansDbContext` at all.
- **Inline `IMemoryCache` usage in service methods:**
  - `CampService.cs:272` — `_cache.GetOrCreateAsync(CacheKeys.CampSeasonsByYear(year), ...)` inside `GetCampsForYearAsync`
  - `CampService.cs:353` — `_cache.GetOrCreateAsync(CacheKeys.CampSettings, ...)` inside `GetSettingsAsync`
  - `CampService.cs:1127`, `:1137`, `:1147` — `_cache.InvalidateCampSettings()` inside settings mutators
  - `CampService.cs:1233` — `_cache.InvalidateCampSeasonsByYear(year)`
  - `CampContactService.cs:43` — `_cache.TryReserveAsync(rateLimitKey, ...)` for per-user per-camp rate limit
  - `CampContactService.cs:71` — `_cache.InvalidateCampContactRateLimit(senderUserId, campId)` on send failure
- **Cross-domain nav properties on this section's entities:**
  - `Camp.CreatedByUser` (→ `User`) in `Camp.cs:19`
  - `CampLead.User` (→ `User`) in `CampLead.cs:14`
  - `CampSeason.ReviewedByUser` (→ `User?`) in `CampSeason.cs:49`

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- **Do not add new cross-domain navs or `.Include()` calls.** If you need a `User` from a `CampLead`/`Camp`/`CampSeason`, call `IUserService` / `IProfileService` with the existing `*UserId` foreign key — do not extend `Camp.CreatedByUser`, `CampLead.User`, or `CampSeason.ReviewedByUser` usage.
- **Do not add new `_cache.*` call sites in `CampService` or `CampContactService`.** Route new caching needs through a thin method the future `CachingCampService` decorator can wrap. If you must invalidate an existing key, keep the call at a single well-named private method so the decorator lift is mechanical.
- **Keep new queries on Camp-owned tables inside `CampService`.** If another section (Profiles, City Planning, etc.) needs camp data, add a method to `ICampService` returning a Camp-section DTO — never let the caller reach into `DbContext.Camps*`.
- **When adding new season/lead/image/historical-name queries**, prefer `_dbContext.CampSeasons.Where(...)` style over new `.Join()` chains; the existing `CampLeads.Join(CampSeasons, ...)` at `CampService.cs:997` is the kind of shape the future `ICampRepository` should encapsulate behind a named method.
