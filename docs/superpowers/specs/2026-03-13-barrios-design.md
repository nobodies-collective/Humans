# Barrios (Camps) Feature Design

## Business Context

Barrios are camps — the fundamental social unit at Nowhere. Camps form, evolve, and return year after year. They need to register with the organization, provide logistical data for city planning, and present themselves publicly to attract new members.

Today this is handled via a Google Form with ~36 questions. This feature replaces that form with an integrated system in Humans that supports registration, approval, public listing, and data feeds for downstream systems (website, city planning, barrio support, early entry allocation).

## Scope

**Phase 1 (this spec):**
- Camp registration with all form fields
- Season-based lifecycle (persistent camp identity, yearly registrations)
- CampAdmin approval workflow for new camps (returning camps auto-approve)
- Public camp listing (no auth) + individual camp profiles
- JSON API for website feed and city planning data
- Image uploads (up to 5 per camp)
- Name lock mechanism with configurable cutoff date

**Phase 2 (future):**
- Barrio support purchasing (Stripe store) — reads camp data for container storage, water, etc.
- Early entry pass allocation — reads camp size and needs to allocate passes
- Google Group integration for camp communication (if needed)

## Data Model

### Entities

#### Barrio (persistent identity)

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| Slug | string (256) | Unique, URL-friendly, stable across seasons |
| ContactEmail | string (256) | Primary contact email for all barrio matters |
| ContactPhone | string (64) | Mobile number with country code, for on-site use |
| WebOrSocialUrl | string? (512) | Website, Facebook, Instagram, or "none" |
| ContactMethod | string (512) | How to reach the barrio (for joining, performing, etc.) |
| IsSwissCamp | bool | The important question. Default false. |
| TimesAtNowhere | int | How many times the barrio has been to Nowhere (0 = new this year) |
| CreatedByUserId | Guid (FK → User) | User who registered the camp |
| CreatedAt | Instant | |
| UpdatedAt | Instant | |

**Navigation properties:** Seasons, Leads, HistoricalNames, Images

#### BarrioSeason (year-specific registration)

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | |
| Year | int | Event year |
| Name | string (256) | Camp name for this season (can change year-to-year) |
| NameLockDate | LocalDate? | Cutoff date for name changes (e.g., May 1). Set by CampAdmin. |
| NameLockedAt | Instant? | Stamped when name lock takes effect (for audit) |
| Status | BarrioSeasonStatus | See state machine below |
| BlurbLong | string (4000) | Full description for the barrios page. Markdown supported. |
| BlurbShort | string (1000) | Short personality description (~150 words). Markdown supported. |
| Languages | string (256) | Languages spoken in the barrio |
| AcceptingMembers | YesNoMaybe | Open for new members? |
| KidsWelcome | YesNoMaybe | Kids welcome as campers? |
| KidsVisiting | KidsVisitingPolicy | Kids friendly for visiting? |
| KidsAreaDescription | string? (2000) | Kids area/playground description (for kids-friendly map). Markdown supported. |
| HasPerformanceSpace | PerformanceSpaceStatus | Performance space available? |
| PerformanceTypes | string? (1000) | Types of performances accepted |
| Vibes | List\<BarrioVibe\> | Multiple vibes, stored as `jsonb` column in PostgreSQL. Filtering by vibe happens in-memory via IMemoryCache (not SQL). EF Core config: `HasColumnType("jsonb")`. |
| AdultPlayspace | AdultPlayspacePolicy | S+ playspace availability |
| MemberCount | int | Approximate number of campers |
| SpaceRequirement | SpaceSize? | Footprint in m² |
| SoundZone | SoundZone? | Sound zone preference |
| ContainerCount | int | Number of shipping containers in storage |
| ContainerNotes | string? (2000) | Additional container/portacabin info |
| ElectricalGrid | ElectricalGrid? | Which electrical grid to join |
| CreatedAt | Instant | |
| UpdatedAt | Instant | |

**Unique constraint:** (BarrioId, Year)

#### BarrioLead (join table: Barrio ↔ User)

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | |
| UserId | Guid (FK → User) | Must have an existing account in the system. Co-leads who don't have accounts yet must sign up first. |
| Role | BarrioLeadRole | Primary or CoLead |
| JoinedAt | Instant | |
| LeftAt | Instant? | Soft delete — null = active |

**Constraints:** Min 1, max 5 active leads per barrio. Exactly 1 Primary lead at all times.

#### BarrioHistoricalName

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | |
| Name | string (256) | |
| Source | BarrioNameSource | How this entry was created |
| CreatedAt | Instant | |

Historical names come from three sources:
1. **Previous season names** — derived automatically from older BarrioSeason.Name values (no BarrioHistoricalName entry needed — query previous seasons directly)
2. **Pre-system names** — manually entered in BarrioHistoricalName with `Source = Manual` (for names before the system existed)
3. **Mid-season name changes** — auto-logged to BarrioHistoricalName with `Source = NameChange` when a lead changes the season name

When displaying "also known as" on the public profile, combine previous season names (from BarrioSeason records) with BarrioHistoricalName entries, deduplicated by name (case-insensitive).

#### BarrioImage

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | |
| FileName | string (256) | Original filename |
| StoragePath | string (512) | Path on disk relative to wwwroot |
| ContentType | string (64) | MIME type |
| SortOrder | int | Display order |
| UploadedAt | Instant | |

**Constraints:** Max 5 images per barrio. Max 10MB each. JPEG/PNG/WebP only.
**Storage:** `wwwroot/uploads/barrios/{barrioId}/{guid}.{ext}`

### Enums

All stored as strings in PostgreSQL (following existing convention).

```
BarrioSeasonStatus: Pending, Approved, Active, Full, Inactive, Rejected, Withdrawn
BarrioLeadRole: Primary, CoLead
YesNoMaybe: Yes, No, Maybe
KidsVisitingPolicy: Yes, DaytimeOnly, No
PerformanceSpaceStatus: Yes, No, WorkingOnIt
AdultPlayspacePolicy: Yes, No, NightOnly
SoundZone: Blue, Green, Yellow, Orange, Red, Surprise
SpaceSize: Sqm150, Sqm300, Sqm450, Sqm600, Sqm800, Sqm1000, Sqm1200, Sqm1500, Sqm1800, Sqm2200, Sqm2800
ElectricalGrid: Yellow, Red, Norg, OwnSupply, Unknown
BarrioVibe: Adult, ChillOut, ElectronicMusic, Games, Queer, Sober, Lecture, LiveMusic, Wellness, Workshop
BarrioNameSource: Manual, NameChange
```

### Relationships

```
Barrio 1──∞ BarrioSeason (one per year)
Barrio 1──∞ BarrioLead (primary + co-leads, soft delete via LeftAt)
Barrio 1──∞ BarrioHistoricalName (append-only, pre-system names)
Barrio 1──∞ BarrioImage (up to 5, reorderable)
BarrioLead ∞──1 User (must have system account)
Barrio ∞──1 User (CreatedByUserId)
```

## Authorization

### Roles

| Role | Scope | Can do |
|------|-------|--------|
| **CampAdmin** | Global | Approve/reject new camp registrations. Edit any camp. Open/close seasons. Set public year. Override name lock. Full barrio admin. |
| **Primary Lead** | Per-barrio | Edit their barrio and season data. Add/remove co-leads (up to 4). Upload images. Opt in to new seasons. Transfer primary lead role. |
| **Co-Lead** | Per-barrio | Same edit access as primary lead. Cannot transfer primary role or delete the barrio. |
| **Authenticated user** | Global | View all camps. Register a new camp (becomes primary lead). |
| **Anonymous** | Global | View public camp listing for the configured public year. View individual camp profiles. |

### Authorization checks

Service-level checks (following Team pattern):
- `IsUserBarrioLeadAsync(userId, barrioId)` — is user an active lead (any role)?
- `IsUserPrimaryLeadAsync(userId, barrioId)` — is user the primary lead?
- `User.IsInRole("CampAdmin")` — global admin check
- Name lock: `IClock.GetCurrentInstant()` vs season name lock date. CampAdmin can override.

**CampAdmin** is a new role added to the existing ASP.NET Identity role system, seeded in migrations (like TeamsAdmin). Added as a constant in `RoleNames` class (alongside Admin, Board, TeamsAdmin, etc.).

## Workflows

### New Camp Registration

1. Authenticated user navigates to `/Barrios/Register`
2. Fills out registration form (all form fields from the Google Form)
3. System creates `Barrio` entity + `BarrioSeason` for the current registration year
4. User becomes Primary Lead (`BarrioLead` created)
5. Season status set to **Pending**
6. CampAdmin sees pending registration in admin dashboard
7. CampAdmin approves → status becomes **Active** (visible publicly)
8. CampAdmin can also reject → status becomes **Rejected**
9. Lead can withdraw → status becomes **Withdrawn**

### Returning Camp Season Opt-In

1. Lead views their barrio dashboard
2. CampAdmin has opened a new season (e.g., 2027)
3. Lead clicks "Join [Year] Season"
4. System copies most recent Active season's data into a new `BarrioSeason`
5. Season status set to **Active** immediately (auto-approved — camp was previously approved)
6. Lead edits copied data as needed (new name, updated blurb, new size, etc.)

### Season Status State Machine

```
                  ┌─────────┐
                  │ PENDING │ ← New camps only
                  └────┬────┘
                       │ CampAdmin approves
                       ▼
                  ┌─────────┐          ┌──────┐
                  │ ACTIVE  │ ◄──────► │ FULL │
                  └────┬────┘          └──┬───┘
                       │                  │
                       ▼                  ▼
                  ┌──────────┐
                  │ INACTIVE │
                  └──────────┘

From PENDING:  → ACTIVE (approved), → REJECTED, → WITHDRAWN
From ACTIVE:   → FULL (lead marks full), → INACTIVE (lead opts out)
From FULL:     → ACTIVE (lead re-opens), → INACTIVE (lead opts out)
REJECTED, WITHDRAWN, INACTIVE are terminal for that season.
```

Note: INACTIVE camps can opt in to a NEW season — they just can't reactivate the same season.

### Name Lock

- CampAdmin sets `NameLockDate` (a `LocalDate`) per season via admin dashboard (e.g., May 1)
- Before the cutoff: leads can change the season name freely
- After the cutoff: name field is locked. Only CampAdmin can override.
- When any name change occurs, the old name is auto-logged to `BarrioHistoricalName` with `Source = NameChange`
- `NameLockedAt` is stamped on the season record for audit when the lock first takes effect
- Service checks `IClock.GetCurrentInstant()` against `NameLockDate` (converted to the event timezone) to enforce

### Lead Management

- Primary lead can add co-leads by searching existing Users (max 5 total leads)
- Primary lead can remove co-leads (soft delete: `LeftAt` stamped)
- Primary lead can transfer primary role to a co-lead
- If primary lead leaves, they must transfer first (system enforces)
- Co-leads have full edit access to the barrio except: transfer primary, delete barrio

## Routes

### Public (no auth)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/Barrios` | Camp listing for public year. Filterable by vibes, sound zone, kids-friendly, etc. |
| GET | `/Barrios/{slug}` | Individual camp profile page |

### Authenticated

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/Barrios/Register` | New camp registration form |
| POST | `/Barrios/Register` | Submit new camp registration |
| GET | `/Barrios/{slug}/Edit` | Edit camp + current season |
| POST | `/Barrios/{slug}/Edit` | Save camp edits |
| POST | `/Barrios/{slug}/OptIn/{year}` | Opt in to a new season (copy forward) |
| GET | `/Barrios/{slug}/Season/{year}` | View/edit a specific season |
| POST | `/Barrios/{slug}/Leads/Add` | Add a co-lead |
| POST | `/Barrios/{slug}/Leads/Remove/{leadId}` | Remove a co-lead |
| POST | `/Barrios/{slug}/Images/Upload` | Upload images |
| POST | `/Barrios/{slug}/Images/Delete/{imageId}` | Delete an image |
| POST | `/Barrios/{slug}/Images/Reorder` | Reorder images |

### CampAdmin

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/Barrios/Admin` | Admin dashboard: pending approvals, season settings |
| POST | `/Barrios/Admin/Approve/{seasonId}` | Approve pending registration |
| POST | `/Barrios/Admin/Reject/{seasonId}` | Reject pending registration |
| POST | `/Barrios/Admin/OpenSeason/{year}` | Open a new season for registration |
| POST | `/Barrios/Admin/SetPublicYear/{year}` | Set which year the public listing shows |
| POST | `/Barrios/Admin/SetNameLockDate` | Configure name lock cutoff for a season |

### API (public, no auth)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/barrios/{year}` | JSON feed of camps for website |
| GET | `/api/barrios/{year}/placement` | JSON placement data for city planning |

## Technical Implementation

### Service Layer

New `IBarrioService` interface in Application layer, `BarrioService` implementation in Infrastructure.

Key methods:
- `CreateBarrioAsync(...)` — registration + first season + lead assignment
- `GetBarrioBySlugAsync(slug)` / `GetBarrioByIdAsync(id)`
- `GetBarriosForYearAsync(year)` — public listing
- `UpdateBarrioAsync(...)` / `UpdateSeasonAsync(...)`
- `OptInToSeasonAsync(barrioId, year)` — copy forward + auto-approve
- `ApproveSeasonAsync(seasonId)` / `RejectSeasonAsync(seasonId)`
- `AddLeadAsync(barrioId, userId, role)` / `RemoveLeadAsync(leadId)`
- `TransferPrimaryLeadAsync(barrioId, newLeadUserId)`
- `UploadImageAsync(barrioId, file)` / `DeleteImageAsync(imageId)`
- `IsUserBarrioLeadAsync(userId, barrioId)`
- `GetPlacementDataAsync(year)` — for city planning API

### Controllers

- `BarrioController` — public listing + authenticated camp management
- `BarrioAdminController` — CampAdmin operations
- `BarrioApiController` — JSON API endpoints (attribute routing: `[Route("api/barrios")]`)

### Caching

`IMemoryCache` for public listing and API responses. Cache key per year. Invalidated on any barrio/season mutation.

### Image Storage

Files stored at `wwwroot/uploads/barrios/{barrioId}/{guid}.{ext}`. Validated on upload: max 10MB, JPEG/PNG/WebP only. Served as static files via ASP.NET Core static file middleware.

**Note:** This is a new infrastructure pattern — the existing codebase stores profile pictures as `byte[]` in the database. Filesystem storage is more appropriate for multiple larger images. **Deployment requirement:** The `wwwroot/uploads/` directory must be mounted as a persistent Docker volume in the Coolify deployment so files survive container rebuilds. Add to the Docker Compose volume configuration.

### Slug Generation

Same pattern as Teams: `SlugHelper.GenerateSlug(name)` with conflict resolution (append `-2`, `-3`). Slug is set once on barrio creation and does NOT change when the season name changes (stable URLs).

### Audit Logging

New `AuditAction` entries (entity type strings: `"Barrio"`, `"BarrioSeason"`, `"BarrioLead"`):
- BarrioCreated, BarrioUpdated, BarrioDeleted
- SeasonCreated, SeasonApproved, SeasonRejected, SeasonWithdrawn, SeasonStatusChanged
- BarrioNameChanged (includes old + new name)
- LeadAdded, LeadRemoved, PrimaryLeadTransferred
- ImageUploaded, ImageDeleted

### Database

- EF Core entity configurations in `Configurations/` directory
- Migration for new tables: `barrios`, `barrio_seasons`, `barrio_leads`, `barrio_historical_names`, `barrio_images`
- Indexes: Barrio.Slug (unique), BarrioSeason (BarrioId, Year) unique, BarrioLead (BarrioId, UserId), BarrioSeason.Status
- CampAdmin role seeded in migration

### Global Settings

A `BarrioSettings` record (or app configuration) for:
- `PublicYear` (int) — which year the public listing shows
- `OpenSeasons` (List\<int\>) — which years are open for registration/opt-in

Stored in a database `barrio_settings` table (single-row, like existing `SyncServiceSettings` pattern) so CampAdmin can manage via the admin dashboard at runtime without redeployment.
