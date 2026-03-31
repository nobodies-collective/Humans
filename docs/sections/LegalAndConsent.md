# Legal & Consent — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Anonymous | View published legal documents via LegalController (public) |
| Any authenticated user | View own consent status; sign/re-sign document versions via ConsentController |
| ConsentCoordinator, Board, Admin | Review consent checks in OnboardingReviewController; clear or flag consent checks |
| Board, Admin | Manage legal documents and document versions via AdminLegalDocumentsController |

## Invariants

- `LegalController` allows anonymous access — no `[Authorize]` at the controller level.
- `ConsentController` requires `[Authorize]` at the controller level — accessible during onboarding (exempt from MembershipRequiredFilter).
- `AdminLegalDocumentsController` requires `RoleGroups.BoardOrAdmin` at the controller level.
- **ConsentRecord is immutable.** Database triggers prevent UPDATE and DELETE on `consent_records`. Only INSERT is allowed.
- Each ConsentRecord links a User to a specific DocumentVersion with a timestamp and consent type (granted/withdrawn).
- LegalDocument can be team-scoped (linked to a Team) or global (no team link).
- DocumentVersion contains the actual content, versioned per LegalDocument.
- Legal documents are synced from GitHub via `SyncLegalDocumentsJob`.
- When all required documents have active consent, the user's `ConsentCheckStatus` transitions from null to Pending.
- `SuspendNonCompliantMembersJob` suspends members who no longer have valid consents for required documents.
- `SendReConsentReminderJob` sends reminders when new document versions require re-consent.

## Triggers

- When a user signs all required documents: `ConsentCheckStatus` on Profile transitions from null to `Pending`.
- When a Consent Coordinator clears a consent check: user is auto-approved as Volunteer.
- When a Consent Coordinator flags a consent check: user's Volunteer activation is blocked until Board/Admin review.
- When a new DocumentVersion is published: existing consents for the old version may become stale; re-consent jobs notify affected users.

## Cross-Section Dependencies

- **Profiles**: ConsentCheckStatus lives on Profile. Consent completion triggers the onboarding gate.
- **Onboarding**: Consent is a mandatory step in the onboarding pipeline before Volunteer activation.
- **Teams**: LegalDocument can be scoped to a specific team.
- **Google Integration**: Legal doc sync from GitHub is a background job (`SyncLegalDocumentsJob`).
