# F-12: Audit Log

## Business Context

Background jobs and admin actions make changes on members' behalf (team enrollment, Google Drive/Group access, suspensions, account anonymization). The Board needs a structured, queryable audit trail showing what the system does automatically and what admins do manually, beyond what Serilog text logs provide.

## Data Model

### AuditLogEntry (append-only)

| Field | Type | Purpose |
|-------|------|---------|
| Id | Guid | PK |
| Action | AuditAction (string) | What happened |
| EntityType | string | "User", "Team", etc. |
| EntityId | Guid | Primary affected entity |
| Description | string | Human-readable text |
| OccurredAt | Instant | When |
| ActorUserId | Guid? | Human actor (null for jobs) |
| ActorName | string | "SystemTeamSyncJob" or "Admin: Jane Doe" |
| RelatedEntityId | Guid? | Secondary entity |
| RelatedEntityType | string? | "User", "Team", etc. |

Table: `audit_log`

### Immutability

Database triggers prevent UPDATE and DELETE on the `audit_log` table, matching the pattern used for `consent_records`. The `ActorName` field preserves the actor's identity even if the user is later anonymized (FK uses `SetNull` on delete).

### AuditAction Enum

Stored as string in the database. New values can be appended without migration.

- `TeamMemberAdded` — System sync added a user to a team
- `TeamMemberRemoved` — System sync removed a user from a team
- `MemberSuspended` — Admin or system suspended a member
- `MemberUnsuspended` — Admin unsuspended a member
- `AccountAnonymized` — Account deletion job anonymized a user
- `RoleAssigned` — Admin assigned a governance role
- `RoleEnded` — Admin ended a governance role
- `VolunteerApproved` — Admin approved a volunteer
- `GoogleResourceAccessGranted` — Google resource access granted (Group or Drive folder)
- `GoogleResourceAccessRevoked` — Google resource access revoked (Group or Drive folder)
- `GoogleResourceProvisioned` — New Google resource created (Drive folder or Group)
- `TeamJoinedDirectly` — User joined an open team directly
- `TeamLeft` — User left a team voluntarily
- `TeamJoinRequestApproved` — Approver approved a team join request
- `TeamJoinRequestRejected` — Approver rejected a team join request
- `TeamMemberRoleChanged` — Member role changed within a team
- `AnomalousPermissionDetected` — Drive Activity API detected a permission change not made by the system

## Service Design

`IAuditLogService` provides two overloads:

1. **Job overload** — no human actor, accepts job name string
2. **Admin overload** — accepts actor user ID and display name

The service adds entries to the `DbContext` without calling `SaveChangesAsync`. Entries are saved atomically with the caller's business operation.

## Phase 1 Coverage (Current)

### Background Jobs

| Job | Actions Logged |
|-----|---------------|
| SystemTeamSyncJob | TeamMemberAdded, TeamMemberRemoved |
| SuspendNonCompliantMembersJob | MemberSuspended |
| ProcessAccountDeletionsJob | AccountAnonymized |

### Admin Actions (AdminController)

| Action | AuditAction |
|--------|-------------|
| Suspend member | MemberSuspended |
| Unsuspend member | MemberUnsuspended |
| Approve volunteer | VolunteerApproved |
| Add role | RoleAssigned |
| End role | RoleEnded |

## Phase 2 Coverage (Current)

### TeamService

| Action | AuditAction | Actor |
|--------|-------------|-------|
| User joins open team | TeamJoinedDirectly | User |
| User leaves team | TeamLeft | User |
| Join request approved | TeamJoinRequestApproved | Approver (Board/Metalead) |
| Join request rejected | TeamJoinRequestRejected | Approver (Board/Metalead) |
| Member removed by admin | TeamMemberRemoved | Admin (Board/Metalead) |
| Member role changed | TeamMemberRoleChanged | Admin (Board/Metalead) |

### GoogleWorkspaceSyncService

| Action | AuditAction | Actor |
|--------|-------------|-------|
| Drive folder provisioned | GoogleResourceProvisioned | GoogleWorkspaceSyncService |
| Google Group provisioned | GoogleResourceProvisioned | GoogleWorkspaceSyncService |
| User added to Group | GoogleResourceAccessGranted | GoogleWorkspaceSyncService |
| User removed from Group | GoogleResourceAccessRevoked | GoogleWorkspaceSyncService |
| Drive permission added (direct or sync) | GoogleResourceAccessGranted | GoogleWorkspaceSyncService |
| Drive permission removed (direct or sync) | GoogleResourceAccessRevoked | GoogleWorkspaceSyncService |

### DriveActivityMonitorJob

| Action | AuditAction | Actor |
|--------|-------------|-------|
| Anomalous permission change detected | AnomalousPermissionDetected | DriveActivityMonitorJob |

## Google Sync Audit Log (Dedicated)

In addition to the general audit log entries above, a dedicated `GoogleSyncAuditEntry` table provides richer, Google-specific audit data with structured fields for role, source, success/failure, and denormalized email. Both systems record entries for each Google permission change — the general audit log serves the global dashboard and per-user general audit, while the Google sync audit provides detailed per-resource and per-user Google sync views.

### GoogleSyncAuditEntry (append-only)

| Field | Type | Purpose |
|-------|------|---------|
| Id | Guid | PK |
| ResourceId | Guid (FK) | The GoogleResource affected |
| UserId | Guid? (FK) | The user whose access changed |
| UserEmail | string | Email at time of action (denormalized for history) |
| Action | GoogleSyncAction (string) | PermissionGranted, PermissionRevoked, MemberAdded, MemberRemoved |
| Role | string | The role granted (e.g., "writer", "fileOrganizer", "MEMBER") |
| Source | GoogleSyncSource (string) | TeamMemberJoined, TeamMemberLeft, ManualSync, ScheduledSync, Suspension, SystemTeamSync |
| Timestamp | Instant | When the action occurred |
| Success | bool | Whether the API call succeeded |
| ErrorMessage | string? | Error details if failed |

Table: `google_sync_audit`

### Immutability

Database triggers prevent UPDATE and DELETE on the `google_sync_audit` table, matching the pattern used for `audit_log` and `consent_records`.

### Service Interface

`IGoogleSyncAuditService` provides:
- `LogAsync(...)` — records an entry (added to DbContext, not saved; caller's SaveChangesAsync persists atomically)
- `GetByResourceAsync(resourceId)` — returns entries for a specific resource (up to 200, newest first)
- `GetByUserAsync(userId)` — returns entries for a specific user (up to 200, newest first)

### Per-Resource Audit View (`/Admin/GoogleSync/Resource/{id}/Audit`)

Accessed via "Audit" button on each resource row in the Google Sync page. Shows:
- Action badge (green for grants, red for revocations)
- User email (linked to member detail if user exists)
- Role granted/revoked
- Source (what triggered the action)
- Success/failure status with error details
- Timestamp

### Per-User Google Sync Audit View (`/Admin/Members/{id}/GoogleSyncAudit`)

Accessed via "Google Sync Audit" button on the MemberDetail page audit history card. Shows the same columns as the per-resource view, plus the resource name (linked to per-resource audit).

## User Interface

### Global Audit Log Page (`/Admin/AuditLog`)

Displays all audit log entries with filtering by action type. Features:
- Filter buttons: All, Anomalous Permissions, Access Granted/Revoked, Suspensions, Roles
- Anomalous entries highlighted with warning styling
- Alert banner showing total anomaly count
- "Check Drive Activity Now" button for manual trigger
- Paginated (50 per page)

### Per-User Audit View (MemberDetail page)

Displays the 50 most recent audit entries affecting a user, queried by:
- `EntityType = 'User' AND EntityId = @userId` (direct entries)
- `RelatedEntityId = @userId` (related entries, e.g., team membership changes)

Each entry shows:
- Description (bold)
- Badge: "System" (info) or "Admin" (secondary)
- Actor name
- Timestamp (right-aligned)

## Authorization

Audit log is visible only to Board and Admin roles (inherits from AdminController's `[Authorize(Roles = "Board,Admin")]`).

## Related Features

- [F-05: Membership Status](05-membership-status.md) — Suspension triggers audit entries
- [F-08: Background Jobs](08-background-jobs.md) — Jobs are primary audit producers
- [F-09: Administration](09-administration.md) — Admin actions produce audit entries
- [F-06: Teams](06-teams.md) — Team sync produces audit entries
- [F-13: Drive Activity Monitoring](13-drive-activity-monitoring.md) — Anomalous permission detection
