# Budget — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| FinanceAdmin, Admin | Full CRUD on BudgetYear, BudgetGroup, BudgetCategory, BudgetLineItem; view audit log; sync departments |
| Team coordinator | Edit line items within categories linked to their team(s) in the active budget year |
| Any authenticated user | Read-only summary of the active budget year |

## Invariants

- `FinanceController` requires `RoleGroups.FinanceAdminOrAdmin` at the controller level — no action is accessible without FinanceAdmin or Admin.
- `BudgetController` requires `[Authorize]` at the controller level; line item mutations check `RoleChecks.IsFinanceAdmin(User)` or coordinator team membership.
- A coordinator can only create/edit/delete line items in categories linked to a team they coordinate.
- BudgetYear status follows Draft -> Active -> Closed. Only one year can be Active at a time.
- BudgetAuditLog is append-only: every field-level change records old value, new value, actor, and timestamp.
- BudgetGroup has an `IsRestricted` flag; restricted groups are only visible/editable by FinanceAdmin/Admin.
- Line items have ExpenditureType (CapEx/OpEx) and optional ResponsibleTeam.
- "Sync Departments" creates categories for each parent-level team that doesn't already have one.

## Triggers

- Every create/update/delete on BudgetGroup, BudgetCategory, or BudgetLineItem generates a BudgetAuditLog entry.

## Cross-Section Dependencies

- **Teams**: BudgetCategory can be linked to a Team (department). Coordinator access is derived from team coordination status.
- **Admin**: BudgetYear lifecycle management is in FinanceController (FinanceAdmin/Admin only).
