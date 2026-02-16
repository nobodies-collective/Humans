# Production Readiness Assessment

**Date:** 2026-02-16
**Assessed by:** Claude Opus 4.6
**Project:** Nobodies Humans (membership management system)
**Stack:** ASP.NET Core 10, EF Core 10, PostgreSQL 16, Hangfire, Google Workspace

---

## Executive Summary

The Humans application is in strong shape for a small nonprofit deployment (~500 users). Security fundamentals are solid: Google OAuth with no password surface, complete CSRF protection, proper authorization checks, and anti-forgery tokens on every POST endpoint. GDPR compliance is well-architected with immutable consent records enforced by database triggers. The main gaps are: (1) incomplete personal data anonymization during account deletion, (2) zero integration tests for critical compliance paths, and (3) several operational hardening items typical of a pre-launch system.

**Overall Assessment:** Ready for production with documented risk acceptance for the items below.

**Finding Counts:** 0 Critical | 2 High | 14 Medium | 11 Low

---

## 1. Security Posture

### S-01: Authentication Surface (Low Risk)

**Severity: Low**
**File:** `src/Humans.Web/Program.cs` (lines 113-131)

Authentication is Google OAuth only -- no password storage, no password reset flows, no brute-force attack surface. This is an excellent design choice for a small org.

One minor observation: the Google OAuth `ClientId` and `ClientSecret` are read from configuration at startup. If these values are missing, the app starts but Google login fails silently. The `ConfigurationHealthCheck` (`src/Humans.Web/Health/ConfigurationHealthCheck.cs`) does validate `Authentication:Google:ClientId` and `ClientSecret` presence, which mitigates this.

### S-02: Authorization Model (Low Risk)

**Severity: Low**
**Files:**
- `src/Humans.Web/Authorization/RoleAssignmentClaimsTransformation.cs`
- `src/Humans.Web/Authorization/MembershipRequiredFilter.cs`

The claims transformation runs on every authenticated request (line 24-72 of `RoleAssignmentClaimsTransformation.cs`), querying `RoleAssignments` with temporal validity (`ValidFrom`/`ValidTo`) and checking Volunteers team membership. This is correct but performs 2-3 DB queries per request.

At ~500 users this is fine, but the marker claim pattern (line 33, `HumansClaimProcessed`) prevents re-processing within a single request pipeline, which is good.

The `MembershipRequiredFilter` (line 29-67) has a well-defined exemption list for controllers that must be accessible pre-membership (Home, Account, Application, Consent, Profile, Admin, Human, Language). Board/Admin users bypass the membership check entirely.

### S-03: CSRF Protection (Pass)

**Severity: Pass**
**Files:** All controllers in `src/Humans.Web/Controllers/`

Every `[HttpPost]` action across all controllers has `[ValidateAntiForgeryToken]`. I counted 41 POST endpoints and 41 anti-forgery decorations. Complete coverage.

### S-04: Input Validation and XSS (Low Risk)

**Severity: Low**
**Files:**
- `src/Humans.Web/Controllers/ProfileController.cs` (lines 180-220, picture upload)
- `src/Humans.Infrastructure/Services/SmtpEmailService.cs` (HTML encoding throughout)

Profile picture upload validates: file size (20MB max), content type allowlist (JPEG, PNG, WebP), and uses SkiaSharp to decode and re-encode the image (which strips embedded payloads). Email templates use `HtmlEncoder.Default.Encode()` for all user-supplied content.

Razor views use `@` syntax which auto-encodes by default. No `@Html.Raw()` usage found outside of intentional markdown rendering.

### S-05: Security Headers (Medium Risk)

**Severity: Medium**
**File:** `src/Humans.Web/Program.cs` (lines 216-233)

Security headers are applied:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy` restricting camera, microphone, geolocation
- `Content-Security-Policy` with `script-src 'self' 'unsafe-inline'`

**Finding:** `unsafe-inline` in CSP weakens XSS protection. For a Razor/server-rendered app with limited JS, moving to nonce-based CSP would be ideal but is low priority given the small attack surface.

**Missing:** `Strict-Transport-Security` (HSTS) header is not explicitly set in the middleware. ASP.NET Core's `UseHsts()` may handle this, but it was not observed in the middleware pipeline in `Program.cs`. The Coolify reverse proxy may add this.

### S-06: Rate Limiting (Medium Risk)

**Severity: Medium**
**File:** `src/Humans.Web/Program.cs` (lines 96-110)

A global rate limiter is configured: fixed window, 100 requests/minute per authenticated user (falls back to IP for anonymous). This is reasonable but:

- No per-endpoint rate limiting for sensitive operations (data export, login, consent submission)
- The GDPR data export endpoint (`ProfileController.DownloadData`) has no additional throttle beyond the global 100/min

At ~500 users with Google OAuth (no password login), the risk is low, but a dedicated rate limit on `/Profile/DownloadData` would be prudent.

### S-07: Secrets Management (Medium Risk)

**Severity: Medium**
**Files:**
- `src/Humans.Web/appsettings.json` (blank credential placeholders)
- `src/Humans.Web/Controllers/AdminController.cs` (line ~1350, configuration display)
- `.env.example`

Secrets are provided via environment variables in production (Docker/Coolify), which is correct. The `appsettings.json` file contains blank placeholders, not actual secrets.

The Admin configuration page (`AdminController.Configuration`) displays the first 3 characters of secret values followed by asterisks. This is a reasonable compromise for debugging but does leak partial secret content to Board/Admin users.

### S-08: Hangfire Dashboard (Low Risk)

**Severity: Low**
**File:** `src/Humans.Web/HangfireAuthorizationFilter.cs`

The Hangfire dashboard is restricted to authenticated users with the `Admin` role. This is appropriate.

### S-09: LocalRedirect for OAuth Callback (Pass)

**Severity: Pass**
**File:** `src/Humans.Web/Controllers/AccountController.cs` (line 35)

The login callback uses `LocalRedirect(returnUrl)` which prevents open redirect attacks.

---

## 2. Data Integrity and GDPR Compliance

### D-01: Consent Record Immutability (Pass)

**Severity: Pass**
**Files:**
- `src/Humans.Infrastructure/Migrations/20260212152552_Initial.cs` (lines 600-630)
- `src/Humans.Infrastructure/Data/Configurations/ConsentRecordConfiguration.cs`

PostgreSQL triggers (`prevent_consent_record_update`, `prevent_consent_record_delete`) raise exceptions on UPDATE and DELETE, enforcing append-only semantics. The EF Core configuration adds a unique index on `(UserId, DocumentVersionId)` preventing duplicate consent records.

### D-02: Audit Log Immutability (Pass)

**Severity: Pass**
**File:** `src/Humans.Infrastructure/Migrations/20260212152552_Initial.cs` (lines 632-660)

Same trigger pattern as consent records. The `audit_log` table prevents UPDATE and DELETE at the database level.

### D-03: Data Export Completeness (Low Risk)

**Severity: Low**
**File:** `src/Humans.Web/Controllers/ProfileController.cs` (lines 692-790, `DownloadData` method)

The GDPR data export includes: profile, contact fields, applications, consents, team memberships, role assignments, user emails, and emergency contacts. This covers the core personal data.

**Finding:** `AdminNotes` from the Profile entity is intentionally excluded from the export. Under GDPR Article 15, the right of access applies to all personal data. Admin notes about a member could be considered personal data. This is a judgment call but should be documented as a deliberate decision.

### D-04: Right to Erasure Implementation (Medium Risk)

**Severity: Medium**
**File:** `src/Humans.Web/Controllers/ProfileController.cs` (lines 600-650)

The deletion flow uses a 30-day grace period with cancellation support. `DeletionRequestedAt` and `DeletionScheduledFor` fields track the request. The `ProcessAccountDeletionsJob` handles actual anonymization after the grace period. The flow is well-designed.

### D-05: Anonymization Completeness (HIGH)

**Severity: HIGH**
**File:** `src/Humans.Infrastructure/Jobs/ProcessAccountDeletionsJob.cs` (lines 124-192)

The `AnonymizeUserAsync` method anonymizes many fields but **misses several personal data fields**:

1. **`EmergencyContactName`** (Profile entity) -- contains a third party's personal data
2. **`EmergencyContactPhone`** (Profile entity) -- contains a third party's personal data
3. **`EmergencyContactRelation`** (Profile entity) -- describes relationship to third party
4. **`Pronouns`** (Profile entity) -- personal identity data
5. **`DateOfBirth`** (Profile entity) -- sensitive personal data
6. **`ProfilePictureData`** (byte[] on Profile entity) -- biometric-adjacent data
7. **`VolunteerHistoryEntries`** (related entity) -- may contain personal narrative

Emergency contacts are particularly concerning because they contain **another person's** personal data that was shared in confidence.

**Recommendation:** Add clearing of all these fields in `AnonymizeUserAsync`.

### D-06: Cascade Delete Configuration (Low Risk)

**Severity: Low**
**File:** `src/Humans.Infrastructure/Data/Configurations/UserConfiguration.cs`

Properly configured. Cascade for Profile, RoleAssignments, Applications, TeamMemberships, UserEmails. Restrict for ConsentRecords (preserves audit trail).

### D-07: Consent Hash Verification (Pass)

**Severity: Pass**
**File:** `src/Humans.Web/Controllers/ConsentController.cs` (lines 70-80)

Consent records store a SHA-256 hash of the document content at the time of signing.

---

## 3. Operational Readiness

### O-01: Health Checks (Pass)

**Severity: Pass**
**Files:** `src/Humans.Web/Health/` (4 health check classes)

Four health checks: ConfigurationHealthCheck (9 required config keys), SmtpHealthCheck, GitHubHealthCheck (rate limit warning at <100), GoogleWorkspaceHealthCheck.

**Missing:** No explicit database health check.

### O-02: Structured Logging (Pass)

Serilog with structured parameters throughout all jobs and services. Appropriate log level configuration.

### O-03: Metrics and Tracing (Low Risk)

**Severity: Low**
OpenTelemetry with Prometheus exporter. Two beta packages noted.

### O-04: Error Handling in Controllers (Medium Risk)

**Severity: Medium**
Several AdminController actions perform multiple side effects (DB + Google + email) without transactional guarantees. Acceptable for small org but should be documented.

### O-05: Database Migrations at Startup (Medium Risk)

**Severity: Medium**
`context.Database.Migrate()` at startup. No migration timeout. Minor concern at this scale.

---

## 4. Configuration and Secrets Management

### C-01: Environment-Based Configuration (Pass)

Standard ASP.NET Core pattern with environment-specific overrides.

### C-02: Fail-Fast for Missing Google Credentials (Pass)

Production fails immediately without Google credentials. Dev/staging uses stubs.

### C-03: Sensitive Data Logging Guard (Pass)

`EnableSensitiveDataLogging()` guarded by `IsDevelopment()`.

### C-04: Data Protection Key Storage (Medium Risk)

**Severity: Medium**
Keys stored in DB. No explicit rotation policy (90-day default is acceptable).

---

## 5. External Integration Resilience

### E-01: Google Workspace Sync (Medium Risk)

**Severity: Medium**
Good error handling (409, 404 gracefully handled). Permission removal intentionally disabled. Google Group settings defined but never applied to groups.

### E-02: Outbox Pattern for Google Sync (Medium Risk)

**Severity: Medium**
Well-implemented outbox with batch processing and retry. **No exponential backoff** -- failed events retried at same interval regardless of failure count.

### E-03: GitHub Integration (Low Risk)

GovernanceController creates standalone GitHubClient (inconsistent but functional).

### E-04: SMTP Email Service (Medium Risk)

**Severity: Medium**
Connection-per-email pattern. Does not set culture per recipient for localized emails.

### E-05: Google Workspace Health Check (Low Risk)

Read-only health check, no side effects.

---

## 6. Background Job Reliability

### J-01: Job Error Handling Pattern (Medium Risk)

Consistent try/catch with metrics recording across all jobs. Per-event/per-user error handling in batch jobs prevents one failure from stopping the batch.

### J-02: Job Idempotency (Medium Risk)

**Severity: Medium**
Most jobs are idempotent. `SuspendNonCompliantMembersJob` could re-send suspension emails if the job fails after emails but before `SaveChangesAsync`.

### J-03: Disabled Jobs (Informational)

Two Google sync jobs intentionally disabled pending manual validation.

### J-04: Batch Save Pattern (Medium Risk)

**Severity: Medium**
Multiple users processed in loop with single `SaveChangesAsync` at end. All-or-nothing for the batch. Acceptable at this scale.

---

## 7. Testing

### T-01: Integration Test Coverage (HIGH)

**Severity: HIGH**
Zero integration tests for critical compliance paths: anonymization, consent immutability, suspension flow, data export completeness. For a GDPR-regulated system, these are essential.

### T-02: No Test for Claims Transformation (Medium Risk)

**Severity: Medium**
No unit tests for the authorization backbone (`RoleAssignmentClaimsTransformation`).

---

## 8. Container and Deployment

### K-01: Docker Configuration (Low Risk)

Best practices: multi-stage build, non-root user, HEALTHCHECK directive.

### K-02: Docker Compose PostgreSQL Exposure (Medium Risk)

**Severity: Medium**
PostgreSQL port 5432 exposed. Should not be exposed in production.

### K-03: Forwarded Headers Configuration (Low Risk)

Correctly configured for single proxy with known IP.

---

## Summary of Findings by Severity

### HIGH (2)

| ID | Finding | Area |
|----|---------|------|
| D-05 | Emergency contacts, pronouns, date of birth, profile picture, and volunteer history not cleared during account anonymization | GDPR |
| T-01 | Zero integration tests for critical compliance paths (anonymization, consent immutability, suspension) | Testing |

### MEDIUM (14)

| ID | Finding | Area |
|----|---------|------|
| S-05 | CSP uses `unsafe-inline`; HSTS header not explicitly configured | Security |
| S-06 | No per-endpoint rate limiting on sensitive operations (data export) | Security |
| S-07 | Admin config page shows first 3 chars of secrets | Security |
| D-04 | Right to erasure flow -- functional but see D-05 for completeness gap | GDPR |
| O-04 | Multi-step controller actions without transactional guarantees | Operations |
| O-05 | Database migration at startup with no timeout | Operations |
| C-04 | No explicit data protection key rotation policy | Configuration |
| E-01 | Google Group settings defined but never applied | Integration |
| E-02 | Outbox retry has no exponential backoff | Integration |
| E-04 | Email service does not set culture per recipient | Integration |
| J-02 | Suspension job could re-send emails on retry after partial failure | Jobs |
| J-04 | Batch save pattern -- all-or-nothing for multi-user operations | Jobs |
| T-02 | No unit tests for claims transformation (authorization backbone) | Testing |
| K-02 | PostgreSQL port exposed in Docker Compose | Deployment |

### LOW (11)

| ID | Finding | Area |
|----|---------|------|
| S-01 | Google OAuth only -- excellent, minimal attack surface | Security |
| S-02 | 2-3 DB queries per request for claims -- fine at scale | Security |
| S-04 | Input validation and XSS prevention solid | Security |
| S-08 | Hangfire dashboard properly restricted to Admin | Security |
| D-03 | AdminNotes excluded from GDPR data export (document the decision) | GDPR |
| D-06 | Cascade delete configuration appropriate for anonymization pattern | GDPR |
| O-03 | Two beta OpenTelemetry packages | Operations |
| E-03 | GovernanceController creates standalone GitHubClient | Integration |
| E-05 | Health check is read-only (good) | Integration |
| K-01 | Docker config follows best practices | Deployment |
| K-03 | Forwarded headers correctly configured for single proxy | Deployment |

---

## Recommended Pre-Launch Actions

### Must-Fix Before Production

1. **Fix D-05 (anonymization completeness):** Add clearing of `EmergencyContactName`, `EmergencyContactPhone`, `EmergencyContactRelation`, `Pronouns`, `DateOfBirth`, and `ProfilePictureData` in `ProcessAccountDeletionsJob.AnonymizeUserAsync`. This is a GDPR compliance gap involving third-party personal data.

### Strongly Recommended

2. **Add integration test for anonymization (T-01):** Create a test that populates every field on a User/Profile entity, runs anonymization, and asserts all PII is nulled or replaced. This prevents regression on D-05.

3. **Add integration test for consent immutability (T-01):** Verify the PostgreSQL triggers prevent UPDATE/DELETE on `consent_records`.

4. **Add exponential backoff to outbox (E-02):** Add a `NextRetryAfter` column to `GoogleSyncOutboxEvents` and skip events whose retry time hasn't arrived.

### Nice to Have

5. Add database health check to `/healthz` (O-01)
6. Add per-endpoint rate limit on `/Profile/DownloadData` (S-06)
7. Add unit tests for `RoleAssignmentClaimsTransformation` (T-02)
8. Document the AdminNotes export exclusion decision (D-03)
9. Remove PostgreSQL port exposure from production Compose (K-02)
10. Set culture per recipient in email service (E-04)

---

## Positive Highlights

- **Immutable audit trail** enforced at the database level with triggers (not just application logic)
- **Outbox pattern** for eventually-consistent Google sync, avoiding distributed transaction complexity
- **Temporal role assignments** with `ValidFrom`/`ValidTo` enabling scheduled governance transitions
- **Complete CSRF coverage** across all 41 POST endpoints
- **Structured logging** throughout all jobs and services with meaningful parameters
- **Health checks** covering all 4 external dependencies
- **Multi-stage Docker build** with non-root user
- **Centralized package management** with TreatWarningsAsErrors enabled
- **Google OAuth only** -- eliminating the entire password management attack surface
- **Consent hash verification** preserving the exact document content at time of signing
- **Intentional feature gating** -- disabling automated Google permission sync until manually validated
