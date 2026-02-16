# Production Readiness Assessment: Profiles.net
**Date:** 2026-02-16  
**Author:** Gemini  
**Project:** Profiles.net (Humans)

## Executive Summary
Profiles.net is a membership management system for Nobodies Collective. It is built on a modern stack (ASP.NET Core 10, EF Core, PostgreSQL, Hangfire) and follows clean architecture principles. This assessment identifies several critical and high-severity gaps that should be addressed before full production deployment, particularly around PII logging, rate limiting, and database constraints.

---

## 1. Security Posture

### 1.1 Authentication & Authorization
*   **Status:** Good
*   **Finding:** Authentication is robustly handled via Google OAuth. Authorization is enforced through a combination of global filters (`MembershipRequiredFilter.cs`), role-based attributes, and a custom `RoleAssignmentClaimsTransformation.cs` that dynamically maps active role assignments to claims.
*   **Risk:** `RoleAssignmentClaimsTransformation` queries the database on every authenticated request. While acceptable at the target scale (~500 users), it represents a potential performance bottleneck and increases database load unnecessarily. (Ref: `todos.md` G-09)

### 1.2 PII Logging (Severity: High)
*   **Finding:** Structured logs include personally identifiable information (PII) such as email addresses in plaintext.
    *   `SmtpEmailService.cs:238`: Logs recipient email address on every sent email.
    *   `ProfileController.cs:405`: Logs email address when sending verification links.
*   **Risk:** GDPR non-compliance. Logs often have different retention and access policies than the primary database. PII in logs should be redacted or hashed. (Ref: `todos.md` P2-09)

### 1.3 Rate Limiting (Severity: Medium)
*   **Finding:** Global rate limiting is implemented (100 requests/minute per partition), but specific high-value endpoints lack granular protection.
    *   `ProfileController.cs:DownloadData()` (GDPR Data Export) explicitly notes the lack of rate limiting: "implement rate limiting if needed".
*   **Risk:** Denial of Service (DoS) or scraping of entire member datasets by a compromised or malicious account. (Ref: `todos.md` P1-04)

---

## 2. Data Integrity

### 2.1 Audit Trail & Immutability
*   **Status:** Excellent
*   **Finding:** `consent_records` and `audit_log` tables are protected by database triggers that prevent `UPDATE` and `DELETE` operations, ensuring a tamper-proof audit trail for GDPR compliance. (Ref: `Initial` migration)

### 2.2 Constraints & Validations (Severity: Medium)
*   **Finding:** DB-level exclusion constraints for role assignments are missing.
    *   `RoleAssignmentConfiguration.cs` only has a simple check constraint for the time window.
*   **Risk:** Potential for overlapping active role assignments if application-layer validation is bypassed or fails. (Ref: `todos.md` P1-09)

### 2.3 GDPR Compliance
*   **Status:** Strong
*   **Finding:** Right-to-access (Export) and Right-to-erasure (30-day grace period deletion) are fully implemented. Anonymization is used for deleted accounts.

---

## 3. Operational Readiness

### 3.1 Logging & Monitoring
*   **Finding:** Serilog and OpenTelemetry are configured.
*   **Issue:** `HumansMetricsService.cs` is implemented and emitting counters/gauges, but `todos.md` (#26) indicates it may not be appearing as expected in the `/metrics` endpoint or requires additional callbacks.
*   **Issue:** Structured logs lack redaction policy (see 1.2).

### 3.2 Health Checks
*   **Status:** Strong
*   **Finding:** Comprehensive health checks cover PostgreSQL, Hangfire, SMTP, GitHub, and Google Workspace connectivity. `ConfigurationHealthCheck.cs` ensures all required secrets are present at startup.

### 3.3 Error Handling
*   **Finding:** Global exception handling and status code re-execution are configured in `Program.cs`. `GoogleWorkspaceSyncService.cs` correctly handles specific API errors (409, 400).

---

## 4. Configuration & Secrets

### 4.1 Management
*   **Finding:** Secrets are managed via `IConfiguration`, allowing for Environment Variables or User Secrets.
*   **Security:** `Program.cs` throws an `InvalidOperationException` at startup if critical Google credentials are missing in Production mode.

### 4.2 Data Protection
*   **Finding:** Data Protection keys are persisted to the database, ensuring auth cookies remain valid across container restarts. (Ref: `AddDataProtectionKeys` migration)

---

## 5. External Integration Resilience

### 5.1 Google Workspace Sync
*   **Status:** Strong (Add-only)
*   **Finding:** Uses the Outbox pattern with `ProcessGoogleSyncOutboxJob` to handle retries (up to 10 attempts).
*   **Risk:** Member removal from Google Groups and Drive is currently disabled/stubbed. This represents a security gap where former members or leads may retain access until manual intervention. (Ref: `GoogleWorkspaceSyncService.cs`)

### 5.2 GitHub Integration
*   **Finding:** Used for legal document versioning. Connectivity is verified via health check.

---

## 6. Background Jobs

### 6.1 Reliability
*   **Finding:** Hangfire is used with PostgreSQL storage. Outbox pattern ensures transactional consistency between DB changes and external sync operations.
*   **Issue:** Background jobs currently lack culture context, leading to English-only content for non-English users in job-triggered emails. (Ref: `todos.md` #25)

---

## Summary of Critical Gaps & Risks

| ID | Severity | Category | Description |
|---|---|---|---|
| **S-01** | **High** | Security/GDPR | PII (emails) logged in plaintext in structured logs. |
| **S-02** | **High** | Security | Google resource removal is disabled; access remains until manual sync. |
| **D-01** | **Medium** | Data Integrity | Missing DB-level exclusion constraint for overlapping roles. |
| **O-01** | **Medium** | Operations | GDPR export lacks rate limiting. |
| **O-02** | **Low** | Operations | N+1 queries and over-fetching in Admin controllers. |
| **O-03** | **Low** | Operations | Background jobs lack localization context for emails. |

## Recommendation
Address **S-01** (PII Redaction) and **O-01** (Export Rate Limiting) immediately. Validate the **S-02** removal logic in a staging environment before enabling automatic removals to close the access control gap.
