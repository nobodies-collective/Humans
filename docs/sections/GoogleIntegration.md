# Google Integration — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Admin | Manage sync settings; trigger manual syncs; view reconciliation results; link/unlink Google resources; remediate group settings |
| TeamsAdmin, Board, Admin | Link/unlink Google resources to teams via TeamAdminController |
| Background jobs | Automated sync (SystemTeamSyncJob, GoogleResourceReconciliationJob, ProcessGoogleSyncOutboxJob, GoogleResourceProvisionJob) |

## Invariants

- All Google Drive resources are on Shared Drives. All Drive API calls use `SupportsAllDrives = true`.
- Permission listing includes `permissionDetails` to distinguish inherited from direct permissions. Only direct permissions are managed; inherited Shared Drive permissions are excluded from drift detection.
- Google sync is controlled by per-service `SyncMode` (None/AddOnly/AddAndRemove) configurable at `/Admin/SyncSettings`.
- Setting a service to `None` disables sync without redeploying.
- The service account authenticates as itself for ALL Google APIs — no domain-wide delegation, no `CreateWithUser`.
- There are exactly 4 gateway methods that can modify Google access; all enforce SyncSettings mode.
- `SystemTeamSyncJob` runs hourly; reconciles system team membership.
- `GoogleResourceReconciliationJob` runs daily at 03:00; detects drift between expected and actual Google resource state.
- `ProcessGoogleSyncOutboxJob` processes queued sync events from the outbox.
- `GoogleResourceProvisionJob` provisions new Google resources (folders, groups) for teams.
- `GoogleResource` entity links a team to a specific Google Drive folder or Google Group.
- User's Google service email is determined by `User.GetGoogleServiceEmail()` (GoogleEmail ?? Email).
- `DriveActivityMonitorJob` monitors Shared Drive activity for audit purposes.

## Triggers

- When team membership changes, sync outbox events are queued for Google Group/Drive updates.
- When a user's Google email changes, their Google resource memberships need re-sync.
- When a Google resource is linked to a team, current team members are synced to that resource.
- When a Google resource is unlinked, managed permissions are removed (if SyncMode allows).

## Cross-Section Dependencies

- **Teams**: GoogleResource is linked per-team. Team membership drives Google Group membership.
- **Profiles**: User.GoogleEmail determines the email address used in Google services.
- **Admin**: SyncSettings management is in AdminController (Admin only).
- **Onboarding**: Volunteer activation triggers system team sync, which cascades to Google Group membership.
