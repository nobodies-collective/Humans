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
| ContactEmail | string (256) | Camp's public contact email (not a lead's personal email) |
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
| Vibes | List\<BarrioVibe\> | Multiple vibes, stored as `jsonb` column in PostgreSQL with `JsonStringEnumConverter` so values are string names (consistent with string enum convention). Filtering by vibe happens in-memory via IMemoryCache (not SQL). EF Core config: `HasColumnType("jsonb")`. |
| AdultPlayspace | AdultPlayspacePolicy | S+ playspace availability |
| MemberCount | int | Approximate number of campers |
| SpaceRequirement | SpaceSize? | Footprint in m² |
| SoundZone | SoundZone? | Sound zone preference |
| ContainerCount | int | Number of shipping containers in storage |
| ContainerNotes | string? (2000) | Additional container/portacabin info |
| ElectricalGrid | ElectricalGrid? | Which electrical grid to join |
| ReviewedByUserId | Guid? (FK → User) | CampAdmin who approved/rejected (null if auto-approved) |
| ReviewNotes | string? (2000) | CampAdmin notes on approval/rejection |
| ResolvedAt | Instant? | When the season was approved/rejected |
| CreatedAt | Instant | |
| UpdatedAt | Instant | |

**Unique constraint:** (BarrioId, Year)

#### BarrioLead (join table: Barrio ↔ User)

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | |
| UserId | Guid (FK → User) | Must have an existing account and be an active volunteer. Co-leads who don't have accounts yet must sign up first. |
| Role | BarrioLeadRole | Primary or CoLead |
| JoinedAt | Instant | |
| LeftAt | Instant? | Soft delete — null = active |

**Constraints:** Min 1, max 5 active leads per barrio. Exactly 1 Primary lead at all times. A user can only be Primary lead of one barrio per season.

**Index:** `(BarrioId, UserId) WHERE left_at IS NULL` — filtered unique index on active leads only (allows re-adding former leads).

#### BarrioHistoricalName

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK, init |
| BarrioId | Guid (FK → Barrio) | |
| Name | string (256) | |
| Year | int? | Optional year association (for pre-system entries) |
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

All stored as strings in PostgreSQL (following existing convention). Explicit integer values assigned to prevent accidental reordering.

```
BarrioSeasonStatus: Pending = 0, Active = 1, Full = 2, Inactive = 3, Rejected = 4, Withdrawn = 5
BarrioLeadRole: Primary = 0, CoLead = 1
YesNoMaybe: Yes = 0, No = 1, Maybe = 2
KidsVisitingPolicy: Yes = 0, DaytimeOnly = 1, No = 2
PerformanceSpaceStatus: Yes = 0, No = 1, WorkingOnIt = 2
AdultPlayspacePolicy: Yes = 0, No = 1, NightOnly = 2
SoundZone: Blue = 0, Green = 1, Yellow = 2, Orange = 3, Red = 4, Surprise = 5
SpaceSize: Sqm150 = 0, Sqm300 = 1, Sqm450 = 2, Sqm600 = 3, Sqm800 = 4, Sqm1000 = 5, Sqm1200 = 6, Sqm1500 = 7, Sqm1800 = 8, Sqm2200 = 9, Sqm2800 = 10
ElectricalGrid: Yellow = 0, Red = 1, Norg = 2, OwnSupply = 3, Unknown = 4
BarrioVibe: Adult = 0, ChillOut = 1, ElectronicMusic = 2, Games = 3, Queer = 4, Sober = 5, Lecture = 6, LiveMusic = 7, Wellness = 8, Workshop = 9
BarrioNameSource: Manual = 0, NameChange = 1
```

### Relationships

```
Barrio 1──∞ BarrioSeason (one per year)
Barrio 1──∞ BarrioLead (primary + co-leads, soft delete via LeftAt)
Barrio 1──∞ BarrioHistoricalName (append-only)
Barrio 1──∞ BarrioImage (up to 5, reorderable)
BarrioLead ∞──1 User (must have system account, must be active volunteer)
BarrioSeason ∞──1 User (ReviewedByUserId, nullable)
Barrio ∞──1 User (CreatedByUserId)
```

## Authorization

### Roles

| Role | Scope | Can do |
|------|-------|--------|
| **Admin** | Global | Delete a barrio permanently (big red button). Full system admin — all CampAdmin permissions implicitly. |
| **CampAdmin** | Global | Approve/reject new camp registrations. Edit any camp. Open/close seasons. Set public year. Override name lock. Full barrio admin. |
| **Primary Lead** | Per-barrio | Edit their barrio and season data. Add/remove co-leads (up to 4). Upload images. Opt in to new seasons. Transfer primary lead role. |
| **Co-Lead** | Per-barrio | Same edit access as primary lead. Cannot transfer primary role. |
| **Active volunteer** | Global | View all camps. Register a new camp (becomes primary lead). |
| **Anonymous** | Global | View public camp listing for the configured public year. View individual camp profiles. |

**Registration requires active volunteer status.** Users must have completed volunteer onboarding (profile, consent) before they can register a camp.

### Authorization checks

Service-level checks (following Team pattern):
- `IsUserBarrioLeadAsync(userId, barrioId)` — is user an active lead (any role)?
- `IsUserPrimaryLeadAsync(userId, barrioId)` — is user the primary lead?
- `User.IsInRole(RoleNames.CampAdmin)` — global CampAdmin check
- `User.IsInRole(RoleNames.Admin)` — full site admin (for barrio deletion)
- Name lock: `IClock.GetCurrentInstant()` vs `NameLockDate`. CampAdmin can override.

**CampAdmin** is a new role following the existing `RoleAssignment` pattern (not raw ASP.NET Identity roles). Added as a constant in `RoleNames` class (alongside Admin, Board, TeamsAdmin, etc.). Projected into claims via `RoleAssignmentClaimsTransformation`. Seeded in migrations.

**MembershipRequiredFilter integration:**
- `BarrioController` public actions (`Index`, `Details`) use `[AllowAnonymous]`
- `BarrioController` authenticated actions (Register, Edit, etc.) require active volunteer status via the existing `MembershipRequiredFilter` — no exemption needed since only active volunteers should access these
- `CampAdmin` must be added to the `MembershipRequiredFilter` role bypass list (following the TeamsAdmin pattern)

### Field Visibility Matrix

Camp data is public advertising — most fields are visible to anyone. Lead personal details are protected.

| Field | Anonymous | Authenticated | Leads | CampAdmin |
|-------|-----------|---------------|-------|-----------|
| Camp name, slug | Yes | Yes | Yes | Yes |
| BlurbLong, BlurbShort | Yes | Yes | Yes | Yes |
| Images | Yes | Yes | Yes | Yes |
| Vibes, SoundZone | Yes | Yes | Yes | Yes |
| Languages | Yes | Yes | Yes | Yes |
| AcceptingMembers, ContactMethod | Yes | Yes | Yes | Yes |
| KidsWelcome, KidsVisiting, KidsArea | Yes | Yes | Yes | Yes |
| HasPerformanceSpace, PerformanceTypes | Yes | Yes | Yes | Yes |
| AdultPlayspace | Yes | Yes | Yes | Yes |
| ContactEmail (camp's public email) | Yes | Yes | Yes | Yes |
| WebOrSocialUrl | Yes | Yes | Yes | Yes |
| MemberCount, SpaceRequirement | Yes | Yes | Yes | Yes |
| ContainerCount, ContainerNotes | Yes | Yes | Yes | Yes |
| ElectricalGrid | Yes | Yes | Yes | Yes |
| ContactPhone (on-site mobile) | No | No | Yes | Yes |
| Lead names / user links | No | Yes | Yes | Yes |
| Lead personal email addresses | No | No | Yes | Yes |
| ReviewNotes, ReviewedBy | No | No | No | Yes |
| IsSwissCamp | Yes | Yes | Yes | Yes |
| TimesAtNowhere | Yes | Yes | Yes | Yes |
| HistoricalNames | Yes | Yes | Yes | Yes |

## Workflows

### New Camp Registration

1. Active volunteer navigates to `/Barrios/Register`
2. Fills out registration form (all form fields from the Google Form)
3. System creates `Barrio` entity + `BarrioSeason` for the current registration year
4. User becomes Primary Lead (`BarrioLead` created)
5. Season status set to **Pending**
6. CampAdmin sees pending registration in admin dashboard
7. CampAdmin approves → status becomes **Active**, `ReviewedByUserId`/`ResolvedAt` stamped (visible publicly)
8. CampAdmin can also reject → status becomes **Rejected**, `ReviewNotes` records reason
9. Lead can withdraw → status becomes **Withdrawn**

**Duplicate prevention:** A barrio can only have one season per year (enforced by unique constraint). A user can only be primary lead of one barrio at a time per season (enforced in service layer).

### Returning Camp Season Opt-In

1. Lead views their barrio dashboard
2. CampAdmin has opened a new season (e.g., 2027)
3. Lead clicks "Join [Year] Season"
4. System copies most recent season's data into a new `BarrioSeason`
5. **Auto-approval rule:** If the barrio has any prior season that was NOT Rejected, the new season is auto-approved → status: **Active** immediately. Otherwise, status: **Pending** (requires CampAdmin review).
6. Lead edits copied data as needed (new name, updated blurb, new size, etc.)

### Season Status State Machine

```
                  ┌─────────┐
                  │ PENDING │ ← New camps + previously-rejected camps
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
- Service checks `IClock.GetCurrentInstant()` against `NameLockDate` (converted to `Instant` at start-of-day UTC) to enforce

### Lead Management

- Any lead can add co-leads by searching existing Users who are active volunteers (max 5 total leads)
- Primary lead can remove co-leads (soft delete: `LeftAt` stamped)
- Primary lead can transfer primary role to a co-lead
- If primary lead leaves, they must transfer first (system enforces)
- Co-leads have full edit access to the barrio except: transfer primary role

### Barrio Deletion

- **Admin only** (full site admin, not CampAdmin) can permanently delete a barrio
- This is a destructive action with a confirmation step ("big red button")
- Deletes the barrio and all related data (seasons, leads, images, historical names)
- Audit logged as `BarrioDeleted`

## Routes

### Public (no auth)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/Barrios` | Camp listing for public year. Filterable by vibes, sound zone, kids-friendly, etc. |
| GET | `/Barrios/{slug}` | Individual camp profile page |

### Authenticated (active volunteer)

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
| POST | `/Barrios/{slug}/Leads/TransferPrimary` | Transfer primary lead role to a co-lead |
| POST | `/Barrios/{slug}/Images/Upload` | Upload images |
| POST | `/Barrios/{slug}/Images/Delete/{imageId}` | Delete an image |
| POST | `/Barrios/{slug}/Images/Reorder` | Reorder images |

### CampAdmin

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/Barrios/Admin` | Admin dashboard: pending approvals, season settings |
| POST | `/Barrios/Admin/Approve/{seasonId}` | Approve pending registration |
| POST | `/Barrios/Admin/Reject/{seasonId}` | Reject pending registration (with notes) |
| POST | `/Barrios/Admin/OpenSeason/{year}` | Open a new season for registration |
| POST | `/Barrios/Admin/SetPublicYear/{year}` | Set which year the public listing shows |
| POST | `/Barrios/Admin/SetNameLockDate` | Configure name lock cutoff for a season |

### Admin (full site admin)

| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/Barrios/Admin/Delete/{barrioId}` | Permanently delete a barrio (with confirmation) |

### API (public, no auth)

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/barrios/{year}` | JSON feed of camps for website (public fields only — see visibility matrix) |
| GET | `/api/barrios/{year}/placement` | JSON placement data for city planning (public — camp data is advertising) |

**API response fields:**
- **Website feed** (`/api/barrios/{year}`): name, slug, blurbShort, imageUrls, vibes, soundZone, languages, acceptingMembers, contactMethod, contactEmail, kidsWelcome, kidsVisiting, hasPerformanceSpace, adultPlayspace, webOrSocialUrl, timesAtNowhere, historicalNames
- **Placement feed** (`/api/barrios/{year}/placement`): name, slug, memberCount, spaceRequirement, soundZone, containerCount, containerNotes, electricalGrid

**Reserved slugs:** `register`, `admin` — prevented during barrio creation to avoid route collisions.

## Technical Implementation

### Service Layer

New `IBarrioService` interface in Application layer, `BarrioService` implementation in Infrastructure.

Key methods:
- `CreateBarrioAsync(...)` — registration + first season + lead assignment. Validates user is active volunteer and not already primary lead of another barrio for the same season.
- `GetBarrioBySlugAsync(slug)` / `GetBarrioByIdAsync(id)`
- `GetBarriosForYearAsync(year)` — public listing
- `UpdateBarrioAsync(...)` / `UpdateSeasonAsync(...)`
- `OptInToSeasonAsync(barrioId, year)` — copy forward + auto-approve (if any prior non-rejected season exists)
- `ApproveSeasonAsync(seasonId, reviewedByUserId, notes?)` / `RejectSeasonAsync(seasonId, reviewedByUserId, notes)`
- `AddLeadAsync(barrioId, userId, role)` / `RemoveLeadAsync(leadId)`
- `TransferPrimaryLeadAsync(barrioId, newLeadUserId)`
- `DeleteBarrioAsync(barrioId)` — Admin only, permanent deletion
- `UploadImageAsync(barrioId, file)` / `DeleteImageAsync(imageId)`
- `IsUserBarrioLeadAsync(userId, barrioId)`
- `GetPlacementDataAsync(year)` — for city planning API

### Controllers

- `BarrioController` — public listing (`[AllowAnonymous]`) + authenticated camp management
- `BarrioAdminController` — CampAdmin + Admin operations
- `BarrioApiController` — JSON API endpoints (attribute routing: `[Route("api/barrios")]`)

### Caching

`IMemoryCache` for public listing and API responses. Cache key per year. Invalidated on any barrio/season mutation.

### Image Storage

Files stored at `wwwroot/uploads/barrios/{barrioId}/{guid}.{ext}`. Validated on upload: max 10MB, JPEG/PNG/WebP only. Served as static files via ASP.NET Core static file middleware.

**Note:** This is a new infrastructure pattern — the existing codebase stores profile pictures as `byte[]` in the database. Filesystem storage is more appropriate for multiple larger images (up to 5 x 10MB). **Deployment requirement:** The `wwwroot/uploads/` directory must be mounted as a persistent Docker volume in the Coolify deployment so files survive container rebuilds. Add to the Docker Compose volume configuration.

### Slug Generation

Extract the private `GenerateSlug` method from `TeamService` into a shared `SlugHelper` utility class (or duplicate in `BarrioService`). Same logic: lowercase, replace non-alphanumerics with hyphens, deduplicate hyphens, append `-2`/`-3` for uniqueness. Block reserved slugs (`register`, `admin`). Slug is set once on barrio creation and does NOT change when the season name changes (stable URLs).

### Audit Logging

New `AuditAction` entries (entity type strings: `"Barrio"`, `"BarrioSeason"`, `"BarrioLead"`):
- BarrioCreated, BarrioUpdated, BarrioDeleted
- SeasonCreated, SeasonApproved, SeasonRejected, SeasonWithdrawn, SeasonStatusChanged
- BarrioNameChanged (includes old + new name)
- LeadAdded, LeadRemoved, PrimaryLeadTransferred
- ImageUploaded, ImageDeleted

### Database

- EF Core entity configurations in `Configurations/` directory
- Migration for new tables: `barrios`, `barrio_seasons`, `barrio_leads`, `barrio_historical_names`, `barrio_images`, `barrio_settings`
- Indexes: Barrio.Slug (unique), BarrioSeason (BarrioId, Year) unique, BarrioLead (BarrioId, UserId) filtered `WHERE left_at IS NULL`, BarrioSeason.Status
- CampAdmin role seeded in migration via `RoleAssignment` pattern

### Global Settings

A `BarrioSettings` entity for:
- `PublicYear` (int) — which year the public listing shows
- `OpenSeasons` (List\<int\>) — which years are open for registration/opt-in

Stored in a database `barrio_settings` table (single-row) so CampAdmin can manage via the admin dashboard at runtime without redeployment. Seeded with `PublicYear = 2026` and `OpenSeasons = [2026]` via `HasData()` in the entity configuration.
