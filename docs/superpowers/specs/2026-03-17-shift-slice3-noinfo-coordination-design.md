# Shift Management Slice 3 — NoInfo & Coordination Design

**Date:** 2026-03-17
**Parent spec:** `docs/specs/2026-03-16-shift-management-design.md` (sections 6.4, 6.5, 4.7, 12, 2.9 visibility, 6.1)

---

## Overview

Slice 3 builds operational coordination features on top of Slices 1 (core entities + lead management) and 2 (volunteer experience). It delivers the NoInfo dashboard, voluntell capability, no-show marking UI, build/strike staffing visualization, volunteer event profile display to coordinators, and a shifts summary card on department pages.

## Deliverables

1. `/Shifts/Dashboard` — urgency-ranked unfilled shifts with filters
2. Voluntell — inline search on dashboard, assign volunteers to shifts
3. No-show marking UI — on `/Teams/{slug}/Shifts` for past shifts + no-show history on volunteer profiles
4. Build/strike staffing visualization — Chart.js on dashboard and department shifts page
5. Volunteer event profile badges — in signup lists for coordinators
6. Shifts summary card — on `/Teams/{slug}` for departments (backfill from Slice 1 — was listed there but not delivered)

---

## 1. Dashboard (`/Shifts/Dashboard`)

### Controller

New `ShiftDashboardController` at route `[Route("Shifts/Dashboard")]`.

**Authorization:** NoInfoAdmin or Admin (checked via `User.IsInRole`).

**Actions:**

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/Shifts/Dashboard` | Index — urgency-ranked shifts with filters |
| `GET` | `/Shifts/Dashboard/SearchVolunteers?shiftId={id}&query={name}` | AJAX — volunteer search for voluntell |
| `POST` | `/Shifts/Dashboard/Voluntell` | Assign volunteer to shift |

### Index Action

- Calls existing `ShiftManagementService.GetUrgentShiftsAsync(eventSettingsId, limit: null, departmentId, date)`
- Accepts optional `departmentId` (Guid?) and `date` (string, parsed to LocalDate) query params
- Populates department dropdown from all parent teams with rotas
- Returns `ShiftDashboardViewModel`

### SearchVolunteers Action (AJAX)

- Searches users by display name (case-insensitive `Contains`)
- For each match, returns:
  - `UserId`, `DisplayName`
  - `Skills`, `Quirks`, `Languages`, `DietaryPreference` from `VolunteerEventProfile`
  - `BookedShiftCount` — count of Confirmed signups in the active event
  - `HasOverlap` — boolean, whether the volunteer has a Confirmed signup overlapping the target shift's time slot
- Returns JSON array, max 10 results
- Authorization: same as dashboard (NoInfoAdmin/Admin)

### Voluntell Action

- Accepts `shiftId` (Guid) + `userId` (Guid) via form POST
- Calls existing `ShiftSignupService.VoluntellAsync(userId, shiftId, currentUserId)`
- Sets TempData success/error message
- Redirects back to dashboard

### View (`Views/ShiftDashboard/Index.cshtml`)

**Layout:**
- Filter bar at top: date picker input + department dropdown + "Filter" button
- Build/strike staffing chart (Chart.js, see Section 4)
- Urgency table below

**Urgency table columns:**
- Shift title
- Department name
- Date/time (resolved, with period badge: Build/Burn/Strike)
- Slots: "X / Y" (confirmed / max), red text if below MinVolunteers
- Priority badge (Normal=secondary, Important=warning, Essential=danger)
- Voluntell button

**Voluntell inline panel** (parent spec says "modal" — inline collapse chosen instead for better UX with server-rendered pages; keeps context visible, avoids z-index issues):
- Button click expands a panel below the row (Bootstrap collapse)
- Panel contains: text input for name search, results area
- Typing in the input triggers AJAX to `SearchVolunteers` (debounced, min 2 chars)
- Results show: volunteer name, profile badges (skills, quirks, dietary, languages), "N shifts booked", overlap warning if applicable, "Assign" button
- "Assign" button submits a form POST to `/Shifts/Dashboard/Voluntell`

### ViewModel

```csharp
public class ShiftDashboardViewModel
{
    public List<UrgentShift> Shifts { get; set; } = [];
    public List<DepartmentOption> Departments { get; set; } = [];
    public Guid? SelectedDepartmentId { get; set; }
    public string? SelectedDate { get; set; }
    public EventSettings EventSettings { get; set; } = null!;
    public List<DailyStaffingData> StaffingData { get; set; } = [];
}
```

---

## 2. No-Show Marking UI

### Where

Extend existing `ShiftAdmin/Index.cshtml` (`/Teams/{slug}/Shifts`).

### Behavior

For past shifts (where `AbsoluteEnd < SystemClock.Instance.GetCurrentInstant()`):
- Show a "Signups (N)" button on the shift row that expands (Bootstrap collapse) to reveal the signup list
- Each confirmed signup shows: volunteer name + event profile badges + "Mark No-Show" button
- "Mark No-Show" submits a form POST to the existing `ShiftAdminController.MarkNoShow` action
- Already-marked NoShow signups show with a "No-Show" badge (no button)
- Bailed signups shown as muted with "Bailed" badge

### Authorization

Only shown when `CanApproveSignups` is true (Dept Coordinators for own dept, NoInfoAdmin, Admin).

### Data

The existing `GetRotasByDepartmentAsync` already includes `Shifts` and their `ShiftSignups`. Need to also include `ShiftSignup.User` (for display name) and load `VolunteerEventProfile` for badge display.

**Service change:** Add `.ThenInclude(s => s.User)` to the signup include chain in `GetRotasByDepartmentAsync`. Load volunteer event profiles separately via a batch query on the user IDs present in the signups (avoids deep include chains).

### No-Show History on Volunteer Profile

Per parent spec §4.7: "No-show history is visible on a volunteer's profile (to all Dept Coordinators, NoInfoAdmin, Admin — cross-department visibility)."

**Where:** Extend the existing profile view (the page that shows a user's details to coordinators/admins).

**Data:** Query `ShiftSignups` where `UserId = profileUserId` and `Status = NoShow`, include `Shift.Rota.Team` for department/shift context.

**Display:** A "No-Show History" section showing a table: shift title, department, date, marked by whom, date marked. Only visible to Dept Coordinators (any department, not just own), NoInfoAdmin, Admin. Hidden from the profile owner and regular volunteers.

**Service method:** Add `GetNoShowHistoryAsync(Guid userId)` to `ShiftSignupService` returning `IReadOnlyList<ShiftSignup>` filtered to NoShow status with includes.

---

## 3. Volunteer Event Profile Display

### Inline Badge Rendering

In signup lists (pending approvals table, past-shift signup expansion, and voluntell search results), show volunteer event profile data as compact badges:

- **Skills:** Small colored pills (e.g., `<span class="badge bg-info">Bartending</span>`)
- **Quirks:** Small pills in a different color (e.g., `bg-secondary`)
- **Dietary:** Single badge with dietary preference text
- **Languages:** Small pills
- **Medical conditions:** Only visible to NoInfoAdmin and Admin. Rendered as a `<span class="badge bg-danger" title="{conditions}">Medical</span>` with tooltip showing the text. Not shown to Dept Coordinators.

### Profile Data Loading

- For `ShiftAdmin/Index.cshtml`: batch-load profiles by user IDs from the signup data. Pass as a `Dictionary<Guid, VolunteerEventProfile>` on the view model.
- For voluntell search results: include profile data in the AJAX response.
- For dashboard: profiles aren't shown in the urgency table (it's shift-level, not signup-level).

### ViewModel Addition

Add to `ShiftAdminViewModel`:
```csharp
public Dictionary<Guid, VolunteerEventProfile> VolunteerProfiles { get; set; } = new();
public bool CanViewMedical { get; set; } // true for NoInfoAdmin/Admin
```

### Partial View

Create `_VolunteerProfileBadges.cshtml` partial accepting `(VolunteerEventProfile? profile, bool showMedical)`. Reused in:
- Pending approvals table rows
- Past-shift signup expansion rows

---

## 4. Build/Strike Staffing Visualization

### Data Service

New method on `ShiftManagementService`:

```csharp
public async Task<List<DailyStaffingData>> GetStaffingDataAsync(
    Guid eventSettingsId, Guid? departmentId = null)
```

**DailyStaffingData:**
```csharp
public record DailyStaffingData(
    int DayOffset,
    string DateLabel,       // e.g., "Mon Jun 22"
    int ConfirmedCount,     // unique volunteers with confirmed signups on this day
    int TotalSlots,         // sum of MaxVolunteers for active shifts on this day
    string Period);         // "Build" or "Strike"
```

**Query:** For each day offset in `[BuildStartOffset..-1]` and `[EventEndOffset+1..StrikeEndOffset]`:
- Count unique `UserId` from `ShiftSignups` where `Status = Confirmed` and the shift's `DayOffset` matches
- Sum `MaxVolunteers` from active shifts with that `DayOffset`
- Optional `departmentId` filter via `Rota.TeamId`

### Chart Rendering

**Chart.js stacked bar chart** in a reusable partial `_StaffingChart.cshtml`.

**Input:** `List<DailyStaffingData>` serialized to JSON in the view.

**Chart config:**
- X-axis: date labels
- Two datasets stacked: "Confirmed" (filled portion) and "Remaining" (unfilled)
- Bar colors based on fill percentage per day:
  - >= 80%: green (`#198754`)
  - 50-79%: yellow (`#ffc107`)
  - < 50%: red (`#dc3545`)
- Remaining portion always light gray

**Used in:**
1. `ShiftDashboard/Index.cshtml` — always shows global data (all departments), regardless of the urgency table's department/date filter. The chart is a high-level overview; the table handles drill-down.
2. `ShiftAdmin/Index.cshtml` — department-scoped (passes `departmentId`)

---

## 5. Shifts Summary Card on `/Teams/{slug}`

### Where

Extend `TeamController.Details` and `Views/Team/Details.cshtml`.

### Conditions

Only shown for parent teams (departments) that have rotas in the active event settings.

### Data

New method on `ShiftManagementService`:

```csharp
public async Task<ShiftsSummaryData?> GetShiftsSummaryAsync(
    Guid eventSettingsId, Guid departmentTeamId)
```

Returns `null` if the department has no rotas in the event.

**ShiftsSummaryData:**
```csharp
public record ShiftsSummaryData(
    int TotalSlots,           // SUM(MaxVolunteers) for active shifts
    int ConfirmedCount,       // count of Confirmed signups
    int PendingCount,         // count of Pending signups
    int UniqueVolunteerCount  // distinct UserId from Confirmed signups
);
```

**Query:** Filter rotas by `EventSettingsId` and `TeamId`, join active shifts, aggregate signup counts by status.

### ViewModel

```csharp
public class ShiftsSummaryCardViewModel
{
    public int TotalSlots { get; set; }
    public int ConfirmedCount { get; set; }
    public int PendingCount { get; set; }
    public int UniqueVolunteerCount { get; set; }
    public string ShiftsUrl { get; set; } = "";  // /Teams/{slug}/Shifts
    public bool CanManageShifts { get; set; }
}
```

### Partial View (`_ShiftsSummaryCard.cshtml`)

- Card with "Shifts" header
- Bootstrap progress bar showing fill rate (confirmed / total slots, percentage label)
- Stats row: "N humans signed up" + pending badge ("N pending" in warning color, links to shifts page)
- "Manage Shifts" link — only shown if `CanManageShifts` is true

---

## Authorization Summary

| Feature | Volunteer | Dept Coordinator | NoInfoAdmin | Admin |
|---------|-----------|-----------------|-------------|-------|
| View dashboard | -- | -- | Yes | Yes |
| Voluntell | -- | -- | Yes | Yes |
| Mark no-show | -- | Own dept | Yes | Yes |
| View profile badges | -- | Own dept (no medical) | Yes (with medical) | Yes (with medical) |
| View staffing chart (global) | -- | -- | Yes | Yes |
| View staffing chart (dept) | -- | Own dept | Yes | Yes |
| View shifts summary card | Team members (read-only) | Yes | Yes | Yes |

---

## Files to Create

| File | Purpose |
|------|---------|
| `Controllers/ShiftDashboardController.cs` | Dashboard + voluntell + search |
| `Views/ShiftDashboard/Index.cshtml` | Dashboard view |
| `Views/Shared/_StaffingChart.cshtml` | Reusable Chart.js staffing visualization |
| `Views/Shared/_VolunteerProfileBadges.cshtml` | Reusable profile badge rendering |
| `Views/Shared/_ShiftsSummaryCard.cshtml` | Team page shifts card |

## Files to Modify

| File | Changes |
|------|---------|
| `Models/ShiftViewModels.cs` | Add `ShiftDashboardViewModel`, `DailyStaffingData`, `ShiftsSummaryCardViewModel`, `ShiftsSummaryData`; extend `ShiftAdminViewModel` with profile dict + `CanViewMedical` |
| `Services/ShiftManagementService.cs` | Add `GetStaffingDataAsync`, `GetShiftsSummaryAsync`; update `GetRotasByDepartmentAsync` to include signup users |
| `Services/ShiftSignupService.cs` | Add `GetNoShowHistoryAsync` |
| `Controllers/ShiftAdminController.cs` | Pass profile data and `CanViewMedical` to view |
| `Views/ShiftAdmin/Index.cshtml` | Add past-shift signup expansion with no-show buttons + profile badges |
| `Controllers/TeamController.cs` | Load shifts summary data for departments |
| `Views/Team/Details.cshtml` | Render shifts summary card partial |
| Profile view (controller + view) | Add no-show history section for coordinators/admins |

## No Database Migrations

All data needed for Slice 3 already exists from Slices 1 & 2. No new entities or columns required.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Voluntell UX | Inline collapse panel, not modal | Better context visibility with server-rendered pages; avoids z-index issues. Parent spec says "modal" — conscious deviation. |
| Dashboard chart filtering | Always global, ignores department filter | Chart is a high-level build/strike overview. Department drill-down is handled by the urgency table filter and the dept-scoped chart on `/Teams/{slug}/Shifts`. |
| Shifts summary card scope | Slice 3, backfill from Slice 1 | Parent spec listed this under Slice 1 but it was not delivered. Building it now as part of Slice 3. |
| Chart.js | Use existing dependency | Already in the project CDN includes. |
