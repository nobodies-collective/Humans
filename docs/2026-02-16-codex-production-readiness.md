# Production Readiness Assessment
**Date:** 2026-02-16  
**Author:** Codex

**Overall verdict:** **Not production-ready yet**.  
Critical blockers are present in external access revocation and GDPR deletion completeness.

Context reviewed: `CLAUDE.md` and `todos.md` first, then startup, controllers, data model/configuration, migrations, integrations, and Hangfire jobs.

## 1. Security posture (auth, authorization, input validation, OWASP)
| Severity | Finding | Evidence | Recommendation |
|---|---|---|---|
| **High** | Anonymous admin metadata endpoint exposes DB migration state (`/Admin/DbVersion`). | `src/Humans.Web/Controllers/AdminController.cs:1407`, `src/Humans.Web/Controllers/AdminController.cs:1411` | Require `Admin` authorization or disable in production behind environment gate. |
| **High** | Health and metrics endpoints are public, exposing dependency status and service internals. | `src/Humans.Web/Program.cs:353`, `src/Humans.Web/Program.cs:363`, `src/Humans.Web/Program.cs:369`, `todos.md:89`, `todos.md:100` | Restrict by auth and/or network policy (ingress allowlist, internal-only scraping). |
| **High** | Stored XSS risk in legal/consent rendering: markdown converted to HTML and emitted raw; CSP allows inline scripts. | `src/Humans.Web/Views/Consent/Review.cshtml:89`, `src/Humans.Web/Views/Consent/Review.cshtml:148`, `src/Humans.Web/Program.cs:330` | Sanitize rendered HTML (allowlist sanitizer) and remove `'unsafe-inline'` from CSP. |
| **Medium** | Admin config page reveals first 3 chars of secrets, increasing blast radius if privileged account is compromised. | `src/Humans.Web/Controllers/AdminController.cs:22`, `src/Humans.Web/Controllers/AdminController.cs:1149`, `src/Humans.Web/Controllers/AdminController.cs:1157` | Show only boolean set/unset for sensitive keys. |
| **Medium** | PII in logs (recipient email + subject) and no redaction policy. | `src/Humans.Infrastructure/Services/SmtpEmailService.cs:326`, `src/Humans.Infrastructure/Services/SmtpEmailService.cs:330`, `todos.md:43` | Add structured redaction/classification policy; avoid direct email addresses in info logs. |
| **Low (strength)** | Core authn/authz posture is generally solid: role claims transformation, membership gating, anti-forgery on POSTs, secure cookies, security headers, global rate limiting. | `src/Humans.Web/Program.cs:103`, `src/Humans.Web/Program.cs:104`, `src/Humans.Web/Program.cs:125`, `src/Humans.Web/Program.cs:218`, `src/Humans.Web/Program.cs:324`, `src/Humans.Web/Program.cs:342`, `src/Humans.Web/Authorization/RoleAssignmentClaimsTransformation.cs:54`, `src/Humans.Web/Authorization/MembershipRequiredFilter.cs:53`, `src/Humans.Web/Controllers/ProfileController.cs:245` | Keep this baseline; tighten only the exposed endpoints and XSS surface. |

**OWASP mapping (most relevant):**  
- **A01 Broken Access Control:** public admin/ops endpoints (`AdminController.cs:1407`, `Program.cs:353`, `Program.cs:369`).  
- **A05 Security Misconfiguration:** CSP includes `'unsafe-inline'` (`Program.cs:330`).  
- **A03 Injection / XSS:** unsanitized markdown-to-raw-HTML path (`Review.cshtml:89`, `Review.cshtml:148`).  
- **A09 Logging/Monitoring Failures:** plaintext PII in logs (`SmtpEmailService.cs:326`, `todos.md:43`).

## 2. Data integrity (constraints, migrations, GDPR compliance)
| Severity | Finding | Evidence | Recommendation |
|---|---|---|---|
| **Critical** | GDPR anonymization appears incomplete and external access revocation is not guaranteed during deletion flow. | `src/Humans.Infrastructure/Jobs/ProcessAccountDeletionsJob.cs:157`, `src/Humans.Infrastructure/Jobs/ProcessAccountDeletionsJob.cs:166`, `src/Humans.Domain/Entities/Profile.cs:89`, `src/Humans.Domain/Entities/Profile.cs:95`, `src/Humans.Domain/Entities/Profile.cs:101`, `src/Humans.Domain/Entities/Profile.cs:113`, `src/Humans.Infrastructure/Jobs/ProcessAccountDeletionsJob.cs:179`, `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs:592` | Expand anonymization to all profile PII fields and add explicit external deprovisioning guarantees in deletion workflow. |
| **High** | Role assignment overlap prevention is app-layer only; DB-level exclusion constraint still missing. | `src/Humans.Infrastructure/Services/RoleAssignmentService.cs:20`, `src/Humans.Infrastructure/Data/Configurations/RoleAssignmentConfiguration.cs:13`, `src/Humans.Infrastructure/Data/Configurations/RoleAssignmentConfiguration.cs:43`, `todos.md:27`, `todos.md:28` | Add PostgreSQL exclusion constraint for active interval overlap. |
| **Medium** | Consent submit path validates `DocumentVersion` existence but not full required-scope/effective-state at submission point. | `src/Humans.Web/Controllers/ConsentController.cs:77`, `src/Humans.Web/Controllers/ConsentController.cs:101`, `src/Humans.Web/Controllers/ConsentController.cs:209`, `src/Humans.Web/Controllers/ConsentController.cs:246` | Re-validate document required/team scope/effective window in POST submit handler. |
| **Medium** | Team join request table has index on `(TeamId,UserId,Status)` but no uniqueness constraint for pending duplicates. | `src/Humans.Infrastructure/Data/Configurations/TeamJoinRequestConfiguration.cs:47` | Add partial unique index for pending state. |
| **Low (strength)** | Strong integrity controls exist: consent uniqueness + immutable consent/audit triggers + team member uniqueness + startup migrations. | `src/Humans.Infrastructure/Data/Configurations/ConsentRecordConfiguration.cs:39`, `src/Humans.Infrastructure/Migrations/20260212152552_Initial.cs:986`, `src/Humans.Infrastructure/Migrations/20260212152552_Initial.cs:1014`, `src/Humans.Infrastructure/Data/Configurations/TeamMemberConfiguration.cs:35`, `src/Humans.Web/Program.cs:390` | Preserve these controls; extend them to role overlap and join-request duplicates. |

## 3. Operational readiness (logging, monitoring, health checks, error handling)
| Severity | Finding | Evidence | Recommendation |
|---|---|---|---|
| **High** | Readiness details are externally exposed via public health/metrics routes. | `src/Humans.Web/Program.cs:353`, `src/Humans.Web/Program.cs:365`, `src/Humans.Web/Program.cs:369`, `todos.md:100` | Keep detailed health JSON internal; expose only liveness externally. |
| **Medium** | Monitoring foundation exists, but unresolved telemetry gaps remain in TODOs. | `src/Humans.Web/Program.cs:141`, `src/Humans.Web/Program.cs:159`, `src/Humans.Web/Program.cs:345`, `todos.md:17` | Close custom metrics TODO before go-live and define alert thresholds. |
| **Medium** | Critical-path integration tests are missing. | `tests/Humans.Integration.Tests/Humans.Integration.Tests.csproj:4`, `tests/Humans.Integration.Tests/Humans.Integration.Tests.csproj:8`, `todos.md:48` | Add integration tests for onboarding, consent, suspension, deletion, and outbox processing. |
| **Low (strength)** | Error-handling and runtime health basics are present (global exception handler, HSTS, Docker HEALTHCHECK). | `src/Humans.Web/Program.cs:297`, `src/Humans.Web/Program.cs:299`, `Dockerfile:48` | Keep as baseline; add alerting/on-call runbooks. |

## 4. Configuration and secrets management
| Severity | Finding | Evidence | Recommendation |
|---|---|---|---|
| **High** | Committed default DB credential in base appsettings increases accidental misdeployment risk. | `src/Humans.Web/appsettings.json:3` | Remove real/default credentials from committed appsettings; use env/secret store only. |
| **Medium** | Startup fail-fast is explicit for Google workspace credentials in production, but not symmetric for SMTP/GitHub paths. | `src/Humans.Web/Extensions/InfrastructureServiceCollectionExtensions.cs:39`, `src/Humans.Web/Extensions/InfrastructureServiceCollectionExtensions.cs:41`, `src/Humans.Web/Extensions/InfrastructureServiceCollectionExtensions.cs:52`, `src/Humans.Web/Program.cs:115`, `src/Humans.Web/Program.cs:117` | Add options validation on startup for SMTP and GitHub critical keys. |
| **Low (strength)** | Secret hygiene has reasonable scaffolding (.gitignore patterns and required env var in compose). | `.gitignore:92`, `.gitignore:95`, `.gitignore:104`, `docker-compose.yml:10`, `docker-compose.yml:27`, `.env.example:9` | Keep this; back it with a managed secret provider in production. |

## 5. External integration resilience (Google APIs, SMTP, GitHub)
| Severity | Finding | Evidence | Recommendation |
|---|---|---|---|
| **Critical** | Google access removal paths are effectively disabled; sync is add-only. | `CLAUDE.md:46`, `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs:325`, `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs:592`, `src/Humans.Web/Extensions/RecurringJobExtensions.cs:13` | Re-enable safe remove operations with dry-run + audit + rollback controls. |
| **High** | Google Group behavior settings are defined but not applied during provisioning. | `src/Humans.Infrastructure/Configuration/GoogleWorkspaceSettings.cs:49`, `src/Humans.Infrastructure/Configuration/GoogleWorkspaceSettings.cs:66`, `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs:245`, `todos.md:32`, `todos.md:101` | Apply group settings immediately after group creation and verify via reconciliation test. |
| **Medium** | Resilience patterns (retry/backoff/circuit breaker) are not evident around SMTP/Google/GitHub API calls despite Polly package present. | `Directory.Packages.props:37`, `src/Humans.Infrastructure/Services/SmtpEmailService.cs:312`, `src/Humans.Infrastructure/Services/SmtpEmailService.cs:323`, `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs:245`, `src/Humans.Infrastructure/Services/LegalDocumentSyncService.cs:321` | Add bounded retries with jitter and circuit breaking for transient failures. |
| **Medium** | Drive Activity monitor still has unresolved actor identity quality (`people/` IDs). | `src/Humans.Infrastructure/Services/DriveActivityMonitorService.cs:198`, `todos.md:20`, `todos.md:21` | Implement People API resolution and fallback mapping cache. |
| **Low (strength)** | External dependency health checks are implemented (SMTP/GitHub/Google). | `src/Humans.Web/Program.cs:168`, `src/Humans.Web/Program.cs:169`, `src/Humans.Web/Program.cs:170`, `src/Humans.Web/Program.cs:171` | Keep checks; add alert routing and SLOs. |

## 6. Background job reliability (Hangfire)
| Severity | Finding | Evidence | Recommendation |
|---|---|---|---|
| **High** | Google reconciliation/sync jobs are disabled, leaving drift and stale access unmanaged. | `src/Humans.Web/Extensions/RecurringJobExtensions.cs:13`, `src/Humans.Web/Extensions/RecurringJobExtensions.cs:22`, `CLAUDE.md:46` | Re-enable with safety rails and staged rollout. |
| **High** | Outbox processor reads pending events without explicit claim/lock step; overlap can duplicate work. | `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:41`, `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:42`, `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:52`, `src/Humans.Web/Extensions/RecurringJobExtensions.cs:51` | Add transactional claim (`FOR UPDATE SKIP LOCKED` or status transition) before processing. |
| **Medium** | Retry policy caps attempts but no dead-letter queue/escalation path. | `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:17`, `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:83`, `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:90` | Add dead-letter state + alerting for exhausted retries. |
| **Medium** | Job-level success metric can hide per-item failures in same run. | `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:82`, `src/Humans.Infrastructure/Jobs/ProcessGoogleSyncOutboxJob.cs:98` | Emit run result based on aggregate failure count (success/partial/failure). |
| **Medium** | Several jobs rethrow on outer catch, so one integration failure can fail entire run. | `src/Humans.Infrastructure/Jobs/SendReConsentReminderJob.cs:60`, `src/Humans.Infrastructure/Jobs/SendReConsentReminderJob.cs:121`, `src/Humans.Infrastructure/Jobs/SendReConsentReminderJob.cs:125`, `src/Humans.Infrastructure/Jobs/DriveActivityMonitorJob.cs:51`, `src/Humans.Infrastructure/Jobs/DriveActivityMonitorJob.cs:55` | Add per-item isolation and continue-on-error where safe. |
| **Low (strength)** | Hangfire dashboard is admin-gated outside development. | `src/Humans.Web/Program.cs:372`, `src/Humans.Web/HangfireAuthorizationFilter.cs:16` | Keep as-is. |

## 7. Known gaps and risks (from `todos.md`)
| Severity | Gap/Risk | Evidence |
|---|---|---|
| **High** | DB-level active role overlap constraint deferred. | `todos.md:27`, `todos.md:28` |
| **High** | GDPR export scope decision for `AdminNotes` unresolved. | `todos.md:39`, `todos.md:40` |
| **High** | PII logging redaction/classification not defined. | `todos.md:43` |
| **Medium** | No integration tests for critical paths. | `todos.md:48` |
| **Medium** | GDPR export endpoint rate-limiting gap. | `todos.md:52` |
| **Medium** | Drive Activity actor mapping unresolved (`people/` IDs). | `todos.md:20`, `todos.md:21` |
| **Medium** | Google group settings application gap. | `todos.md:32` |
| **Low (accepted risk)** | Public `/metrics` and health endpoints accepted temporarily. | `todos.md:89`, `todos.md:100` |
| **Low (decision conflict)** | `todos` says anonymization is sufficient, but code review indicates remaining PII fields and disabled external revocation paths. | `todos.md:99`, `src/Humans.Infrastructure/Jobs/ProcessAccountDeletionsJob.cs:157`, `src/Humans.Domain/Entities/Profile.cs:95`, `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs:592` |

## Go-live blockers to close first
1. Re-enable and validate **safe Google revocation** flows (group/resource removals + reconciliation jobs).  
2. Complete **GDPR deletion** anonymization/deprovisioning coverage and document legal basis for retained fields.  
3. Lock down **public operational endpoints** (`/Admin/DbVersion`, `/health*`, `/metrics`).  
4. Remove markdown raw-render XSS path and tighten CSP.  
5. Harden outbox/job reliability (claim-locking, dead-letter, partial-failure metrics, integration tests).