# Code Review Rules

Hard rules for code review. Every item here is a **reject** — if any of these are violated, the review must flag it as CRITICAL regardless of context. These rules are passed verbatim to all reviewers (Claude, Codex, Gemini) via `/code-review`.

## Migration Integrity

- **No hand-edited migrations.** Migration files must be exactly as `dotnet ef migrations add` generated them. Any `migrationBuilder.Sql()` call, manual column tweak, or inserted comment that wasn't scaffolded is a reject. Data cleanup goes in separate scripts or application code, never in migrations.
- **No deleted or renamed migration files** that have already been committed. Generate a new migration for corrections.

## Service Method Parity

- **Batch/range methods must enforce the same guards as their single-item counterparts.** If `SignUpAsync` checks AdminOnly, duplicates, overlap, capacity, EE cap, and system-open — then `SignUpRangeAsync` must check all of those too. Missing a guard in a batch path is an authorization or data integrity bypass. Same principle for `BailRangeAsync` vs `BailAsync`, or any future bulk operation.
- **Batch operations on stateful entities must filter by valid status.** If a domain method throws on invalid state transitions (e.g., `Bail()` throws on already-Bailed signups), the caller must filter to only valid-status records before calling. Never load all records and hope they're in the right state.

## View / Controller Consistency

- **Every controller action must be reachable from UI.** No dead endpoints — if an action exists, there must be a form, link, or JS call that invokes it. Unreachable actions are dead code that misleads and rots.
- **Every new page must have a nav link** (per CLAUDE.md). No orphan pages.
- **Data-creating buttons must have idempotency guards.** If clicking "Generate Shifts" twice would double-create, the button must be hidden or disabled after the first use. Applies to any bulk creation UI.
- **Success messages must reflect actual results**, not independently computed values. If the service creates N items, the message should use N from the result, not re-derive it from input parameters (which can diverge, e.g., negative counts).

## Type Safety in Views

- **No lossy casts in display logic.** `(int)duration.TotalHours` silently drops fractional hours. Use `PlusMinutes((int)duration.TotalMinutes)` or equivalent. Applies to any numeric conversion where precision matters for display.
- **JS array indices for model binding must be contiguous.** ASP.NET MVC model binding breaks on gaps (e.g., `[0], [2]` after removing `[1]`). When removing dynamic form rows, reindex remaining elements.

## Anti-Forgery

- **Every POST form must include `@Html.AntiForgeryToken()`.** Every POST action must have `[ValidateAntiForgeryToken]`. No exceptions.

## Dead Code

- **No unused variables, unreachable code, or orphan imports** in committed code. If a reviewer spots `var x = ...; // never read`, it's a reject.
