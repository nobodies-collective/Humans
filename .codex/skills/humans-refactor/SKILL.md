---
name: humans-refactor
description: Autonomous Reforge-guided refactor workflow for the Humans repository. Use when the user wants Codex to target a section such as Shifts, Users, Teams, Tickets, Camps, or Store, create a dedicated worktree/branch from origin/main, run iterative architecture-focused improvements, score each stage with Reforge, gate commits through a read-only architecture review, append score and verdict details to commit messages, push progress, and open a PR when the loop reaches stasis.
---

# Humans Refactor

Run a higher-autonomy refactor pass for one Humans section. This is the v2 tech-debt loop: Reforge supplies deterministic score pressure, and a separate architecture-review pass decides whether the score movement was worth the trade.

## Start

1. Confirm the repo root is a Humans checkout.
2. Read the current request carefully for target section, time budget, branch/PR expectations, and any overridden constraints.
3. Fetch `origin/main`, create `refactor/YYYY-MM-DD-<section>-N`, and attach `.worktrees/refactor-YYYY-MM-DD-<section>-N`.
4. Keep all scratch output under `local/refactor-runs/<run-id>/`.
5. Run `reforge stop` before and after every Reforge call. Do not use daemon mode.

If resuming, continue the existing refactor worktree/branch only when the user clearly refers to it.

## Hard Limits

- Avoid database/storage changes unless the user explicitly asks for them: no migrations, no schema configuration changes, no entity persistence-shape changes, no JSON serialization attribute changes.
- Do not revert user changes or unrelated work.
- Do not move debt across sections to win points. Regressions outside the target section count against the change.
- Public members may be removed when they are internal application surface and the call graph is updated. Preserve reflection/external contracts, Razor action routes, serialized DTO contracts, and public APIs that are intentionally consumed outside the repo.
- Prefer the best end-state architecture over the smallest diff. Large cohesive refactors are allowed when the call graph and tests support them.

## Scoring

Capture baseline scores before editing:

```powershell
reforge stop | Out-Null
reforge surface-score --solution Humans.slnx --format Json --all --top-symbols 200 > local\refactor-runs\<run-id>\stage00-all.json
reforge stop | Out-Null
reforge surface-score --solution Humans.slnx --group <Section> --format Json --all --top-symbols 200 > local\refactor-runs\<run-id>\stage00-section.json
reforge stop | Out-Null
```

For each candidate stage, write `stageNN-all.json` and `stageNN-section.json`. Use `scripts/reforge_delta.py` to summarize score movement for commit messages and status updates.

Measure value as:

- target-section total improvement: positive value
- overall total improvement: useful context
- internal-complexity increases: cost unless the architecture-review pass says the trade is justified
- outside-target regressions: high cost
- score-neutral changes: acceptable only when the reviewer says the architecture clearly improved

## Work Loop

Repeat until stasis:

1. Inspect Reforge top symbols/rules for the target section.
2. Read the surrounding code and call graph before editing.
3. Pick the highest-leverage cohesive improvement, not just the highest scoring rule.
4. Make the change.
5. Run targeted tests and `dotnet build Humans.slnx --disable-build-servers -v q`.
6. Run Reforge after the change.
7. Run the architecture-review gate.
8. If accepted, commit and push. If rework/reject, improve or abandon before committing.

Stasis means at least two consecutive candidate ideas are rejected, score-negative without architectural upside, blocked by hard limits, or too speculative to change without user/product input.

## Architecture Review Gate

Use a separate read-only pass after every candidate stage. Prefer a subagent when multi-agent tools are available; otherwise perform an explicit second-pass review without editing. Read [architecture-reviewer.md](references/architecture-reviewer.md) for the prompt and JSON contract.

Inputs to the reviewer:

- target section and stated objective
- git diff for the uncommitted stage
- Reforge before/after deltas, including surface/internal/combined totals
- build/test results
- notes about behavior or ownership assumptions

Reviewer verdicts:

- `accept`: commit/push.
- `rework`: change the patch and rerun tests/Reforge/review.
- `reject`: abandon the patch or redesign before committing.

Treat Reforge as evidence, not judgment. Good consolidation removes duplicated call structures, uses canonical read DTOs, and replaces interfaces with small data where appropriate. Bad consolidation hides domain verbs behind generic action/mode dispatchers, grows god methods, moves complexity to private helpers, or shifts debt out of the target section.

## Commit Messages

Every accepted commit must include a score and review appendix:

```text
<short imperative subject>

Reforge:
- Target: <Section> <before> -> <after> (<delta>); surface <delta>, internal <delta>
- Overall: <before> -> <after> (<delta>); surface <delta>, internal <delta>

Architecture-review:
- Verdict: accept
- Grade: good|neutral
- Score-gaming risk: none|low|medium
- Reason: <one or two concise sentences>

Verification:
- <commands run and result>
```

Push after each accepted commit. If a PR does not exist by the time the user asks or the loop reaches stasis, open a draft PR with cumulative score movement, verification, and any commits the reviewer marked as tradeoffs.

## Judgment Rules

- Prefer explicit domain verbs for independent commands.
- Prefer a real state engine for lifecycle transitions.
- Penalize generic action/mode methods that are neither explicit domain verbs nor a real state engine.
- Treat read/query shape objects differently from mutation mode dispatchers; shaped reads are acceptable when they collapse duplicated include/query shapes without growing unbounded.
- Prefer adding one or two scalar properties to an existing DTO over creating a new interface/service/repository path.
- Prefer canonical read services such as `IUserServiceRead`, `ITeamServiceRead`, and section view DTOs for cross-section reads.
- Keep controllers thin, but allow UI-specific finite sorting/filtering/paging at the controller/view boundary.

## References

- Use [architecture-reviewer.md](references/architecture-reviewer.md) for the review gate.
- Use [reforge_delta.py](scripts/reforge_delta.py) for score summaries.
