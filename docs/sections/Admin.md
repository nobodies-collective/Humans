# Admin — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Admin only | Full system administration: sync settings, email outbox, Hangfire dashboard, configuration, logs, merge users, purge humans, Google group management, DB version, system team sync |
| HumanAdmin, Board, Admin | Human administration: view human list with admin detail, role assignments, account provisioning, tier management, suspension |
| Board, Admin | Legal document management via AdminLegalDocumentsController; email administration via AdminEmailController |

## Invariants

- `AdminController` requires `RoleNames.Admin` at the controller level.
- `AdminEmailController` requires `RoleNames.Admin` at the controller level.
- `AdminLegalDocumentsController` requires `RoleGroups.BoardOrAdmin` at the controller level.
- `AdminMergeController` requires `RoleNames.Admin` at the controller level.
- `HumanController` admin actions require `RoleGroups.HumanAdminBoardOrAdmin` or `RoleGroups.HumanAdminOrAdmin`.
- Hangfire dashboard access requires `RoleChecks.IsAdmin(User)` via `HangfireAuthorizationFilter`.
- SyncSettings (per-service SyncMode: None/AddOnly/AddAndRemove) are managed at `/Admin/SyncSettings`.
- Email outbox can be paused/resumed via SystemSetting `email_outbox_paused`.
- User purge (`PurgeHuman`) permanently deletes a user and all associated data.
- User merge (`AdminMergeController`) consolidates two user accounts into one.
- Admin can assign all roles; Board/HumanAdmin can assign all roles except Admin.
- Role management checks via `RoleChecks.CanManageRole(User, roleName)` and `RoleChecks.GetAssignableRoles(User)`.

## Triggers

- When sync settings are changed, sync jobs respect the new mode on next execution.
- When email outbox is paused, `ProcessEmailOutboxJob` skips processing until resumed.
- When a user is purged, all associated data is cascade-deleted.

## Cross-Section Dependencies

- **Google Integration**: SyncSettings, group management, and reconciliation are administered here.
- **Email**: Outbox pause/resume and retry/discard operations.
- **Legal & Consent**: Document version management via AdminLegalDocumentsController.
- **Governance**: Role assignment management via HumanController admin actions.
- **All sections**: Admin has override access to all areas of the system.
