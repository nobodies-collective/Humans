# Legal & Consent — Section Invariants

## Concepts

- A **Legal Document** is a named document (e.g., "Privacy Policy", "Volunteer Agreement") that may be global or scoped to a specific team. Documents are synced from a GitHub repository.
- A **Document Version** is a specific revision of a legal document with an effective date and content. When a new version is published, existing consents for the old version may become stale.
- A **Consent Record** is an append-only audit entry linking a human to a specific document version with a timestamp and consent type (granted or withdrawn). Consent records can never be updated or deleted — only new records can be inserted.
- **Consent Check** is the safety gate in the onboarding pipeline. After a human signs all required documents, a Consent Coordinator reviews and either clears or flags the check.

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| Anyone (including anonymous) | View published legal documents |
| Any authenticated human | View own consent status. Sign or re-sign document versions. Accessible during onboarding (before becoming an active member) |
| ConsentCoordinator, Board, Admin | Review consent checks in the onboarding queue. Clear or flag consent checks |
| Board, Admin | Manage legal documents and document versions (create, edit, publish new versions) |

## Invariants

- Consent records are immutable. Database triggers prevent UPDATE and DELETE operations on consent records. Only INSERT is allowed to maintain GDPR audit trail integrity.
- Legal documents can be global (required of all humans) or team-scoped (required when joining a specific team).
- When all required global documents have active consent, the human's consent check status transitions from unset to Pending.
- Legal documents are synced from a GitHub repository by a background job.
- When a new document version is published, existing consents for the old version become stale and re-consent is required.

## Negative Access Rules

- Regular humans **cannot** manage legal documents or document versions.
- ConsentCoordinator **cannot** manage legal documents or versions — they can only review and clear/flag consent checks.
- No one can update or delete consent records. They are permanently immutable.

## Triggers

- When a human signs all required global documents: their consent check status transitions to Pending.
- When a Consent Coordinator clears a consent check: the human is auto-approved as a Volunteer and added to the Volunteers system team.
- When a Consent Coordinator flags a consent check: the human's Volunteer activation is blocked until Board or Admin review.
- When a new document version is published: affected humans are notified to re-consent. A background job sends re-consent reminders.
- A background job suspends humans who no longer have valid consents for required documents.

## Cross-Section Dependencies

- **Profiles**: Consent check status lives on the profile. Consent completion triggers the onboarding gate.
- **Onboarding**: Consent to all required documents is a mandatory step before Volunteer activation.
- **Teams**: Legal documents can be scoped to a specific team. Joining a team may require consenting to team-specific documents.
- **Google Integration**: Legal document sync from GitHub is a background job.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `LegalDocumentService`, `AdminLegalDocumentService`, `ConsentService`
**Owned tables:** `legal_documents`, `document_versions`, `consent_records`

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **`ILegalDocumentRepository`** — owns `legal_documents`, `document_versions`
  - Aggregate-local navs kept: `LegalDocument.Versions`, `DocumentVersion.LegalDocument`
  - Cross-domain navs stripped: `LegalDocument.Team` (Teams section)
- **`IConsentRepository`** — owns `consent_records`
  - Aggregate-local navs kept: (none — `ConsentRecord` is a flat record)
  - Cross-domain navs stripped: `ConsentRecord.User` (Users/Identity section), `ConsentRecord.DocumentVersion` (sibling `ILegalDocumentRepository` aggregate — callers that need version data should call `ILegalDocumentService`/`ILegalDocumentRepository` by `DocumentVersionId`)
  - Note: `consent_records` is append-only per §12 (DB triggers block UPDATE/DELETE) — repository exposes `AddAsync` and `GetXxxAsync` but no `UpdateAsync`/`DeleteAsync`.

`LegalDocumentService` (the GitHub markdown fetcher) is not a data-owning service — it has zero `DbContext` usage and lives in Infrastructure as a GitHub content provider. It does not get a repository; its `IMemoryCache` usage (see below) should move into a caching decorator around `ILegalDocumentService` itself when this section migrates.

`LegalDocumentSyncService` is treated as an Infrastructure sync job. Once repositories exist it should consume `ILegalDocumentRepository` instead of `DbContext` directly; it does not own tables.

### Current violations

Observed in this section's service code as of 2026-04-15:

- **Cross-domain `.Include()` calls:**
  - `AdminLegalDocumentService.cs:41` — `.Include(d => d.Team)` (Teams section)
  - `ConsentService.cs:59` — `.Include(d => d.Team)` (Teams section)
  - `ConsentService.cs:65` — `.Include(c => c.DocumentVersion)` (cross-aggregate within section: consent records → legal document aggregate)
  - `ConsentService.cs:181` — `.Include(c => c.DocumentVersion)` (cross-aggregate within section)
- **Cross-section direct DbContext reads:**
  - `AdminLegalDocumentService.cs:58` — reads `_dbContext.Teams` (Teams section; should call `ITeamService`)
  - `ConsentService.cs:108` — reads `_dbContext.Profiles` (Profiles section; should call `IProfileService`)
- **Within-section cross-service direct DbContext reads:**
  - `ConsentService.cs:57` — reads `_dbContext.LegalDocuments` (owned by `AdminLegalDocumentService` / `LegalDocumentSyncService`; should call `ILegalDocumentService` or the future `ILegalDocumentRepository`)
  - `LegalDocumentSyncService.cs:66,94,113,150,164,196` — reads `_dbContext.LegalDocuments` directly; `:184` — reads `_dbContext.DocumentVersions` directly. Sync job should go through `ILegalDocumentRepository` once it exists.
- **Inline `IMemoryCache` usage in service methods:**
  - `LegalDocumentService.cs:44,62,69` — inline `_cache.TryGetValue` / `_cache.Set` around the GitHub fetch. Must move into a caching decorator (`CachingLegalDocumentService`) wrapping `ILegalDocumentService` per §4/§5.
- **Cross-domain nav properties on this section's entities:**
  - `LegalDocument.Team` (→ `Teams`, Teams section) — strip when introducing `ILegalDocumentRepository`.
  - `ConsentRecord.User` (→ `User`, Users/Identity section) — strip when introducing `IConsentRepository`.
  - `ConsentRecord.DocumentVersion` (→ `DocumentVersion`, sibling aggregate) — strip; callers join by `DocumentVersionId` through `ILegalDocumentRepository`.

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- When editing `ConsentService.cs:108`, replace the direct `_dbContext.Profiles` read with an `IProfileService` call (e.g., `GetProfileByUserIdAsync`) — do not propagate the cross-section read further.
- When editing `ConsentService.cs:57` or any `LegalDocuments` read in `ConsentService`, route through `ILegalDocumentSyncService`/`ILegalDocumentService` rather than adding another `_dbContext.LegalDocuments` query.
- When editing `AdminLegalDocumentService.cs:58`, stop reading `_dbContext.Teams`; call `ITeamService` for team lookups and do not add new `.Include(d => d.Team)` calls.
- When editing `LegalDocumentService.cs` GitHub fetch logic, do not add more inline `IMemoryCache` calls — leave the existing three in place as the only caching site so the future `CachingLegalDocumentService` decorator has a single seam to replace.
- Never add `UpdateAsync`/`DeleteAsync` paths for `consent_records` — §12 DB triggers will reject them at runtime. Only `AddAsync` and `GetXxxAsync` are valid on the consent side.
