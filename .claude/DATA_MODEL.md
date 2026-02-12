# Data Model

## Key Entities

| Entity | Purpose |
|--------|---------|
| User | Custom IdentityUser with Google OAuth |
| Profile | Member profile with computed MembershipStatus |
| UserEmail | Email addresses per user (login, verified, notifications) |
| ContactField | Contact info with per-field visibility controls |
| VolunteerHistoryEntry | Volunteer involvement history (events, roles, camps) |
| Application | Asociado application with Stateless state machine |
| ApplicationStateHistory | Audit trail of Application state transitions |
| RoleAssignment | Temporal role memberships (ValidFrom/ValidTo) |
| LegalDocument / DocumentVersion | Legal docs synced from GitHub |
| ConsentRecord | **APPEND-ONLY** consent audit trail |
| Team / TeamMember | Working groups |
| TeamJoinRequest | Requests to join a team |
| TeamJoinRequestStateHistory | Audit trail of TeamJoinRequest state transitions |
| GoogleResource | Shared Drive folder + Group provisioning |
| AuditLogEntry | **APPEND-ONLY** system audit trail (user actions, sync ops) |

## Relationships

```
User 1──n Profile
User 1──n UserEmail
User 1──n RoleAssignment
User 1──n ConsentRecord
User 1──n TeamMember
User 1──n Application

Profile 1──n ContactField
Profile 1──n VolunteerHistoryEntry

Team 1──n TeamMember
Team 1──n TeamJoinRequest
Team 1──n GoogleResource
Team 1──n LegalDocument

LegalDocument 1──n DocumentVersion
DocumentVersion 1──n ConsentRecord

Application 1──n ApplicationStateHistory
TeamJoinRequest 1──n TeamJoinRequestStateHistory

AuditLogEntry n──1 User (ActorUser, optional)
AuditLogEntry n──1 GoogleResource (optional)
```

## ContactField Entity

Contact fields allow members to share different types of contact information with per-field visibility controls.

### Field Types (`ContactFieldType`)

| Value | Description |
|-------|-------------|
| ~~Email~~ | **Deprecated** — use `UserEmail` entity instead. Kept for backward compatibility. |
| Phone | Phone number |
| Signal | Signal messenger |
| Telegram | Telegram messenger |
| WhatsApp | WhatsApp messenger |
| Discord | Discord username |
| Other | Custom type (requires CustomLabel) |

### Visibility Levels (`ContactFieldVisibility`)

Lower values are more restrictive. A viewer with access level X can see fields with visibility >= X.

| Value | Level | Who Can See |
|-------|-------|-------------|
| BoardOnly | 0 | Board members only |
| LeadsAndBoard | 1 | Team leads and board |
| MyTeams | 2 | Members who share a team with the owner |
| AllActiveProfiles | 3 | All active members |

### Access Level Logic

Viewer access is determined by:
1. **Self** → BoardOnly (sees everything)
2. **Board member** → BoardOnly (sees everything)
3. **Any lead** → LeadsAndBoard
4. **Shares team with owner** → MyTeams
5. **Active member** → AllActiveProfiles only

## Serialization Notes

- All entities use System.Text.Json serialization
- See `CODING_RULES.md` for serialization requirements
