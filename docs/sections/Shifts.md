# Shifts — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Any active member | Browse available shifts; sign up for shifts; view own signups; bail from own signups |
| Department coordinator | Manage rotas and shifts for their department; approve/refuse signups; voluntell users; manage tags |
| VolunteerCoordinator | All coordinator capabilities across all departments; move rotas between departments |
| NoInfoAdmin, Admin | Approve/refuse/bail signups across all departments; view volunteer medical data |
| Admin only | Manage event settings (dates, timezone, EE capacity, global volunteer cap, shift browsing toggle) |

## Invariants

- `ShiftsController` requires `[Authorize]` at the controller level. Admin-only actions (event settings) require `RoleNames.Admin`.
- `ShiftAdminController` requires `[Authorize]` at the controller level. Every action checks department coordinator status OR `RoleChecks.IsVolunteerManager(User)`.
- `ShiftDashboardController` requires Admin, NoInfoAdmin, or VolunteerCoordinator.
- `VolController` requires `[Authorize]` at the controller level. Dashboard/management actions check `ShiftRoleChecks.CanAccessDashboard(User)`.
- Rota belongs to a parent team (department). Rota has a `RotaPeriod` (Build/Event/Strike) that determines shift type (all-day vs time-slotted).
- Shift belongs to a Rota. Shift has DayOffset + StartTime + Duration + IsAllDay flag.
- ShiftSignup state machine: Pending -> Confirmed/Refused/Bailed/Cancelled/NoShow. Only valid forward transitions.
- `ShiftRoleChecks.IsPrivilegedSignupApprover` = Admin or NoInfoAdmin. These can approve/refuse across all departments.
- `ShiftRoleChecks.CanManageDepartment` = IsPrivilegedSignupApprover or VolunteerCoordinator.
- `ShiftRoleChecks.CanViewMedical` = Admin or NoInfoAdmin only. Medical data from VolunteerEventProfile is restricted.
- Rota visibility is controlled by `IsVisibleToVolunteers` toggle (default true).
- Voluntelling (admin-initiated signup) sets `EnrolledByUser` on the signup.
- Range signups link multiple shifts via `SignupBlockId`; range operations (bail, approve, refuse) operate on the block.
- EventSettings is a singleton per event; controls dates, timezone, EE capacity, and caps.
- GeneralAvailability tracks per-user per-event day availability.

## Triggers

- When a signup is approved/refused, an email notification is queued to the volunteer.
- When a signup is voluntelled, an email notification is queued to the volunteer.
- Range signups/bails create/cancel all shifts in the date range atomically.

## Cross-Section Dependencies

- **Teams**: Rotas belong to a parent team (department). Coordinator status on the team determines shift management access.
- **Profiles**: VolunteerEventProfile stores per-event volunteer data (skills, dietary, medical).
- **Admin**: EventSettings management is in VolController/ShiftsController (Admin only).
- **Email**: Signup status change notifications are queued through the email outbox.
