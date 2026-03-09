# Google Groups, Sync Modes & Navigation Restructure — Design

**Date:** 2026-03-09

## Overview

Three interconnected changes:

1. **Google Groups sync** — full member sync for Google Groups linked to teams, with dynamic group creation
2. **Sync mode system** — per-service settings (None / AddOnly / AddAndRemove) stored in DB, controlling scheduled job behavior
3. **Navigation restructure** — separate Board and Admin areas, add TeamsAdmin role, move sync status page to Teams area

## Design Principles

- **Single code path** — preview, manual action, and scheduled jobs all use the same sync logic. The only variable is which actions get executed (preview vs add-only vs add+remove).
- **App is source of truth** — expected membership comes from team membership in the database. Any Google-side member not in the app is "extra" and shows as a removal candidate.
- **Mode guards automation, not operators** — sync mode controls what scheduled jobs do. Manual actions from the admin UI are always available regardless of mode.

---

## 1. Data Model Changes

### New Entity: `SyncServiceSettings`

| Column | Type | Notes |
|--------|------|-------|
| `Id` | Guid | PK |
| `ServiceType` | string (enum) | Unique index. `GoogleDrive`, `GoogleGroups`, `Discord` |
| `SyncMode` | string (enum) | `None` (default), `AddOnly`, `AddAndRemove` |
| `UpdatedAt` | Instant | |
| `UpdatedByUserId` | Guid? | FK to User. Null for seed data. |

Seeded with one row per `SyncServiceType`, all defaulting to `None`.

### New Enums

**`SyncServiceType`**: `GoogleDrive`, `GoogleGroups`, `Discord`

**`SyncMode`**: `None`, `AddOnly`, `AddAndRemove`

**`SyncAction`**: `Preview`, `AddOnly`, `AddAndRemove` — used in the code path, not persisted. Maps from `SyncMode` for scheduled jobs; passed explicitly for manual actions.

### Modified Entity: `Team`

Add `GoogleGroupPrefix` (string?, max 64). The part before `@nobodies.team`. Null means no group for this team.

- Unique filtered index where not null (no two teams share a group email)
- Computed property `GoogleGroupEmail` → `$"{GoogleGroupPrefix}@nobodies.team"` or null. EF-ignored.

### New Role Constant

`RoleNames.TeamsAdmin` = `"TeamsAdmin"`

No entity changes — stored as a string in `RoleAssignment.RoleName` like all other roles.

---

## 2. Unified Sync Code Path

### Interface Changes to `IGoogleSyncService`

```
SyncResourcesByTypeAsync(GoogleResourceType type, SyncAction action, CancellationToken ct)
    → SyncPreviewResult

SyncSingleResourceAsync(Guid resourceId, SyncAction action, CancellationToken ct)
    → ResourceSyncDiff
```

Replace: `PreviewSyncAllAsync`, `SyncAllResourcesAsync`, `SyncResourcePermissionsAsync`.

Keep: per-user methods (`AddUserToTeamResourcesAsync`, `RemoveUserFromTeamResourcesAsync`) for outbox-driven real-time membership changes.

### Flow

1. Load all active resources of the given type
2. **Drives:** group by `GoogleId` (multiple teams can share one). Expected members = union of all linked teams' active members.
3. **Groups:** one group per team. Expected members = that team's active members.
4. Fetch current members from Google API
5. Compute diff: correct, missing (to add), extra (to remove)
6. `Preview` → return diff only
7. `AddOnly` → execute adds, return diff with results
8. `AddAndRemove` → execute adds and removes, return diff with results

### Scheduled Jobs

Jobs read `SyncMode` from `SyncServiceSettings` table:
- `None` → skip entirely (no API calls)
- `AddOnly` → call `SyncResourcesByTypeAsync(..., SyncAction.AddOnly)`
- `AddAndRemove` → call `SyncResourcesByTypeAsync(..., SyncAction.AddAndRemove)`

### Manual Actions

Admin UI buttons call the same methods with an explicit `SyncAction`. Not gated by the DB sync mode.

---

## 3. Google Groups — Creation & Settings

### Group Lifecycle

1. TeamsAdmin or Board sets `GoogleGroupPrefix` on a team
2. System checks if a `GoogleResource` of type `Group` already exists for this team
3. If not: check if the group exists in Google Workspace
   - Exists → link it (create `GoogleResource` row)
   - Doesn't exist → create it with configured settings, then link it
4. If prefix is cleared (set to null) → soft-deactivate the `GoogleResource` (`IsActive = false`). The Google Group is NOT deleted.

### Group Creation Settings

| Setting | Value |
|---------|-------|
| `WhoCanJoin` | `INVITED_CAN_JOIN` |
| `WhoCanViewMembership` | `ALL_MEMBERS_CAN_VIEW` |
| `WhoCanContactOwner` | `ALL_MANAGERS_CAN_CONTACT` |
| `WhoCanPostMessage` | `ANYONE_CAN_POST` |
| `WhoCanViewGroup` | `ALL_MEMBERS_CAN_VIEW` |
| `WhoCanModerateMembers` | `OWNERS_AND_MANAGERS` |
| `AllowExternalMembers` | `true` |

Kept configurable in `GoogleWorkspaceSettings.Groups` (appsettings) but now actually applied during creation (fixes todo P1-13).

### Member Role

All synced members added as `MEMBER` role. Service account has domain-wide Groups Admin — does not need to be a group member.

---

## 4. TeamsAdmin Role

### Capabilities

| Capability | Admin | Board | TeamsAdmin | Lead (own team) |
|------------|-------|-------|------------|-----------------|
| Create/archive teams | Yes | Yes | Yes | No |
| Approve/reject join requests (any team) | Yes | Yes | Yes | No |
| Approve/reject join requests (own team) | Yes | Yes | Yes | Yes |
| Assign/remove leads (any team) | Yes | Yes | Yes | No |
| Set `GoogleGroupPrefix` on any team | Yes | Yes | Yes | No |
| Manage linked resources (any team) | Yes | Yes | Yes | No |
| View sync status (preview) | Yes | Yes | Yes | No |
| Execute sync actions | Yes | No | No | No |
| Change sync mode settings | Yes | No | No | No |

### Wiring

- `RoleNames.TeamsAdmin` constant
- `MembershipRequiredFilter` bypass list — add TeamsAdmin
- `CanUserApproveRequestsForTeamAsync` — add TeamsAdmin
- `CanManageTeamResourcesAsync` — add TeamsAdmin
- `AdminController.CanManageRole` — Board and Admin can assign/end TeamsAdmin
- Navigation — TeamsAdmin sees extra buttons on Teams page, not Board or Admin menus

### What TeamsAdmin Does NOT Get

No access to: Board area (humans, roles, audit), application voting, Admin area (sync settings, health, Hangfire, GDPR), onboarding review.

---

## 5. Navigation & URL Restructure

### Header

| Item | Visible To |
|------|------------|
| Review | ConsentCoordinator, VolunteerCoordinator, Board, Admin |
| Voting | Board, Admin |
| Board | Board, Admin |
| Admin | Admin only |
| Teams | ActiveMember+ (extra buttons for TeamsAdmin, Board, Admin) |

### Teams Area (`/Teams/...`)

| Route | Purpose | Access |
|-------|---------|--------|
| `/Teams` | Browse/join teams | ActiveMember+ |
| `/Teams/{slug}` | Team detail | ActiveMember+ |
| `/Teams/{slug}/Admin` | Team management | Lead, TeamsAdmin, Board, Admin |
| `/Teams/Sync` | Tabbed sync status page | TeamsAdmin, Board, Admin (actions: Admin only) |

Extra buttons on `/Teams` for TeamsAdmin/Board/Admin: "Create Team", "Sync Status".

### Board Area (`/Board/...`)

| Route | Purpose | Access |
|-------|---------|--------|
| `/Board/Humans` | Humans list | Board, Admin |
| `/Board/Humans/{id}` | Human detail | Board, Admin |
| `/Board/Roles` | Role management | Board, Admin |
| `/Board/AuditLog` | Audit log | Board, Admin |

### Admin Area (`/Admin/...`)

| Route | Purpose | Access |
|-------|---------|--------|
| `/Admin/SyncSettings` | Sync mode per service | Admin |
| `/Admin/Health` | System health | Admin |
| `/Admin/SyncSystemTeams` | Trigger system team sync | Admin |
| `/Admin/Hangfire` | Hangfire dashboard | Admin |
| `/Admin/Purge` | GDPR purge | Admin |

---

## 6. Sync Status Page UI (`/Teams/Sync`)

### Layout

Top bar: "Resource Sync Status" with tabs — Drives | Groups | Discord (disabled placeholder).

### Per Tab

**Summary row:** Stat cards (Total, In Sync, Drifted, Errors). Toggle: "Show changes only" (default) / "Show all". Admin only: bulk "Add All Missing" and "Sync All" buttons.

**Resource list:** One card per resource showing:
- Resource name (linked URL for drives, group email for groups)
- Linked teams (comma-separated)
- Status badge (In Sync / Drifted / Error)
- Admin only: per-resource "Add Missing" / "Sync All" buttons

**Expandable member detail per resource:**

| Column | Description |
|--------|-------------|
| Email | User's email |
| Status | Correct / Missing / Extra |
| Teams | Which linked teams they belong to |

"Correct" rows hidden when "Show changes only" is on.

### Drives Tab

Resources grouped by `GoogleId`. Multiple teams can share one drive. Expected members = union of all linked teams' active members.

### Groups Tab

One row per group (1:1 with team via `GoogleGroupPrefix`). Expected members = team's active members.

### Loading

- Page loads immediately with resource list from DB
- Diffs load async via AJAX per tab on selection
- Spinner per resource while loading
- Errors shown inline

### Action Flow

1. Admin clicks "Add Missing" or "Sync All"
2. Confirmation modal showing counts
3. POST to server
4. Resource row updates in-place with new status

---

## 7. Resolved Todos

This design addresses:
- **P1-13**: Apply configured Google group settings during provisioning — now applied during group creation
- Removal of hardcoded add-only behavior — replaced by configurable sync modes
- Admin sync page limitations — replaced by tabbed per-resource member detail UI
