# Shift Management

## Business Context

Nobodies Collective runs multi-day events (e.g., Nowhere) where volunteers are needed for shifts across departments (Gate, Bar, DPW, etc.). The shift management system lets admins configure event schedules, department coordinators create and manage shift rotas, and volunteers browse and sign up for shifts. Urgency scoring surfaces understaffed shifts to drive volunteer action.

See `docs/specs/shift-management-spec.md` for the full design specification.

## User Stories

### US-25.1: Admin Configures Event
**As an** Admin
**I want to** create and manage EventSettings (dates, timezone, EE capacity, browsing toggle)
**So that** the shift system is configured for the current event cycle

**Acceptance Criteria:**
- Only one active EventSettings at a time
- Configure gate opening date, build/event/strike offsets, timezone
- Set early entry capacity step function and barrios allocation
- Toggle shift browsing open/closed
- Set early entry close instant

### US-25.2: Coordinator Manages Rotas and Shifts
**As a** department coordinator
**I want to** create rotas with shifts for my department
**So that** volunteers can sign up for work slots

**Acceptance Criteria:**
- Create rota with name, priority (Normal/Important/Essential), and signup policy (Public/RequireApproval)
- Create shifts with title, day offset, start time, duration, min/max volunteers
- Mark shifts as AdminOnly to restrict to coordinators/admins
- Deactivate or delete rotas/shifts (delete blocked if confirmed signups exist)

### US-25.3: Volunteer Browses and Signs Up
**As a** volunteer
**I want to** browse available shifts and sign up
**So that** I can contribute to the event

**Acceptance Criteria:**
- Browse shifts filtered by department and date
- See fill status (confirmed count vs max)
- Sign up for a shift (auto-confirmed for Public policy, pending for RequireApproval)
- Overlap detection prevents signing up for conflicting time slots
- AdminOnly shifts hidden from non-privileged users
- EE freeze blocks non-privileged build shift signups after early entry close

### US-25.4: Coordinator Approves/Refuses Signups
**As a** department coordinator
**I want to** approve or refuse pending signups
**So that** I can manage who works my department's shifts

**Acceptance Criteria:**
- Approve re-validates overlap (returns warning, not blocker)
- Refuse with optional reason
- Voluntell: enroll a volunteer directly (auto-confirmed, sets Enrolled flag)

### US-25.5: Volunteer Manages Their Shifts
**As a** volunteer
**I want to** view my shifts and bail if needed
**So that** I can manage my schedule

**Acceptance Criteria:**
- View upcoming, pending, and past shifts on /Shifts/Mine
- Bail from confirmed or pending signups
- Build shift bail blocked after EE close for non-privileged users

### US-25.6: Post-Event No-Show Tracking
**As a** coordinator
**I want to** mark no-shows after shifts end
**So that** reliability data is captured

**Acceptance Criteria:**
- MarkNoShow blocked before shift end time
- Sets status to NoShow with reviewer recorded

## Data Model

| Entity | Purpose |
|--------|---------|
| `EventSettings` | Singleton event config: dates, timezone, EE capacity, browsing toggle |
| `Rota` | Shift container per department+event, with priority and signup policy |
| `Shift` | Single work slot: day offset, time, duration, volunteer min/max |
| `ShiftSignup` | User-to-shift link with state machine |
| `VolunteerEventProfile` | Per-event skills, dietary, medical info, email preferences |

## State Machine (ShiftSignup)

```
Pending --> Confirmed   (Approve / auto-confirm)
Pending --> Refused     (Refuse)
Pending --> Bailed      (Bail)
Confirmed --> Bailed    (Bail)
Confirmed --> NoShow    (MarkNoShow, post-shift only)
Confirmed --> Cancelled (system: shift deleted, account deletion)
Pending --> Cancelled   (system: shift deleted, account deletion)
```

## Authorization Model

| Role | Permissions |
|------|------------|
| Admin | Full access: manage shifts, approve signups, bypass all restrictions |
| NoInfoAdmin | Approve/refuse signups, voluntell; cannot create/edit shifts or rotas |
| Dept Coordinator | Manage rotas/shifts for own department, approve/refuse signups |
| Volunteer | Browse shifts, sign up, bail, view own schedule |

## Urgency Scoring

`score = remainingSlots * priorityWeight * durationHours * understaffedMultiplier`

- Priority weights: Normal=1, Important=3, Essential=6
- Understaffed multiplier: 2x when confirmed < minVolunteers, else 1x
- Score=0 when fully staffed (remaining=0)

## Routes

| Route | Purpose |
|-------|---------|
| `/Shifts` | Browse all shifts (filtered by department/date) |
| `/Shifts/Mine` | View own signups (upcoming, pending, past) |
| `/Shifts/Settings` | Admin: manage EventSettings |
| `/Teams/{slug}/Shifts` | Coordinator: manage rotas/shifts for a department |

## Related Features

- **Teams** (06): Departments are parent teams; coordinator roles grant shift management access
- **Profiles** (02): VolunteerEventProfile extends the user profile with event-specific data
- **Audit Log** (12): All signup state transitions are audit-logged
