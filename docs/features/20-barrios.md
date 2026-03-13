# Barrios (Camps)

## Business Context

Nobodies Collective organizes camping areas ("barrios") at Nowhere and related events. Each barrio is a self-organizing community that registers annually, receives admin approval, and is listed publicly. Barrios have leads who manage their profile, season data, and membership status. The system tracks barrio history across years through seasonal opt-ins.

## User Stories

### US-20.1: Browse Barrios
**As a** visitor or member
**I want to** see all barrios for the current public year
**So that** I can discover communities I might want to join

**Acceptance Criteria:**
- Public page showing all active barrios as cards
- Filter by vibe, sound zone, kids-friendly, accepting members
- Each card shows name, short description, image, vibes, and status badges
- Sorted alphabetically by name
- Clicking a card navigates to the barrio detail page

### US-20.2: View Barrio Details
**As a** visitor or member
**I want to** see detailed information about a barrio
**So that** I can learn about its community and decide whether to join

**Acceptance Criteria:**
- Shows barrio name, contact info, description, images
- Displays current season data (vibes, kids policy, performance space, etc.)
- Shows leads with display names
- Shows historical names if any
- Leads and admins see edit link

### US-20.3: Register a New Barrio
**As an** authenticated member
**I want to** register a new barrio
**So that** my community can participate in the event

**Acceptance Criteria:**
- Only available when a season is open for registration
- Captures barrio details: name, contact info, Swiss camp flag, times at Nowhere
- Captures season-specific data: description, vibes, kids policy, sound zone, etc.
- Optional historical names (comma-separated)
- Creates barrio with Pending status
- Registering user becomes Primary Lead
- Redirects to detail page with success message

### US-20.4: Edit Barrio
**As a** barrio lead or CampAdmin
**I want to** update my barrio's information
**So that** the listing stays current

**Acceptance Criteria:**
- Leads can edit their own barrio; CampAdmin/Admin can edit any
- Can update contact info, season data, and barrio-level fields
- Name change blocked after name lock date
- Can upload, delete, and reorder images
- Can manage co-leads (add, remove, transfer primary)

### US-20.5: Opt-In to New Season
**As a** barrio lead
**I want to** opt my barrio into a new open season
**So that** we can participate again this year

**Acceptance Criteria:**
- Only available when target season is open
- Creates a new BarrioSeason with Pending status
- Copies barrio identity but requires fresh season data review
- Redirects to edit page

### US-20.6: Approve/Reject Season Registration
**As a** CampAdmin or Admin
**I want to** review and approve or reject pending barrio registrations
**So that** only legitimate barrios appear in the public listing

**Acceptance Criteria:**
- Admin dashboard shows all pending seasons
- Approve transitions season to Active status
- Reject requires notes explaining the reason
- Records reviewer ID and timestamp

### US-20.7: Manage Seasons
**As a** CampAdmin or Admin
**I want to** open/close registration seasons, set the public year, and configure name lock dates
**So that** the barrio registration lifecycle is controlled

**Acceptance Criteria:**
- Open a season by year (adds to OpenSeasons list)
- Close a season by year (removes from OpenSeasons list)
- Set public year (controls which year is shown on the public page)
- Set name lock date per year (prevents name changes after date)

### US-20.8: Delete Barrio
**As an** Admin
**I want to** permanently delete a barrio
**So that** invalid or test entries can be removed

**Acceptance Criteria:**
- Admin-only action (not CampAdmin)
- Deletes barrio and all related data (seasons, leads, images, historical names)
- Requires confirmation

### US-20.9: View Season Details by Year
**As a** visitor or member
**I want to** view a barrio's details for a specific season year
**So that** I can see historical or non-current season information

**Acceptance Criteria:**
- Accessible at `/Barrios/{slug}/Season/{year}`
- Returns 404 if barrio or season not found
- Reuses the detail view with the specified season's data

### US-20.10: API Access
**As a** website developer
**I want to** access barrio data via JSON API
**So that** I can integrate barrio listings into the main website

**Acceptance Criteria:**
- `GET /api/barrios/{year}` returns all barrios with season data for a year
- `GET /api/barrios/{year}/placement` returns placement-relevant data (space, sound zone, containers, electrical)
- Both endpoints are public (no authentication required)

## Data Model

### Barrio
```
Barrio
├── Id: Guid
├── Slug: string [unique, URL-friendly]
├── ContactEmail: string
├── ContactPhone: string
├── WebOrSocialUrl: string?
├── ContactMethod: string
├── IsSwissCamp: bool
├── TimesAtNowhere: int
├── CreatedByUserId: Guid (FK → User)
├── CreatedAt: Instant
├── UpdatedAt: Instant
└── Navigation: Seasons, Leads, HistoricalNames, Images
```

### BarrioSeason
```
BarrioSeason
├── Id: Guid
├── BarrioId: Guid (FK → Barrio)
├── Year: int
├── Name: string
├── NameLockDate: LocalDate?
├── NameLockedAt: Instant?
├── Status: BarrioSeasonStatus [enum]
├── BlurbLong / BlurbShort: string
├── Languages: string
├── AcceptingMembers: YesNoMaybe
├── KidsWelcome: YesNoMaybe
├── KidsVisiting: KidsVisitingPolicy
├── KidsAreaDescription: string?
├── HasPerformanceSpace: PerformanceSpaceStatus
├── PerformanceTypes: string?
├── Vibes: List<BarrioVibe> [JSON]
├── AdultPlayspace: AdultPlayspacePolicy
├── MemberCount: int
├── SpaceRequirement: SpaceSize?
├── SoundZone: SoundZone?
├── ContainerCount: int
├── ContainerNotes: string?
├── ElectricalGrid: ElectricalGrid?
├── ReviewedByUserId: Guid?
├── ReviewNotes: string?
├── ResolvedAt: Instant?
├── CreatedAt: Instant
└── UpdatedAt: Instant
```

### BarrioLead
```
BarrioLead
├── Id: Guid
├── BarrioId: Guid (FK → Barrio)
├── UserId: Guid (FK → User)
├── Role: BarrioLeadRole [Primary, CoLead]
├── JoinedAt: Instant
├── LeftAt: Instant? (null = active)
└── Computed: IsActive (LeftAt == null)
```

### BarrioSettings (singleton)
```
BarrioSettings
├── Id: Guid
├── PublicYear: int
└── OpenSeasons: List<int> [JSON]
```

### Supporting Entities
- **BarrioHistoricalName**: Id, BarrioId, Name, RecordedAt
- **BarrioImage**: Id, BarrioId, StoragePath, FileName, ContentType, FileSize, SortOrder, UploadedAt

### Enums
```
BarrioSeasonStatus: Pending(0), Active(1), Full(2), Inactive(3), Rejected(4), Withdrawn(5)
BarrioLeadRole: Primary(0), CoLead(1)
BarrioVibe: Chill, Party, Artsy, Community, Adventurous, Spiritual, Educational, Musical, Foodie, EcoConscious
YesNoMaybe: No(0), Yes(1), Maybe(2)
SoundZone: Quiet(0), Medium(1), Loud(2)
SpaceSize: Small(0), Medium(1), Large(2), ExtraLarge(3)
KidsVisitingPolicy: NoKids(0), VisitorsOnly(1), DaytimeOnly(2), Anytime(3)
PerformanceSpaceStatus: None(0), Available(1), Planned(2)
AdultPlayspacePolicy: None(0), Private(1), SemiPublic(2)
ElectricalGrid: None(0), LowPower(1), Standard(2), HighPower(3)
```

## Registration Workflow

```
Authenticated User
        │
        ▼
┌───────────────────┐     Season
│ Check open season │──── closed ──→ Redirect with error
└─────────┬─────────┘
          │ open
          ▼
┌───────────────────┐
│ Fill registration │
│ form              │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ Create Barrio     │
│ + BarrioSeason    │
│ (Status=Pending)  │
│ + BarrioLead      │
│ (Role=Primary)    │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ Redirect to       │
│ Detail page       │
└───────────────────┘
```

## Season Approval Workflow

```
           ┌─────────┐
           │ Pending │
           └────┬────┘
                │
   ┌────────────┼────────────┐
   │            │            │
┌──▼───┐   ┌───▼───┐   ┌───▼──────┐
│Approve│   │Reject │   │Withdraw  │
└──┬───┘   └───┬───┘   └───┬──────┘
   │           │            │
┌──▼───┐   ┌───▼────┐  ┌───▼──────┐
│Active│   │Rejected│  │Withdrawn │
└──┬───┘   └────────┘  └──────────┘
   │
   ├──→ Full (SetSeasonFull)
   ├──→ Inactive (Deactivate)
   └──→ Active (Reactivate)
```

## Authorization

| Action | Required Role |
|--------|---------------|
| Browse barrios | Public (AllowAnonymous) |
| View barrio details | Public (AllowAnonymous) |
| Register barrio | Authenticated |
| Edit barrio | Barrio Lead, CampAdmin, or Admin |
| Opt-in to season | Barrio Lead, CampAdmin, or Admin |
| Manage leads | Barrio Lead, CampAdmin, or Admin |
| Upload/delete images | Barrio Lead, CampAdmin, or Admin |
| Approve/reject season | CampAdmin or Admin |
| Open/close season | CampAdmin or Admin |
| Set public year | CampAdmin or Admin |
| Set name lock date | CampAdmin or Admin |
| Delete barrio | Admin only |
| JSON API | Public (AllowAnonymous) |

## URL Structure

| Route | Description |
|-------|-------------|
| `GET /Barrios` | Public barrio listing |
| `GET /Barrios/{slug}` | Barrio detail page |
| `GET /Barrios/{slug}/Season/{year}` | Barrio detail for specific season |
| `GET /Barrios/Register` | Registration form |
| `POST /Barrios/Register` | Submit registration |
| `GET /Barrios/{slug}/Edit` | Edit form |
| `POST /Barrios/{slug}/Edit` | Submit edits |
| `POST /Barrios/{slug}/OptIn/{year}` | Opt-in to season |
| `POST /Barrios/{slug}/Leads/Add` | Add co-lead |
| `POST /Barrios/{slug}/Leads/Remove/{leadId}` | Remove lead |
| `POST /Barrios/{slug}/Leads/TransferPrimary` | Transfer primary lead |
| `POST /Barrios/{slug}/Images/Upload` | Upload image |
| `POST /Barrios/{slug}/Images/Delete/{imageId}` | Delete image |
| `POST /Barrios/{slug}/Images/Reorder` | Reorder images |
| `GET /Barrios/Admin` | Admin dashboard |
| `POST /Barrios/Admin/Approve/{seasonId}` | Approve season |
| `POST /Barrios/Admin/Reject/{seasonId}` | Reject season |
| `POST /Barrios/Admin/OpenSeason/{year}` | Open season |
| `POST /Barrios/Admin/CloseSeason/{year}` | Close season |
| `POST /Barrios/Admin/SetPublicYear` | Set public year |
| `POST /Barrios/Admin/SetNameLockDate` | Set name lock date |
| `POST /Barrios/Admin/Delete/{barrioId}` | Delete barrio |
| `GET /api/barrios/{year}` | JSON API: barrios for year |
| `GET /api/barrios/{year}/placement` | JSON API: placement data |

## Related Features

- [Authentication](01-authentication.md) - User identity for barrio registration and lead management
- [Teams](06-teams.md) - Similar self-organizing group concept; barrios are event-specific
- [Administration](09-administration.md) - Admin role provides full barrio management access
