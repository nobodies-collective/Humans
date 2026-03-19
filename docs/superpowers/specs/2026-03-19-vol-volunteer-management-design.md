# Vol — Volunteer Management Section

**Date:** 2026-03-19
**Status:** Approved
**Source:** Figma Make prototype — [Humans Volunteers Management](https://www.figma.com/make/mJ3ufiTTk8cHMvLB76ysAw/Humans-Volunteers-Management)

## Summary

Port the full `/volunteer-management` section from the Figma Make prototype into a new `/Vol` route in the Humans ASP.NET app. This reorganizes existing volunteer shift management features into a cohesive sub-section with its own sub-navigation, while coexisting with the current `/Shifts` section until a future swap.

## Decisions

| Decision | Choice |
|----------|--------|
| Scope | Full Figma prototype — all pages |
| Route prefix | `/Vol` |
| Parallel rollout | Nav link visible to Admin + all coordinator roles only |
| Styling | Bootstrap with standard colors; Figma layout/UX. Tailwind + earth tones deferred to Phase 2 |
| Role mapping | Direct mapping to existing claims |
| Landing page | `/Vol` → redirects to `/Vol/MyShifts` |
| Controller | New single `VolController` |
| Teams in /Vol | Full team views within /Vol |
| Settings & Registration | Both inside /Vol, auth-gated |
| Sub-nav | Bootstrap `nav-pills` with icons, role-gated tabs |
| Isolation | Own worktree and feature branch |

## Architecture

### Controller

`VolController : HumansControllerBase` with `[Route("Vol")]` prefix.

- Uses existing services: `IShiftSignupService`, `IShiftManagementService`, `ITeamService`
- New view models in `Models/Vol/` — flat projections, no business logic
- Views in `Views/Vol/`

### Nav Integration

New "V" link in `_Layout.cshtml`:
- Position: alongside existing nav items
- Label: "V" (short, non-confusing test label)
- Visibility: `IsAdmin || IsBoard || IsTeamsAdminBoardOrAdmin || VolunteerCoordinator || ConsentCoordinator`
- Existing `/Shifts` nav stays untouched — both coexist

### Shared Layout

`Views/Vol/_VolLayout.cshtml` — rendered by all Vol actions:
- Page header: "Volunteers Management" / "Shifts, duties, staffing coordination"
- Bootstrap `nav-pills` sub-navigation with role-gated tabs

| Tab | Route | Visible To |
|-----|-------|-----------|
| My Shifts | `/Vol/MyShifts` | All authenticated |
| All Shifts | `/Vol/Shifts` | ActiveMember (respects `IsShiftBrowsingOpen`) |
| Teams | `/Vol/Teams` | ActiveMember |
| Urgent Shifts | `/Vol/Urgent` | NoInfoAdmin, Admin, VolunteerCoordinator |
| Management | `/Vol/Management` | Admin, VolunteerCoordinator |
| Settings | `/Vol/Settings` | Admin |

Registration (`/Vol/Register`) is standalone — not in sub-nav.

### Role Mapping

| Figma Role | Humans Role/Claim |
|-----------|------------------|
| volunteer | ActiveMember claim |
| lead | Team Coordinator (TeamMemberRole.Coordinator on specific team) |
| metalead | Coordinator of a ParentTeam (team with ChildTeams) |
| noinfo | NoInfoAdmin global role |
| manager | Admin or VolunteerCoordinator global role |

Users see views based on their actual roles/claims — no role switcher in production.

## Pages

### My Shifts (`GET /Vol/MyShifts`)

Current user's shift signups in a Bootstrap card with table layout.

**Columns:**

| Column | Source |
|--------|--------|
| Duty | `Shift.Rota.Name` + `Shift.Description` |
| Team | `Shift.Rota.Team.Name` |
| Date & Time | Computed from `Shift.DayOffset`, `Shift.StartTime`, `Shift.Duration`, `EventSettings.GateOpeningDate` |
| Status | `ShiftSignup.Status` — badge: Confirmed=green, Pending=amber, Bailed=red, Refused=gray, Cancelled=gray, NoShow=red |
| Action | Bail button on Confirmed signups (with confirmation modal) |

**Data:** `IShiftSignupService.GetByUserAsync(userId)` joined to Shift/Rota.
**Mobile:** Stacked card layout (duty+team top, status badge right, date+bail bottom).

### All Shifts (`GET /Vol/Shifts`)

Shift browser with filters and rota cards grouped by department.

**Filter panel (collapsible Bootstrap card):**
- Text search (rota name, duty title, team name)
- Department dropdown (multi-select)
- Team dropdown (multi-select, filtered by department selection)
- Period chips (Build/Event/Strike)
- Day selector (buttons for each day offset, filtered by period)
- Priority chips (Normal/Important/Essential)
- Open Only toggle (default: on)
- Clear all button with active filter count

**Summary bar:** X rotas, Y shifts, Z open slots, W essential.

**Results:** Grouped by parent team, then by rota. Each rota is a Bootstrap card:
- Team label, rota name (from `Rota.Name`), description (`Rota.Description`)
- Fill bar (slots filled / total across all shifts in rota)
- Expandable "Show shifts" → shift rows:
  - Date (from DayOffset), Time, Volunteers (filled/total), Priority badge, Policy (AutoConfirm=Instant, RequiresApproval=Approval), Sign Up / Bail button

**Data:** `IShiftManagementService.GetBrowseShiftsAsync()` with filters. Rota metadata from `GetRotasByDepartmentAsync()`.

**POST actions:**
- `POST /Vol/SignUp` → `IShiftSignupService.SignUpAsync`
- `POST /Vol/Bail` → `IShiftSignupService.BailAsync`

### Teams Overview (`GET /Vol/Teams`)

Grid of department (parent team) cards.

Each card: name, accent color (from team metadata or hardcoded per-team), description, child team count, aggregate shift fill stats, lead name.

**Data:** `ITeamService` for teams, `IShiftManagementService.GetStaffingDataAsync()` for fill stats.

### Department Detail (`GET /Vol/Teams/{slug}`)

Breadcrumb: "All Departments" → department name.

Lists child teams as cards with:
- Team name, member count
- Rota fill rates per rota
- Pending join requests count (if coordinator)
- Coordinator actions (if authorized): create rota, manage members

**Data:** `ITeamService.GetTeamBySlugAsync()`, `IShiftManagementService.GetRotasByDepartmentAsync()`, `IShiftManagementService.GetStaffingDataAsync()`.

### Child Team Detail (`GET /Vol/Teams/{parentSlug}/{childSlug}`)

Full team view:
- Team info header (name, description, member count)
- Member roster with role assignments
- Rotas section: each rota as a card with shift grid (day × time), fill indicators, rota metadata (description, practical info)
- Pending join requests (if coordinator)
- Coordinator actions: create/edit rota, create/edit/delete shift, approve/refuse signups, voluntell, manage members

**Data:** Same services as Department Detail, plus `IShiftSignupService` for signup management.

### Urgent Shifts (`GET /Vol/Urgent`)

Table of unfilled shifts sorted by urgency score.

**Urgency score:** `priority_weight × remaining_slots` (uses existing `IShiftManagementService.CalculateScore`).

**Columns:**

| Column | Description |
|--------|-------------|
| Urgency | Visual bar showing relative score |
| Duty | Shift title with info tooltip for description |
| Team | Team name |
| Date & Time | Formatted from DayOffset + times |
| Capacity | Fill bar + "X left" |
| Priority | Badge (Normal/Important/Essential) |
| Action | "Find volunteer" button |

**"Find volunteer" flow:**
1. Modal with volunteer search (name, skill, team filters)
2. Results list with availability indicators
3. Click → volunteer profile modal (skills, teams, quirks, medical [restricted to NoInfoAdmin/Admin], booked shifts)
4. "Assign" button → `IShiftSignupService.VoluntellAsync()`

**Data:** `IShiftManagementService.GetUrgentShiftsAsync()`. Volunteer search via user/profile queries.

### Management (`GET /Vol/Management`)

Manager dashboard:
- System status banner (Open/Closed link to Settings)
- Global Volunteer Cap indicator (progress bar, amber at 75%, red at 90%)
- Actions grid:
  - Export All Rotas CSV → `GET /Vol/Export/Rotas`
  - Export Early Entry CSV → `GET /Vol/Export/EarlyEntry`
  - Export Cantina CSV → `GET /Vol/Export/Cantina`
  - Link to Event Settings

**Data:** `EventSettings` for system status and cap. Confirmed volunteer count from signup queries.

### Settings (`GET /Vol/Settings`, `POST /Vol/Settings`)

- Event Periods timeline: Build/Event/Strike date ranges derived from EventSettings offsets + GateOpeningDate
- System Open/Close toggle (`IsShiftBrowsingOpen`) with confirmation modal for closing
- Global Volunteer Cap input with progress indicator and threshold warnings
- Access Matrix reference table (static)

**Data:** `EventSettings` entity — read and update.

### Registration (`GET /Vol/Register`, `POST /Vol/Register`)

Multi-step wizard (server-side with form state in TempData or hidden fields):

1. **Welcome** — period overview (Build/Event/Strike info cards), "Start Registration" button
2. **Availability** — on-site only vs year-round (radio cards)
3. **Period selection** — Build/Event/Strike checkboxes (styled as cards)
4. **Path choice** — "General Volunteer" (assigned by coordinators) vs "Apply for Specific Roles" (choose teams)
5. **If general:** Confirmation summary + optional notes textarea → submit
6. **If specific:** Team picker (accordion by department with checkboxes) + notes → submit
7. **Done** — confirmation message with links to dashboard and shift browser

**Data:** Creates `GeneralAvailability` record. For specific teams: creates `TeamJoinRequest` per selected team. May update `VolunteerEventProfile`.

**Note:** This supplements (does not replace) existing membership onboarding. It captures shift volunteering interest and team preferences.

## Data Flow

### Service Reuse

No new Application layer interfaces. All pages wire into existing services:

- `IShiftSignupService` — signup lifecycle (sign up, bail, approve, refuse, voluntell, cancel, no-show)
- `IShiftManagementService` — shift browsing, urgency scoring, staffing data, rota/shift CRUD, event settings
- `ITeamService` — team hierarchy, members, join requests, role definitions

### New View Models

Each page gets a dedicated view model in `Models/Vol/`:

- `MyShiftsViewModel` — flattened shift signup rows
- `ShiftBrowserViewModel` — filter state + rota groups with shifts
- `TeamsOverviewViewModel` — department cards with aggregate stats
- `DepartmentDetailViewModel` — child teams with staffing data
- `ChildTeamDetailViewModel` — full team view with rotas, shifts, members
- `UrgentShiftsViewModel` — urgency-sorted shift list
- `ManagementViewModel` — system status, cap data, export links
- `SettingsViewModel` — event settings form
- `RegistrationViewModel` — wizard state

### Authorization

Claims-first pattern (per CODING_RULES):
- Controller actions: `[Authorize]` + `RoleChecks` helpers
- Sub-nav visibility: `@if` blocks in `_VolLayout.cshtml`
- Team-specific ops: fallback to `IShiftManagementService.IsDeptCoordinatorAsync` / `CanManageShiftsAsync`

### Error Handling

- All controller actions: try-catch with `ILogger` logging
- TempData flash messages via `SetSuccess()` / `SetError()`
- Bail/signup actions: confirmation modals before POST

## POST Actions

| Action | Route | Service Call |
|--------|-------|-------------|
| Sign up | `POST /Vol/SignUp` | `IShiftSignupService.SignUpAsync` |
| Bail | `POST /Vol/Bail` | `IShiftSignupService.BailAsync` |
| Approve signup | `POST /Vol/Approve` | `IShiftSignupService.ApproveAsync` |
| Refuse signup | `POST /Vol/Refuse` | `IShiftSignupService.RefuseAsync` |
| Voluntell | `POST /Vol/Voluntell` | `IShiftSignupService.VoluntellAsync` |
| Update settings | `POST /Vol/Settings` | Update `EventSettings` |
| Register | `POST /Vol/Register` | Create `GeneralAvailability` / `TeamJoinRequest` |
| Export Rotas | `GET /Vol/Export/Rotas` | FileResult CSV |
| Export Early Entry | `GET /Vol/Export/EarlyEntry` | FileResult CSV |
| Export Cantina | `GET /Vol/Export/Cantina` | FileResult CSV |

## New Files

| Category | Files |
|----------|-------|
| Controller | `Controllers/VolController.cs` |
| Shared layout | `Views/Vol/_VolLayout.cshtml` |
| Page views | `Views/Vol/MyShifts.cshtml`, `Shifts.cshtml`, `Teams.cshtml`, `DepartmentDetail.cshtml`, `ChildTeamDetail.cshtml`, `Urgent.cshtml`, `Management.cshtml`, `Settings.cshtml`, `Register.cshtml` |
| View models | `Models/Vol/MyShiftsViewModel.cs`, `ShiftBrowserViewModel.cs`, `TeamsOverviewViewModel.cs`, `DepartmentDetailViewModel.cs`, `ChildTeamDetailViewModel.cs`, `UrgentShiftsViewModel.cs`, `ManagementViewModel.cs`, `SettingsViewModel.cs`, `RegistrationViewModel.cs` |
| Partials | `Views/Vol/_ShiftRow.cshtml`, `_RotaCard.cshtml`, `_TeamCard.cshtml`, `_FilterPanel.cshtml`, `_UrgencyBar.cshtml` (shared partials for reuse across pages) |

## Modified Files

| File | Change |
|------|--------|
| `Views/Shared/_Layout.cshtml` | Add "V" nav link with role gate |

## No Changes To

- Domain entities
- Application interfaces
- Infrastructure/data layer
- Existing controllers/views
- Database schema

## Future Phases

- **Phase 2:** Tailwind CSS + Figma earth-tone palette reskin (CSS-only change, same Razor views)
- **Phase 3 (optional):** React/Blazor interactivity for specific components
- **Swap:** When `/Vol` is validated, replace `/Shifts` nav link → `/Vol`, remove "V" label, make `/Vol` the primary volunteering section
