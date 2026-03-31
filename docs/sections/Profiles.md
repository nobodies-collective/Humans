# Profiles — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Authenticated user | Own profile: view, edit, manage emails, manage contact fields, upload picture, set notification preferences, request data export/deletion |
| HumanAdmin, Board, Admin | View any profile; manage humans via HumanController admin actions |
| Any active member | View other active members' profiles (restricted by contact field visibility) |

## Invariants

- Every authenticated user can edit their own profile regardless of membership status (exempt from MembershipRequiredFilter).
- Contact field visibility is enforced per-field: Board sees all; coordinators see CoordinatorsAndBoard+; shared-team members see MyTeams+; active members see AllActiveProfiles only.
- A user viewing their own profile gets BoardOnly access level (sees everything).
- Profile pictures are stored on disk under `wwwroot/uploads/avatars/`; the `ProfilePictureFileName` on Profile tracks the file.
- Birthday stores month and day only — never year. UI text must say "birthday", not "date of birth".
- MembershipTier is tracked on Profile (Volunteer/Colaborador/Asociado), not as a RoleAssignment.
- ConsentCheckStatus on Profile gates volunteer activation: null until all consents signed, then Pending/Cleared/Flagged.
- Profile deletion request sets a flag; actual deletion is processed by `ProcessAccountDeletionsJob`.
- User data export (`DownloadData`) returns all personal data as JSON (GDPR Article 15).

## Triggers

- When all required legal documents are consented to, `ConsentCheckStatus` transitions from null to `Pending`.
- When `ConsentCheckStatus` is set to `Cleared`, the user is auto-approved as a Volunteer and added to the Volunteers system team.
- When a user requests deletion, memberships and team memberships are revoked; actual data purge is deferred to the background job.

## Cross-Section Dependencies

- **Legal & Consent**: ConsentCheckStatus depends on all required DocumentVersions having ConsentRecords.
- **Teams**: Active membership = membership in the Volunteers system team. Profile activation triggers addition.
- **Onboarding**: Profile completion is a prerequisite step in the onboarding pipeline.
- **Google Integration**: `GoogleEmail` on User determines which email is used for Google Groups/Drive sync.
