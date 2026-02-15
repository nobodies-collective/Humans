# Pre-Production Refactoring Recommendations

**Date:** 2026-02-15
**Context:** This application is approaching production launch. Some changes will be inherently more difficult to perform once that happens, so the current pre-production window is an opportunity for substantial improvements that won't be available later.

**Methodology:** Full codebase evaluation across all four architecture layers (Domain, Application, Infrastructure, Web), including data model analysis, dependency audit, test coverage assessment, and code quality review.

---

## Codebase Evaluation Summary

**Overall:** Well-structured Clean Architecture with good GDPR compliance, proper domain modeling, and modern .NET 10 stack. The domain layer and infrastructure services are generally well-designed for the stated ~500-user scale. However, the codebase has one major structural problem and two time-sensitive opportunities that are much easier to address before production.

**By the numbers:**

| Metric | Value |
|--------|-------|
| Source code | 23,110 lines across 181 files |
| Controller LOC | 4,508 (93 direct `_dbContext` calls bypassing services) |
| Infrastructure service LOC | 4,940 |
| Hangfire job LOC | 1,063 |
| Test LOC | ~999 (~4% test-to-code ratio; integration test project is empty) |

---

## Recommendation 1: Extract Business Logic from Controllers into Application Services

**Priority: HIGH | Effort: LARGE | Risk if deferred: Compounds over time**

### The Problem

The clean architecture is correctly established — Domain, Application, Infrastructure, Web layers exist with proper interfaces and DI — but the two largest controllers bypass it entirely.

| Controller | LOC | Direct DbContext calls | Constructor dependencies |
|---|---|---|---|
| `AdminController` | 1,528 | 47 | 12 |
| `ProfileController` | 896 | 16 | 12 |
| All others (9) | 2,084 | 30 | — |

`AdminController` alone is larger than any infrastructure service. It queries the database directly, computes membership status, manages role assignments, triggers sync jobs, and handles approval workflows — all inline. `ProfileController` similarly handles image processing, email verification, contact field management, and GDPR export directly.

Meanwhile, the Application layer's `ITeamService` (23 methods) and `IMembershipCalculator` (7 methods) prove the project already knows how to do this right. The controllers just didn't follow the pattern.

### Why Pre-Production

Every post-launch bugfix and feature will add more logic to these controllers, compounding the problem. Right now refactoring can happen fearlessly because there are no active users — no production incidents competing for attention, no "just hotfix the controller" pressure. Once in production, extracting logic from a 1,500-line controller while it's being actively patched creates painful merge conflicts.

### Proposed Approach

Create new application-layer service interfaces and move all DbContext access and business logic into them. Controllers become thin HTTP wiring — parse request, call service, return view.

Possible service boundaries:

| New Service | Extracted From | Responsibility |
|---|---|---|
| `IAdminMemberService` | AdminController | Member listing, detail, approval, suspension |
| `IRoleManagementService` | AdminController | Role assignment CRUD, temporal validation |
| `IProfileEditService` | ProfileController | Profile CRUD, image processing, GDPR export |
| `IEmailVerificationService` | ProfileController | Email verification token flow |

This also unblocks writing integration tests (P2-07 in todos.md), since services are testable but controllers are not.

### Related Items

- G-08 in todos.md: "Centralize admin business logic into services"
- G-07 in todos.md: "AdminController over-fetches data"

---

## Recommendation 2: Add Missing Database Constraints Before Data Exists

**Priority: HIGH | Effort: SMALL | Risk if deferred: Migration failures against production data**

### The Problem

Several data integrity constraints are documented as needed but not yet in the schema. On an empty database, these are trivial single-line migrations. On a production database with real data, each one requires: validate existing data won't violate the constraint, plan a maintenance window, test migration against a production clone, and hope nothing breaks. If existing data *does* violate the constraint, you're stuck doing data cleanup before the migration will even run.

### Specific Constraints

| Constraint | Table | Risk if missing | Pre-prod difficulty | Post-prod difficulty |
|---|---|---|---|---|
| CHECK: exactly one of `team_id`/`user_id` non-null | `google_resources` | Silent orphaned records, null-null resources | One-line migration | Must audit all existing rows first |
| Exclusion constraint on temporal overlap | `role_assignments` | Duplicate active Board roles, overlapping date ranges | One-line + btree_gist extension | Must scan and fix all existing overlaps |
| CHECK: `valid_to > valid_from` | `role_assignments` | Nonsensical backward date ranges | One-line migration | Must find and fix violations |
| Re-enable NU1902/NU1903 vulnerability warnings | `Directory.Packages.props` | Vulnerable transitive deps invisible in builds | Flip a property | Same, but more deps accumulate over time |

### Why Pre-Production

Database constraints are categorically harder to add retroactively. Every other recommendation (service extraction, code splitting) can be done at any time with only developer pain. But constraints against production data can fail migrations, require downtime, and need data cleanup. Right now the tables are empty or have test data. Add every constraint you'll ever want *now*.

### Related Items

- P1-09 in todos.md: "Enforce uniqueness for active role assignments"

---

## Recommendation 3: Decompose GoogleWorkspaceSyncService Before Re-enabling Sync

**Priority: MEDIUM-HIGH | Effort: MEDIUM | Risk if deferred: All-or-nothing sync enablement**

### The Problem

`GoogleWorkspaceSyncService` is 827 LOC with 15 public methods handling four distinct responsibilities: resource provisioning, permission syncing, group membership management, and sync preview/diff. It's currently **disabled** (`SystemTeamSyncJob` and `GoogleResourceReconciliationJob` are commented out in `Program.cs`) because it's too complex to validate end-to-end.

This is the right call — but the reason it's hard to validate is that it's monolithic. You can't turn on "just provisioning" or "just group sync" because they're all woven into one service with shared lazy-initialized API clients and interleaved audit logging.

### Proposed Split

| New Service | Methods | Responsibility |
|---|---|---|
| `IGoogleProvisioningService` | ProvisionTeamFolder, ProvisionUserFolder, ProvisionGroup, UnlinkResource | Creating and removing Google resources |
| `IGooglePermissionSyncService` | SyncResourcePermissions, SyncTeamPermissions, SyncGroupMembers, PreviewSync | Comparing actual vs. expected state and reconciling |
| `IGoogleUserAccessService` | AddUserToResources, RemoveUserFromResources, RestoreUserAccess | Per-user access grant/revoke lifecycle |

Shared concerns (API client initialization, credential loading) can live in a small internal `GoogleApiClientFactory` or similar.

### Why Pre-Production

Google sync *will* need to be enabled after launch — you can't run a membership system without automated provisioning forever. If it's still one service, you face an all-or-nothing toggle. Split services let you enable provisioning first (lowest risk), then group sync, then permission reconciliation, validating each independently. The split also makes each piece small enough to write focused tests for (addressing the empty integration test project, P2-07).

### Related Items

- P1-12 in todos.md: "Google group sync misses paginated results" (easier to fix in a focused service)
- P1-13 in todos.md: "Apply configured Google group settings during provisioning"
- P1-07 in todos.md: "Add transactional consistency for Google sync"

---

## Honorable Mentions

These are already tracked in `todos.md` but are confirmed as worth prioritizing in the pre-production window:

| Item | Why it's time-sensitive |
|---|---|
| **#23: Members to Humans rename** | URL structure, localization keys (~30 keys across 5 locales), and view model names all become quasi-public API contracts once users are active. Renaming is trivial now but painful later. |
| **P2-06: Schedule SendReConsentReminderJob** | Functional gap, not a refactoring item, but a launch blocker — volunteers get suspended without warning reminders. |
| **P2-04: Pin pre-release OpenTelemetry packages** | Two beta packages (`Exporter.Prometheus.AspNetCore` v1.10.0-beta.1, `Instrumentation.EntityFrameworkCore` v1.0.0-beta.12) in a compliance-critical system. Pin to stable or document risk acceptance before the dependency tree is locked in production. |
| **P2-07: Integration tests for critical paths** | The integration test project has TestContainers configured but zero tests. Consent, auth, and GDPR deletion paths have no end-to-end coverage. Easier to write once business logic is extracted from controllers (Recommendation 1). |
