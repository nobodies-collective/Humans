# Service Role-Marker Taxonomy + Analyzers (SP1)

**Status:** spec / awaiting Peter review · **Branch:** `feat/role-marker-taxonomy` · **Date:** 2026-05-26
**Driving rules:** [`memory/architecture/orchestrator-marker.md`](../../../memory/architecture/orchestrator-marker.md), [`docs/architecture/peters-hard-rules.md`](../../architecture/peters-hard-rules.md), [`memory/architecture/crosscut-purity.md`](../../../memory/architecture/crosscut-purity.md)
**Supersedes:** the deferred "A2 — orchestrator-no-repository analyzer" of [`docs/superpowers/specs/2026-05-25-analyzer-consolidation.md`](2026-05-25-analyzer-consolidation.md)

## Goal

Introduce a role-marker taxonomy for application services so a service's *architectural role* is declared in its type, not inferred from name or namespace — and wire that taxonomy into the analyzers. Three new markers ship here; the rename of the existing marker is **out of scope** (Peter does `IApplicationService → ISectionService` himself, later, in ReSharper).

## The taxonomy

Two **axes**, confirmed with Peter:

1. **Role (exclusive — pick exactly one):**
   - `IApplicationService` *(existing; later renamed `ISectionService`)* — a **Section** service. Owns a lane; may inject its own section's `I*Repository`.
   - `IOrchestrator` — coordinates **≥2 sections** through their public service interfaces. Owns **no** tables, injects **no** repository.

   A service is **one or the other, never both.** This is build-enforced (HUM0027 below), not just documented. `IOrchestrator` is a **sibling** of `IApplicationService`, never a child — inheriting it would grant the own-lane repository access an orchestrator is defined not to have.

2. **Capability (additive — co-exists with a role):**
   - `IFanout` — marks a **fan-out contributor contract**: an interface many sections implement and a single coordinator aggregates.
   - `IInvalidator` — marks a **cache invalidator**. Carried alongside the type's real interface.

   Capability markers are not mutually exclusive with a role: a Section service routinely *is* a fan-out contributor (`ShiftSignupService` is both an `IApplicationService` and an `IUserDataContributor`).

All three new markers are empty interfaces living beside `IApplicationService` in `Humans.Application.Interfaces`.

## Marker 1 — `IOrchestrator`

**Definition.** `public interface IOrchestrator { }` in `Humans.Application.Interfaces`. Already fully designed in [`orchestrator-marker.md`](../../../memory/architecture/orchestrator-marker.md); this spec only adds the type + analyzer.

**What it marks.** Genuine orchestrators — services that coordinate ≥2 sections, own no tables, inject no repository. The roster is **derived empirically** (services that take only other services / fan-out contracts in their constructor, no `I*Repository`, no `HumansDbContext`) and **approved by Peter** before tagging. Starting candidates from the current code: `GdprExportService`, `EarlyEntryService`, `OnboardingService`. Explicitly **not** `AgentService` — it owns `agent_*` and injects `IAgentRepository`, so it stays a Section (the design-rules §15i "orchestrator" label on it is wrong).

**Topology this unlocks** (Peter's point, recorded for SP3): an orchestrator *is* the section service for a **table-less section** — `controller → IOrchestrator → other sections' services`, no repository in the chain. This lets Onboarding be its own vertical instead of squatting in Users/Profiles. Extracting OnboardingSection is **SP3**, not this spec; SP1 only ships the marker + analyzer that make the topology expressible and enforced.

**Analyzer (one class, two diagnostics).** Runs in the `Humans.Application` compilation only. For each concrete class implementing `IOrchestrator` (transitively):

| Id | Rule | Severity |
|----|------|----------|
| **HUM0026** | An `IOrchestrator` implementer's constructor injects an `I*Repository` (or `HumansDbContext`/`IDbContextFactory`). Orchestrators own no lane. | Error |
| **HUM0027** | A type implements **both** `IOrchestrator` and `IApplicationService`. The role axis is exclusive. | Error |

Both are expected **clean** on first run — a genuine orchestrator already injects no repository and carries no `IApplicationService`. If the empirical pass surfaces a violator, that means the candidate is mislabeled (it's actually a Section) → fix the label, do **not** grandfather. No grandfathering machinery for HUM0026/0027.

## Marker 2 — `IFanout`

**Definition.** `public interface IFanout { }` in `Humans.Application.Interfaces`.

**What it marks.** The fan-out contributor contracts:
- `Humans.Application.Interfaces.Gdpr.IUserDataContributor` → `: IFanout`
- `Humans.Application.Interfaces.EarlyEntry.IEarlyEntryProvider` → `: IFanout`

**Enforcement: none.** Per Peter, `IFanout` is **convenience / searchability / terminology** only — it names the fan-out seam and makes "show me every fan-out contract / every contributor" a one-click search. No analyzer, no test. (Contract purity — read-only, DTO-not-entity returns — is already covered by the existing entity-return ratchet and is not duplicated here.) If a future fan-out contract is added, extend it from `IFanout`; nothing forces this, by design.

## Marker 3 — `IInvalidator`

**Definition.** `public interface IInvalidator { }` in `Humans.Application.Interfaces`.

**What it marks.** The whole `*Invalidator` family — every cache-invalidator interface extends it. Current set (17 today, the grandfather list):

- `Interfaces/Caching/`: `IIssuesBadgeCacheInvalidator`, `ICampLeadJoinRequestsBadgeCacheInvalidator`, `IActiveTeamsCacheInvalidator`, `IConsentCacheInvalidator`, `IVotingBadgeCacheInvalidator`, `INotificationMeterCacheInvalidator`, `ILegalDocumentCacheInvalidator`, `IRoleAssignmentCacheInvalidator`, `IRoleAssignmentClaimsCacheInvalidator`, `INavBadgeCacheInvalidator`
- section-homed: `Tickets/ITicketCacheInvalidator`, `Shifts/IShiftViewInvalidator`, `Shifts/IShiftAuthorizationInvalidator`, `Events/IEventViewInvalidator`, `Camps/ICampInfoInvalidator`, `Users/IUserInfoInvalidator`, `EarlyEntry/IEarlyEntryInvalidator`

(The implementation pass enumerates the exact set from the compilation; this list is the design anchor, cross-checked against the `*Invalidator` interface census.)

**Intent.** A separate invalidator existing **is itself the smell** — same doctrine as the interceptors in the analyzer-consolidation spec and [`crosscut-purity`](../../../memory/architecture/crosscut-purity.md): a standalone invalidator usually means a cross-section write, or a flush the owning section's service + caching decorator should have absorbed. The marker makes the family **countable** so it can be ratcheted toward zero.

**Analyzer (ratchet).**

| Id | Rule | Severity |
|----|------|----------|
| **HUM0028** | An interface extends `IInvalidator` (a new cache-invalidator concept). | **Error**, downgraded to **Warning** for interfaces carrying `[Grandfathered("HUM0028", reason)]` |

- Diagnostic fires at the **interface declaration** that extends `IInvalidator` — that's where a new invalidator concept is born.
- **Prerequisite:** `GrandfatheredAttribute` is `[AttributeUsage(AttributeTargets.Class)]` today; widen it to `AttributeTargets.Class | AttributeTargets.Interface` so the existing `*Invalidator` interfaces can carry the grandfather. (`GrandfatheredCheck.EffectiveSeverity` already operates on `INamedTypeSymbol`, so it resolves interface symbols unchanged.)
- Every existing `*Invalidator` interface gets `[Grandfathered("HUM0028", "<why it exists today>")]` → rides as a visible Warning, not a build break.
- A **new** `*Invalidator` (no `[Grandfathered]`) → Error, failing the PR that adds it. This forbids growth and forces the count down: to add an invalidator you must either delete an existing one or get Peter to sign off a new grandfather.
- Add `HUM0028` to `Directory.Build.props` `WarningsNotAsErrors` with a comment + exit condition (delete the line when the last `[Grandfathered("HUM0028")]` is gone). Matches the **Shape 2** severity discipline of the consolidation spec.

## Marker placement & wiring summary

| Marker | Lives in | Applied to | Analyzer | Severity model |
|--------|----------|-----------|----------|----------------|
| `IOrchestrator` | `Humans.Application.Interfaces` | genuine orchestrators (roster → Peter) | HUM0026 (no repo), HUM0027 (xor `IApplicationService`) | Error, no grandfather |
| `IFanout` | `Humans.Application.Interfaces` | `IUserDataContributor`, `IEarlyEntryProvider` | none | — |
| `IInvalidator` | `Humans.Application.Interfaces` | all `*Invalidator` interfaces | HUM0028 (ratchet) | Error + `[Grandfathered]`→Warning |

## Out of scope

- **The rename** `IApplicationService → ISectionService` — Peter does it himself later. SP1 builds against the current name; the markers are additive new files, so no collision with the in-flight `chore/analyzer-consolidation` work.
- **SP2 — controller base-class consolidation** (audit-first; its own branch/spec).
- **SP3 — extract OnboardingSection** as a table-less orchestrator-only section (after SP1 lands so the marker exists to tag it).
- **HUM0012 for orchestrators** — whether an `IOrchestrator` homed in a section namespace should get a namespace-location check (analogous to HUM0012 for `IApplicationService`). The atom says "homed ≠ owns," so orchestrators may be homed freely; left unenforced unless Peter wants it. Noted, not built.

## Acceptance criteria

- `IOrchestrator`, `IFanout`, `IInvalidator` exist in `Humans.Application.Interfaces` as empty markers.
- `IUserDataContributor` and `IEarlyEntryProvider` extend `IFanout`.
- `GrandfatheredAttribute` accepts `AttributeTargets.Interface`; every existing `*Invalidator` interface extends `IInvalidator` and carries `[Grandfathered("HUM0028", …)]`.
- The approved orchestrator roster carries `IOrchestrator` (and no longer `IApplicationService`).
- HUM0026, HUM0027, HUM0028 implemented, registered in `AnalyzerReleases.Unshipped.md`, with analyzer tests.
- HUM0026/0027 green with **no** grandfathers; HUM0028 green with the existing family grandfathered and `HUM0028` in `WarningsNotAsErrors`.
- `dotnet build Humans.slnx -v quiet` and `dotnet test Humans.slnx -v quiet` green.
- `orchestrator-marker.md` updated ("to build" → built); `INDEX.md` line for the new marker doctrine.
