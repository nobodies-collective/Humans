# Maintenance Log

Tracks when recurring maintenance processes were last run.

| Process | Last Run | Next Due | Cadence | Notes |
|---------|----------|----------|---------|-------|
| NuGet vulnerability check | — | — | Weekly | `dotnet list package --vulnerable` |
| Todo audit | 2026-02-12 | 2026-02-19 | Weekly | Stale items, completed moves |
| Code simplification | — | — | After features | Dead code, unused abstractions |
| Static analysis | — | — | After features | ReSharper/Roslyn warnings |
| Context cleanup | 2026-02-12 | 2026-03-12 | Monthly | CLAUDE.md, .claude/, todos.md |
| Feature spec sync | 2026-02-12 | 2026-03-12 | Monthly | docs/features/ vs implementation |
| i18n audit | — | — | Monthly | Missing translations |
| Data model doc sync | 2026-02-12 | As needed | As needed | .claude/DATA_MODEL.md vs entities |
| GDPR audit | — | — | Quarterly | Exports, consent, PII logging |
| NuGet full update | — | — | Monthly | Non-security package updates |
| GitHub issue triage | — | — | Weekly | Sync issues vs todos.md |
