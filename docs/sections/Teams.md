# Teams — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Any active member | Browse teams, view team pages, request to join, view own membership |
| Team coordinator | Manage members, approve/reject join requests, manage roles, edit team page, manage Google resources for their team |
| TeamsAdmin, Board, Admin | All coordinator capabilities on all teams; create/delete teams; manage system team settings |
| Admin only | Execute sync actions; manage Google resource linking; delete teams |

## Invariants

- `TeamController` requires `[Authorize]` at the controller level.
- `TeamAdminController` requires `[Authorize]` at the controller level; every action checks coordinator status OR `RoleChecks.IsTeamsAdminBoardOrAdmin(User)`.
- System teams (Volunteers, Coordinators, Board, Asociados, Colaboradors) are identified by fixed GUIDs in `SystemTeamIds` and have `SystemTeamType != None`.
- System team membership is managed exclusively by `SystemTeamSyncJob` — manual add/remove is blocked for system teams.
- Teams have a parent-child hierarchy: a parent team (ParentTeamId is null) represents a department; child teams are sub-groups.
- Team membership is tracked via `TeamMember` join entity; a user can be a member of multiple teams.
- `TeamJoinRequest` follows a state machine: Pending -> Approved/Rejected; approval creates a TeamMember.
- `TeamRoleDefinition` defines named role slots on a team with optional slot count limits and IsManagement flag.
- `TeamRoleAssignment` assigns a specific TeamMember to a specific TeamRoleDefinition slot.
- Google resources (Drive folders, Groups) are linked per-team via `GoogleResource`.
- Email provisioning (`@nobodies.team`) is done per-user per-team through TeamAdminController.

## Triggers

- When a join request is approved, a TeamMember record is created.
- When a member is removed from a team, their TeamRoleAssignments for that team are also removed.
- Google resource sync events are queued to the sync outbox when team membership changes.
- `SystemTeamSyncJob` (hourly) reconciles system team membership based on role assignments and tier status.

## Cross-Section Dependencies

- **Google Integration**: Each team can have linked GoogleResource records (Drive folders, Groups). Membership changes trigger sync outbox events.
- **Shifts**: Rotas belong to a parent team (department). ShiftAdminController uses the team slug for routing.
- **Budget**: BudgetCategory can be linked to a team. Coordinator status determines budget edit access.
- **Onboarding**: Volunteer activation adds the user to the Volunteers system team.
- **Governance**: Colaborador/Asociado approval adds users to the respective system teams.
