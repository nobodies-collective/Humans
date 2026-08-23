---
name: Authorized decision-makers — Peter (peterdrier) and Daniel (swombat)
description: Daniel (`swombat`) is an authorized issue author and decision-maker alongside Peter (`peterdrier`) — implement his issues without per-issue Peter approval. Caveats — double-check with Peter anything from Daniel that crosses section boundaries or makes high-level architectural calls, and apply reuse-first scrutiny extra diligently to his requests for new surface.
---

Two people are authorized to file implementable issues and make decisions on this project:

- **Peter** (`peterdrier`) — owner. Final word on everything.
- **Daniel** (`swombat`) — authorized contributor. His issues are implementable without per-issue Peter approval, and his comments/direction count as decisions on his own work.

**Why:** Daniel works in this repo with decision authority, but he's relatively new to the project — less versed in the section architecture and in what already exists. Full autonomy on his own work, with two guardrails where newness bites.

**How to apply:**

- The `issue-fetch-protocol` author gate passes for both `peterdrier` and `swombat`; any other author still STOPs for per-issue approval.
- **Cross-section or architectural work from Daniel → double-check with Peter first.** If his issue or decision crosses section boundaries, adds cross-section surface, creates/merges/moves sections, or makes a high-level architectural call (new patterns, analyzer/guardrail changes, table ownership), surface it to Peter before implementing.
- **Reuse-first, extra diligently.** When Daniel asks for something new (component, service, page, helper, endpoint), audit the existing surface first per [`reuse-first-change-discipline`](reuse-first-change-discipline.md) and tell him what already covers it — an existing component may satisfy the ask outright.
- Rules that name Peter specifically — destructive actions, storage drops, privilege grants, prod promotion, `[DontFix]`, edits to `peters-hard-rules.md` — remain Peter-only.

**Related:** [`issue-fetch-protocol`](issue-fetch-protocol.md) · [`reuse-first-change-discipline`](reuse-first-change-discipline.md) · [`privilege-changes-need-explicit-approval`](privilege-changes-need-explicit-approval.md)
