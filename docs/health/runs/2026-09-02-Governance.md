# Governance — section doctor, 2026-09-02

- **Invocation:** unattended daily run, no arguments. Phase 8 (inline round) skipped.
- **Anchor commit:** `f2a1a3fb` (`origin/main`)
- **Branch:** `section-doctor/2026-09-02T071811Z` (cloud run, repo root — no worktree)
- **Budget:** 2.5h, single PR.
- **PR:** peterdrier/Humans#1580

## Assessment summary

First doctor pass over Governance (reforge 626, loc=3888, files=57, cognitive p95=6,
max=14, maxClassLoc=612 `ApplicationDecisionService`) — the median never-doctored section
by score. The target shape
([`health.md`](../../../src/Sections/Humans.Governance/Docs/health.md), written this run
before any scan) finds unrelated things under one roof: the `applications` aggregate
(tier applications → Board votes → Admin finalization → a term to the end of the next odd
year) and a membership-standing calculator that owns no table, shares no entity with the
aggregate, and answers a different question for the rest of the app. That split is real
and load-bearing, not a defect; it is written down so the next reader meets it deliberately.

**The section had live member-facing bugs, and no scan found them.**
The term-expiry date shown on both member-facing pages was recomputed in Razor from
`ResolvedAt` with arithmetic that disagrees with `TermExpiryCalculator` — an application
approved in 2026 renders "31 December 2027" while the stored `TermExpiresAt`, which the
renewal job and the dashboard term card both use, is 2029-12-31. And every approval and
rejection notification pointed at `/Governance/MyApplications`, a route that has never
existed, so the one action link a decided applicant gets is a 404.

Everything else was surface and truth. Dead surface went — a public cross-section contract
method dead end to end, an interface nothing injected, a calculator shortcut with no caller.
Comments said things that were false, and the section's own docs carried a controller, an
architecture test file and service interfaces that do not exist. `authorization.md` and
`data-access.md` had no freshness triggers at all.

Conformance was clean: section-file-layout PASS, `reforge audit-auth` PASS,
`reforge ownership-violations` 0.

## Ranked findings

Value = bug surface removed, then concepts removed, then words removed.

| # | Finding | Value | Disposition |
|---|---|---|---|
| 1 | **Term expiry mis-displayed on both member-facing pages.** `Views/Governance/Index.cshtml` and `Views/Governance/Applications/Index.cshtml` each computed `resolvedYear % 2 == 0 ? +1 : +2` from `ResolvedAt`; `TermExpiryCalculator` computes `today.Year + 2`, bumped to the next odd year. A 2026 approval displayed 2027 against a stored 2029. `UserApplicationSnapshot` already carried `TermExpiresAt`, so the fix carries the stored value through and deletes both copies of the arithmetic — which also removed a hardcoded English "31 December". Tests added at the service seam. | high | **worked** |
| 2 | **`actionUrl: "/Governance/MyApplications"` 404s for every decided applicant.** No such route; the applicant's list is `/Governance/Applications`. | high | **worked** |
| 3 | **`GetApprovedTiersForUserAsync` dead end to end** — public contract, service, repository interface, repository implementation, zero callers in `src/` or `tests/`. Its xmldoc claimed the consent clear-check flow used it; that flow has been annotation-only since the name-only access switch. Reviewer-approved. | high | **worked** |
| 4 | **`IMembershipCalculator` injected by nothing.** Every consumer, in-section and cross-section, goes through `IMembershipCalculatorRead`, and `Section.cs` registered the concrete class behind both. Target §3: nothing only called from inside the section belongs on an interface. The class now carries `IMembershipCalculatorRead, IOrchestrator` directly. Reviewer-approved, with the marker mechanics it named. | med | **worked** |
| 5 | **`MembershipCalculator.HasAnyExpiredConsentsAsync` had no production caller** — a Volunteers-team shortcut over the live `…ForTeamAsync`. Its tests pinned nothing else. Reviewer-approved; the reviewer also corrected the proposal (the member was on the class, not the interface). | med | **worked** |
| 6 | **Dead `"ConcurrencyConflict"` switch arm** in `GovernanceBoardVotingController.Finalize`, plus its resx entries. The service never returns that key and `Application` carries no concurrency token — [`no-concurrency-tokens`](../../../memory/architecture/no-concurrency-tokens.md) says it never will. | med | **worked** |
| 7 | **Hardcoded English `[Display(Name = …)]` on the member-facing Create form.** `Motivation` and `AdditionalInfo` rendered English labels in every culture through empty `<label asp-for>` tags; new `GovernanceCreate_*Label` keys in every supported culture now carry them, matching the Asociado questions beside them. The one on `ConfirmAccuracy` never rendered at all — the view supplies that label — so it was deleted outright. | med | **worked** |
| 8 | **Comments that say something false.** Unresolvable crefs (`Domain.Enums.ApplicationStatus`, `IMembershipCalculator` from the Contracts leaf, `UsersDbContext`); wrong migrations path; wrong table name (`application_state_histories`); a caching decorator, a Board daily digest, an `IProfileService` and an `Application/Details.cshtml` that do not exist; `GetBoardVotingDashboardAsync` attributed to the wrong interface; `BoardVoteRow` claimed to cross into `OnboardingService`; "nine prefixes" for a list that has since grown; `IRecurringJob` attributed to Hangfire; and two stacked `<summary>` blocks that left `ReassignApplicationsToUserAsync` undocumented while its prose documented the member below it. No project sets `GenerateDocumentationFile`, so every broken cref here was silent. | med | **worked** |
| 9 | **Governance.md drift** — a `BoardController` row and prose (`authorization.md` already said no such class exists); an architecture test file at a path that does not exist; `IProfileService` / `IProfileService.GetTierCountsAsync` / `ITeamService` / `IUserService` / `ILegalDocumentSyncService` / `IConsentService` named where the code injects the `…Read` variants; a grandfathering bullet naming retired jobs and claiming consumers read governance tables directly (`reforge ownership-violations` reports zero); "one pending per tier" where the code enforces one per person, any tier. | med | **worked** |
| 10 | **Feature-doc and guide drift** — a board-voting tier filter documented across the feature docs and never built; "stays as Volunteer" on rejection, which changes no tier; "reverts to Volunteer" on lapse, which downgrades to another held tier first; `/Governance/Applications` given as the submit route (it is `/Create`); a "nightly" system-team sync that runs hourly; the wrong policy on `/Users/Admin/Roles`. | med | **worked** |
| 11 | **Freshness triggers.** `authorization.md` and `data-access.md` had no trigger block at all, so nothing could ever flag them; no doc in the section was triggered by the Contracts leaf; and docs listed `Services/TermExpiryCalculator.cs`, already inside the section glob. | med | **worked** |
| 12 | **~150 lines of comment that restate the code or narrate history.** Which lane a file moved in, which PR added a line, what "was already arriving transitively"; and the xmldoc summaries and `<param>` lines that say the member's name back ("Unique identifier for the application", "Approves this application"). Where a provenance clause wrapped a live constraint, the constraint stayed. | low | **worked** |
| 13 | **`humans.asociados` counts approved applications of both tiers** and publishes them as "Approved asociado members" — `GovernanceMetricsService` reads `applicationStats.Approved`, which excludes only Withdrawn. Defensible definitions — people currently holding an active Asociado term; approved Asociado applications ever; the profile tier count the sidebar already shows — and no way to pick from the code. | med | **noted → Needs Peter** |
| 14 | **`Application.ReviewStartedAt` is never set.** Private setter, no writer, yet DTOs and views carry it to the page as a permanent blank. It belongs to the unbuilt request-more-info flow (target §5), so it is reported, not removed. | low | **noted → Needs Peter** |
| 15 | **`RequestMoreInfo` is unreachable.** The state machine permits `Submitted → Submitted` with reviewer notes and `Application` implements it, but no route, service method or UI reaches it. A seam, not dead code — target §5. | low | **noted → Needs Peter** |
| 16 | **Approval and rejection notification titles and bodies are hardcoded English** (`$"Your {tier} application has been approved"`). Not this run's to fix: which culture a notification renders in is a question about the notification path, not about Governance. | low | **sweep queue** |
| 17 | **GDPR erasure is entirely unpinned.** `ScrubFreeTextForUserAsync` is the section's Art. 17 obligation — which free text is cleared, which skeleton is kept, and that Board notes the person wrote about others are cleared too — and no test asserts any of it. The highest-value remaining test gap. | high | **section ledger** |
| 18 | **Audit entries on approve/reject unpinned**, and the vote-overwrite rule (one row per Board member, updated in place) is pinned only at the repository, not through `CastBoardVoteAsync`. | med | **section ledger** |
| 19 | **Assert-the-mock tests.** The `GetRequiredTeamIdsForUserAsync` Colaborador cases and the `HasActiveRolesAsync` pair assert the substitute's own return value. The filtering tests and the user-scoping test duplicate repository coverage at the service. | low | **section ledger** |
| 20 | **Controller authorization attributes unpinned.** The policies themselves are tested; that these controllers and actions carry them is not. | med | **section ledger** |
| 21 | **Unprefixed resx keys** in `GovernanceResource.resx`. Reported, not backfilled — a prefix rename is a rename, not a doctor strike. | low | **no change** |

## Worked

Findings 1–12, one commit per strike:

- `doctor(Governance): show the stored term expiry, not view arithmetic` — finding 1.
- `doctor(Governance): fix the dead decision-notification link, drop two dead paths` — findings 2, 6, 7.
- `doctor(Governance): delete three dead surfaces` — findings 3, 4, 5.
- `doctor(Governance): make the section's comments true` — finding 8.
- `doctor(Governance): make the section's docs true` — findings 9, 10, 11.
- `doctor(Governance): cut comments that restate the code` — finding 12.
- `doctor(Governance): sweep the queue from merged run files` — Phase 5 sweep, not a Governance finding.

Gates: `dotnet build Humans.slnx -v quiet` clean at every strike;
`dotnet test Humans.slnx -v quiet` green across the solution before the deletion
push (`Humans.Integration.Tests` self-skips, by design);
`dotnet format whitespace Humans.slnx --verify-no-changes` clean.

## Skipped

- **Findings 13–15** are Peter's, not a run's: a metric whose correct definition is a
  product question, and two halves of a seam the target shape reserves.
- **Finding 21** left alone: renaming resource keys is a rename.
- **Phase 8** skipped — unattended run.
- Deleting `IMembershipCalculator` was proposed with the wrong mechanics (this run believed
  `HasAnyExpiredConsentsAsync` sat on it); the reviewer corrected that before the strike.

## Retro

**What did the target shape catch that a scan did not?** Both headline bugs and the
interface deletion. `reforge audit-surface` reported neither view's arithmetic nor the dead
notification URL — nothing in a call-graph scan can see that a string literal is not a
route, or that two Razor expressions disagree with a pure function three files away. The
target's §3 line "nothing that is only called from inside the section belongs on an
interface" is what condemned `IMembershipCalculator`; the scan listed it among many
zero-caller symbols without distinguishing it from the deliberate ones.

**What did the run get wrong?** It proposed deleting
`HasAnyExpiredConsentsAsync` from `IMembershipCalculator` when the member exists only on
the concrete class and the interface declares a different, live method one line away — a
name-prefix collision the run would have walked into without the reviewer. And it read
`reforge audit-surface` through `head -70`, which truncated the dead-surface evidence
mid-list; the rerun without the pipe found the rest.

**What cost the most time?** Solution-wide builds — one per strike, at ~3 minutes each,
against a section test project that runs in 2 seconds. That is a large slice of a 2.5h
budget. The scoped-inner-loop rule exists for this and the run under-used it: only the
deletion strike genuinely needed the full-solution gate.

**What should the next run on this section look at?** The queued test gaps, GDPR
erasure first — it is the section's only regulatory obligation and nothing pins it. And
`ApplicationDecisionService` at 612 LOC is the section's largest class; the target shape
blesses it as one aggregate's state machine plus its fan-out, but a run that wanted to
split anything should start by asking whether the notification/email fan-out is part of the
state machine or a listener on it.

## Needs Peter

- [ ] **`humans.asociados` counts the wrong thing** (finding 13). `GovernanceMetricsService`
  publishes `applicationStats.Approved` — every approved application of either tier, all
  years — under the description "Approved asociado members". The defensible definitions:
  people holding an active Asociado term today, approved Asociado applications ever, or the
  profile-tier count the section's own sidebar already computes. Which one does the gauge
  mean? A run cannot pick this from the code.
- [ ] **`Application.ReviewStartedAt` and `RequestMoreInfo`** (findings 14, 15) are two
  halves of one unbuilt flow: the state machine and entity implement a `Submitted →
  Submitted` re-entry carrying reviewer notes, nothing reaches it, and `ReviewStartedAt`
  rides through DTOs to views as a permanent blank. Build the flow, or delete both?
- [ ] **Notification titles and bodies are hardcoded English** (finding 16). Fixing that is
  a decision about which culture a notification renders in, which belongs to the
  notification path rather than to Governance.
- [ ] Amendment from the retro: this run spent a large slice of its budget on solution-wide
  builds it did not need. If the skill's scoped-inner-loop guidance is meant to apply to
  doctor strikes as well as to ordinary work, saying so in Phase 4 would buy back most of that.

## Sweep queue

Findings 17–20 are in-section and went straight to
[`src/Sections/Humans.Governance/Docs/debt.yml`](../../../src/Sections/Humans.Governance/Docs/debt.yml)
this run, not here — the queue carries off-section debt only.

- `debt: Notifications — every Governance decision notification's title and body is a hardcoded English interpolated string (ApplicationDecisionService.ApproveAsync/RejectAsync). The recipient's PreferredLanguage is already used for the matching email, so the notification path is the odd one out; which culture an in-app notification renders in is that section's call, not Governance's (finding 16, /section-doctor on Governance 2026-09-02).`
- `lesson: 2026-09-02 — never pipe a reforge scan through head. audit-surface's output is ordered by symbol, not by value, so a truncated read drops evidence from the middle of the list rather than the tail; this run's first pass lost several zero-caller entries that way. Redirect to a file and grep it.`
- `lesson: 2026-09-02 — before proposing to delete a member, check whether a near-identical name on the same type is the live one. HasAnyExpiredConsentsAsync (dead, concrete class) and HasAnyExpiredConsentsForTeamAsync (live, on the interface, called from ComputeStatusAsync) differ by one word; the run's proposal named the wrong home for the dead one and the reviewer caught it.`
- `lesson: 2026-09-02 — a view that recomputes something a pure function already computes is a bug waiting, not a duplication smell. Two Razor blocks re-derived the term-expiry year from ResolvedAt and disagreed with TermExpiryCalculator by two years; nothing in the build, the tests or any scan could see it, because both sides compile. When a target shape says "one pure function", grep for the arithmetic, not the function name.`

## File coverage

Every file under `src/Sections/Humans.Governance/**` and
`src/Sections/Humans.Governance.Contracts/**` was read by at least one thread, excluding
`obj/**`, `bin/**`, `Data/Migrations/*.Designer.cs` and
`Data/Migrations/GovernanceDbContextModelSnapshot.cs` (generated). The shipped baseline
migration `20260809124929_BaselineGovernance.cs` was read and deliberately left untouched.

Also read: `tests/Humans.Governance.Tests/**`, the section's six `Docs/` files,
`docs/guide/Governance.md`, and the cross-section docs the strikes made false
(`src/Sections/Humans.Onboarding/Docs/Onboarding.md`,
`src/Sections/Humans.Users/Docs/features/profiles.md`,
`docs/architecture/dependency-graph.md`).

## Threads

| Thread | Findings folded in |
|---|---|
| Freshness | 11, and the doc-drift inputs to 9 and 10 |
| Tests | 5, 17–20 |
| History | 12 — false narrations, and cuts with trim-to text |
| Comments | 8 and 12 — the falsehoods, the cuts, and a keeps list the cuts respected |
| Conformance | none — layout PASS, `audit-auth` PASS, `ownership-violations` 0 |
| Inbox | none — `peterdrier/Humans` has open issues #1576, #1562 and #1554, none Governance; no Governance rows in `debt-ledger.yml`; the section had no `Docs/debt.yml` |

Independence check: **pass**. Findings 1, 4, 14 and 15 came from the target shape rather
than from a scan; findings 1 and 2, the highest-value items, came from reading views
and service code by hand, and `reforge audit-surface` reported neither.

## Size

reforge score 626 → 603; loc 3888 at the start of the run. The run removed the dead
surfaces, the comment bulk, and the resx entries behind the dead switch arm; it added the
service-seam tests, the localized Create labels, and the target shape doc. The PR's Surface
Report carries the measured deltas.
