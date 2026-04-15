# City Planning — Section Invariants

## Concepts

- **City Planning** is an interactive map for camp barrio placement. Camp leads draw polygons to claim their barrio's physical footprint on the site.
- **CityPlanningSettings** is a per-year singleton controlling the placement phase (open/closed), site boundary (limit zone), and informational overlays (official zones).
- **CampPolygon** is a single polygon per CampSeason representing the camp's placed area.
- **CampPolygonHistory** is an append-only audit trail of polygon edits and restores.

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| Any authenticated human | View the map and all placed barrios |
| Camp lead (own camp, placement open) | Draw or edit their own camp's polygon |
| City-planning team member (team slug: city-planning) | Full admin access always (any polygon, settings, exports) |
| CampAdmin role | Full admin access always |

## Invariants

- Only one CampPolygon per CampSeason (unique constraint on CampSeasonId).
- CampPolygonHistory is append-only — edits and restores always create a new history entry.
- Camp leads can only edit their own camp's polygon when placement is open. City-planning team members and CampAdmin are exempt from the placement-open requirement.
- CityPlanningSettings row is auto-created per year from CampSettings.PublicYear.
- SignalR broadcasts polygon updates to all connected clients in real time.
- Limit zone and official zones are stored as GeoJSON on CityPlanningSettings; out-of-bounds and overlap detection is client-side.

## Negative Access Rules

- Regular humans **cannot** edit polygons for camps they do not lead.
- Camp leads **cannot** edit their polygon when placement is closed.
- Non-admin humans **cannot** access the admin panel (placement toggle, zone uploads, export).

## Triggers

- Saving a polygon creates a CampPolygonHistory entry with note "Saved".
- Restoring a historical version saves the current polygon state to history first (note: "Restored from {timestamp}"), then overwrites the polygon with the restored version.
- SignalR broadcasts `CampPolygonUpdated` to all connected clients after every save.

## Cross-Section Dependencies

- **Camps**: CampSeason is the anchor entity; CampLead determines who can edit which polygon.
- **Admin**: CampAdmin role grants full city-planning access.
- **Teams**: Membership in the city-planning team (slug: `city-planning`) grants admin access.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `CityPlanningService`
**Owned tables:** `city_planning_settings`, `camp_polygons`, `camp_polygon_histories`

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **`ICityPlanningRepository`** — owns `city_planning_settings`, `camp_polygons`, `camp_polygon_histories`
  - Aggregate-local navs kept: none (each of the three tables is its own aggregate root; `CampPolygon` and `CampPolygonHistory` are keyed by `CampSeasonId` but do not navigate between each other)
  - Cross-domain navs stripped: `CampPolygon.CampSeason` (Camps), `CampPolygon.LastModifiedByUser` (Users), `CampPolygonHistory.CampSeason` (Camps), `CampPolygonHistory.ModifiedByUser` (Users) — replace with raw FK ids; resolve display data via `ICampService` / `IUserService` at the service layer
  - Note: `camp_polygon_histories` is append-only per §12 — repository exposes `AddAsync` and `GetXxxAsync` but no `UpdateAsync`/`DeleteAsync`.

### Current violations

Observed in this section's service code as of 2026-04-15:

- **Cross-domain `.Include()` calls:** `CityPlanningService.cs:104` — `.Include(h => h.ModifiedByUser)` on `CampPolygonHistories` pulls `User` (Users domain) so history rows can show who edited them.
- **Cross-section direct DbContext reads:** None found. `CampSeasons` access already routes through `ICampService` (e.g. `GetCampSeasonDisplayDataForYearAsync`, `GetCampSeasonSoundZoneAsync`, `GetCampSeasonNameAsync`, `GetCampSeasonBriefsForYearAsync`, `GetCampLeadSeasonIdForYearAsync`, `IsUserCampLeadAsync`, `GetSettingsAsync`).
- **Inline `IMemoryCache` usage in service methods:** None found.
- **Cross-domain nav properties on this section's entities:**
  - `CampPolygon.CampSeason` → Camps
  - `CampPolygon.LastModifiedByUser` → Users
  - `CampPolygonHistory.CampSeason` → Camps
  - `CampPolygonHistory.ModifiedByUser` → Users
  - `CityPlanningSettings`: none

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- When modifying the history list path (`CityPlanningService.cs:103-115`), start moving off the cross-domain `.Include(h => h.ModifiedByUser)`: project to a DTO with `ModifiedByUserId` only, then resolve user display names in a second pass via `IUserService` (batch lookup). Do not add new `.Include()` calls that cross into Camps or Users.
- Keep all new `CampSeasons` / `Camps` / `Users` access behind `ICampService` / `IUserService`. Never reintroduce `_dbContext.CampSeasons` or `_dbContext.Users` reads into this service (see §6).
- Treat `CampPolygonHistory` as strictly append-only (§12): `CityPlanningService.cs:161` `Add` is the only permitted write. Never add `Update`/`Remove` on `_dbContext.CampPolygonHistories` — restores must write a new history row and overwrite the `CampPolygon`, which the current `RestoreAsync` path already does.
- When adding a new read method, return section-local DTOs (as `GetCampSeasonsWithoutCampPolygonAsync` already does with `CampSeasonSummaryDto`). Do not return `CampPolygon` / `CampPolygonHistory` entities with navs populated — it bakes cross-domain coupling into callers and will block the repository extraction.
