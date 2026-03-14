# Barrio Map Feature Design

## Business Context

City planning at Nowhere needs to know where each barrio intends to camp before the event. Today this is handled informally — leads email a map request, a city planner manually updates a layout. This feature replaces that workflow with a self-service interactive map where barrio leads place their own camp footprint on the festival site, and city planners have live visibility into the layout as it develops.

The system centers on a **placement phase** concept: CampAdmin opens and closes a window during which leads can edit their polygons. Outside the placement phase, the map is read-only. City planners have edit access at all times to make corrections.

## Scope

**This spec (Phase 2 of Barrios):**
- Interactive satellite map centered on the festival site
- Polygon drawing/editing for each barrio's camp footprint
- Live area display in square metres
- Admin-controlled placement phase (open/close)
- Full polygon version history with preview and restore
- Collaborative editing: all barrio leads + city planning team
- Real-time cursor presence via SignalR
- CampAdmin panel for uploading a GeoJSON limit zone (visual boundary overlay)

**Out of scope (future):**
- Server-side enforcement of the limit zone (currently visual only)
- Conflict detection between overlapping barrio polygons
- Export of the full site map as GeoJSON or image

**Prerequisites:** Phase 1 Barrios entities (`Barrio`, `BarrioSeason`, `BarrioLead`, `BarrioSettings`, `RoleNames.CampAdmin`) must exist before this feature is implemented.

## Data Model

### New Entities

#### BarrioPolygon

One record per barrio. Updated in-place when the polygon changes; history is separately tracked in `BarrioPolygonHistory`.

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | Unique (1:1 with Barrio) |
| GeoJson | string | GeoJSON Feature string. Stored as PostgreSQL `text`. |
| AreaSqm | double | Pre-calculated area in m². Computed client-side by Turf.js. |
| LastModifiedByUserId | Guid (FK → User) | Non-nullable. DeleteBehavior.Restrict. |
| LastModifiedAt | Instant | NodaTime. Updated on every save. |

**Navigation:** `Barrio`, `LastModifiedByUser`

#### BarrioPolygonHistory

Append-only log of every polygon version. Never updated or deleted. One entry written each time a polygon is saved or restored.

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | init. DeleteBehavior.Restrict. |
| GeoJson | string | Snapshot of the polygon at this point in time. init. |
| AreaSqm | double | Area at time of save. init. |
| ModifiedByUserId | Guid (FK → User) | Who saved. init. DeleteBehavior.Restrict. |
| ModifiedAt | Instant | NodaTime. init. |
| Note | string | Human-readable label. Default: `"Saved"`. Restore entries: `"Restored from 2026-03-10 14:32 UTC"`. init. |

**Navigation:** `Barrio`, `ModifiedByUser`

#### BarrioMapSettings

Single-row settings table. Seeded on migration; always has exactly one row with a fixed well-known Guid.

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK. Seeded: `00000000-0000-0000-0004-000000000001`. |
| IsPlacementOpen | bool | Whether the placement phase is currently open. Default: `false`. |
| OpenedAt | Instant? | When placement was last opened. Null if never opened. |
| ClosedAt | Instant? | When placement was last closed. |
| LimitZoneGeoJson | string? | GeoJSON FeatureCollection defining the visual boundary. Null until uploaded. |
| UpdatedAt | Instant | Last write timestamp. |

### Relationships

```
Barrio 1──0..1 BarrioPolygon
Barrio 1──∞    BarrioPolygonHistory (append-only)
```

### Storage

GeoJSON is stored as PostgreSQL `text` columns (not `jsonb`) — the system never queries inside the JSON structure, so jsonb's overhead is unnecessary.

## Authorization

### Roles

| Role | Map access |
|------|-----------|
| **CampAdmin** | Full access at all times. Can open/close placement, upload limit zone, restore any barrio's history. Can edit any polygon even when placement is closed. Full admin panel access. |
| **City planning team member** | Full admin panel access: can open/close placement, upload/delete limit zone. Can edit any barrio's polygon at all times (including after placement closes). |
| **Barrio lead (primary or co-lead)** | Can edit their own barrio's polygon when placement is open. Cannot edit other barrios. Read-only when closed. |
| **Active volunteer** | Read-only map view. |
| **Anonymous** | Not permitted — map requires authentication. |

**City planning team** is identified by a slug configured in `appsettings.json`:

```json
"BarrioMap": {
  "CityPlanningTeamSlug": "city-planning"
}
```

This is a manually-managed team (not auto-synced) whose members get map-wide edit access.

### Edit Permission Logic (`CanUserEditAsync`)

```
if user.IsInRole(CampAdmin) → always allowed
if user is member of city planning team → always allowed
if placement is closed → denied
if user is active lead of the target barrio → allowed
otherwise → denied
```

### Per-Barrio Ownership Check

Non-admin, non-city-planning leads can only save polygons for their own barrio. Attempting to save to a different barrio's polygon returns `403 Forbidden`.

## Workflows

### Placement Phase Lifecycle

```
Closed (default)
    │ CampAdmin or city planning team member opens placement
    ▼
Open
    │ CampAdmin or city planning team member closes placement
    ▼
Closed
```

When placement is open:
- Barrio leads see an "Edit" button on the map
- City planning team members can edit any polygon
- CampAdmin can always edit

When placement is closed:
- Map is read-only for barrio leads and regular volunteers
- CampAdmin and city planning team members retain edit access
- A banner indicates placement is closed

### Polygon Save Workflow

1. User draws or edits a polygon using maplibre-gl-draw
2. Turf.js calculates area in m² in real time (shown during editing)
3. User clicks "Save"
4. Client sends PUT `/BarrioMap/SavePolygon/{barrioId}` with GeoJSON + area
5. Server checks edit permission, updates `BarrioPolygon`, appends to `BarrioPolygonHistory`
6. Server broadcasts polygon update via SignalR to all connected clients
7. All connected users' maps update to show the new polygon

### Polygon History & Restore

- Each save creates an immutable `BarrioPolygonHistory` entry
- History panel shows: who, when, area, note — sorted newest first
- "Preview" shows the historical polygon overlaid on the current state (not persisted)
- "Restore" calls POST `/BarrioMap/RestorePolygonVersion/{historyId}`, which:
  1. Calls `SavePolygonAsync` with the historical GeoJSON
  2. Creates a new history entry with note `"Restored from {iso8601} UTC"`
  3. Broadcasts the restored polygon to all connected users

### Real-Time Cursor Presence

- On connecting to the SignalR hub, users receive a list of currently connected users
- As the user moves their mouse over the map, cursor coordinates are sent to the hub
- The hub relays cursor updates to all OTHER connected users (excluding sender)
- Each remote cursor is rendered as a colored marker with the user's display name
- On disconnect, the hub broadcasts `CursorLeft` with the user's connection ID so others remove the cursor

### Limit Zone

- CampAdmin uploads a GeoJSON file via the admin panel
- The limit zone is stored in `BarrioMapSettings.LimitZoneGeoJson`
- On the map, the limit zone is rendered as a semi-transparent overlay showing the allowed placement area
- Polygons placed outside the limit zone are visually highlighted (e.g., red outline) but not server-side rejected
- CampAdmin can delete the limit zone (sets to null)

## Routes

### Map Page

| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/BarrioMap` | Authenticated | Interactive map for all logged-in users |

### API (AJAX, anti-forgery validated)

| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/BarrioMap/GetPolygons` | Authenticated | All current barrio polygons (GeoJSON) |
| GET | `/BarrioMap/GetHistory/{barrioId}` | Authenticated | Polygon history for one barrio |
| PUT | `/BarrioMap/SavePolygon/{barrioId}` | Authenticated | Save/update polygon |
| POST | `/BarrioMap/RestorePolygonVersion/{historyId}` | Authenticated | Restore a historical version |

### Admin Panel

| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/BarrioMap/Admin` | CampAdmin or city planning team | Admin panel: placement toggle + limit zone |
| POST | `/BarrioMap/Admin/OpenPlacement` | CampAdmin or city planning team | Open the placement phase |
| POST | `/BarrioMap/Admin/ClosePlacement` | CampAdmin or city planning team | Close the placement phase |
| POST | `/BarrioMap/Admin/UploadLimitZone` | CampAdmin or city planning team | Upload GeoJSON limit zone file |
| POST | `/BarrioMap/Admin/DeleteLimitZone` | CampAdmin or city planning team | Remove the limit zone |

### SignalR Hub

| Endpoint | Purpose |
|----------|---------|
| `/hubs/barrio-map` | Real-time cursor presence + polygon broadcast |

## Technical Implementation

### Frontend Libraries (CDN)

All loaded from CDN. Added to `About.cshtml` with version and license:

| Library | Version | License | Purpose |
|---------|---------|---------|---------|
| MapLibre GL JS | 4.x | BSD-3 | Interactive map rendering |
| @maplibre/maplibre-gl-draw | 1.x | ISC | Polygon drawing/editing tool |
| Turf.js | 7.x | MIT | Client-side area calculation |
| @microsoft/signalr | (bundled) | MIT | Real-time WebSocket client |

**Satellite tiles:** ESRI World Imagery — `https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}` — free, no API key required.

**Map center:** `[-0.13717, 41.69964]`, zoom 17

### Content Security Policy

`CspNonceMiddleware` must be updated to add:
- `worker-src blob:` — required for MapLibre's web workers
- `https://server.arcgisonline.com` in `connect-src` — for ESRI tile fetching

### Service Layer

`IBarrioMapService` / `BarrioMapService` in Application/Infrastructure layers:

```
GetPolygonsAsync(year)           → all BarrioPolygon records for barrios in the given year
GetPolygonHistoryAsync(barrioId) → BarrioPolygonHistory ordered by ModifiedAt desc
SavePolygonAsync(barrioId, geoJson, areaSqm, userId, note = "Saved")
RestorePolygonVersionAsync(historyId, restoredByUserId)
CanUserEditAsync(userId, barrioId) → bool    // CampAdmin + city planning always true; leads only when placement open
IsUserMapAdminAsync(userId) → bool           // true for CampAdmin or city planning team member
GetOrCreateSettingsAsync()       → BarrioMapSettings (creates with defaults if missing)
OpenPlacementAsync(userId)
ClosePlacementAsync(userId)
UploadLimitZoneAsync(geoJson, userId)
DeleteLimitZoneAsync(userId)
```

`SavePolygonAsync` is the single write path used by both saves and restores. It:
1. Upserts `BarrioPolygon` (creates on first save, updates thereafter)
2. Appends to `BarrioPolygonHistory`

### SignalR Hub (`BarrioMapHub`)

- `[Authorize]` — only authenticated users can connect
- `UpdateCursor(lat, lng)` — client sends cursor position; hub relays to `Others`
- `PolygonUpdated(barrioId, geoJson, areaSqm)` — broadcast from `SavePolygonAsync` (via `IHubContext<BarrioMapHub>`)
- `OnDisconnectedAsync` — broadcasts `CursorLeft(connectionId)` to group

### UI Patterns

**Anti-forgery:** AJAX PUT/POST requests send the `RequestVerificationToken` header (read from the hidden `@Html.AntiForgeryToken()` input in the page). Both `SavePolygon` and `RestorePolygonVersion` are decorated with `[ValidateAntiForgeryToken]`.

**Own barrio highlight:** The `Index.cshtml` view receives `USER_BARRIO_ID` from the server. The user's own barrio polygon is rendered in a distinct color (e.g., `#00bfff`) while other barrios use the default color (e.g., `#ff6600`).

**History panel XSS safety:** The history list renders using data attributes + event listeners instead of inline `onclick` handlers with embedded GeoJSON strings.

**Save button disabled state:** The Save button is disabled until the user has drawn a polygon. For CampAdmin and city planning team members (who have `USER_BARRIO_ID = null`), a barrio selector is shown so they can choose which barrio to save to. *(Note: this selector is a follow-up UI concern; the core save/restore logic does not depend on it.)*

### EF Core Configuration

- `BarrioPolygonConfiguration` — unique index on `BarrioId`, `DeleteBehavior.Restrict` on both FKs (non-nullable Guids cannot use SetNull)
- `BarrioPolygonHistoryConfiguration` — index on `(BarrioId, ModifiedAt)`, `DeleteBehavior.Restrict` on both FKs
- `BarrioMapSettingsConfiguration` — seeds one row with fixed well-known Id `00000000-0000-0000-0004-000000000001`, `IsPlacementOpen = false`

### DI Registration

- `IBarrioMapService` registered as scoped in `InfrastructureServiceCollectionExtensions`
- `builder.Services.AddSignalR()` in `Program.cs`
- `app.MapHub<BarrioMapHub>("/hubs/barrio-map")` in `Program.cs`

## Related Features

| Feature | Relationship |
|---------|-------------|
| Barrios Phase 1 | **Prerequisite** — provides `Barrio`, `BarrioSeason`, `BarrioLead`, `RoleNames.CampAdmin` |
| Teams | City planning team is a standard `Team` — no special entity needed |
| Google Sync | Not involved — no Google resources provisioned for the map |
| GDPR | No personal data exported by map APIs. GeoJSON polygons are spatial data only. |
