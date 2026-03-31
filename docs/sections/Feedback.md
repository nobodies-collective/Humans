# Feedback — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Any authenticated user | Submit feedback (bug, feature request, question); view own feedback history |
| FeedbackAdmin, Admin | View all feedback; update status; add admin notes; send email responses; link GitHub issues |
| API (key auth) | Full CRUD via `FeedbackApiController` (no user session required) |

## Invariants

- `FeedbackController` requires `[Authorize]` at the controller level — accessible during onboarding (exempt from MembershipRequiredFilter).
- Admin actions (Manage, Respond) require `RoleGroups.FeedbackAdminOrAdmin`.
- FeedbackReport is always linked to the submitting user via `UserId`.
- Screenshot uploads are stored under `wwwroot/uploads/feedback/` with validated MIME types (image/jpeg, image/png, image/webp).
- FeedbackStatus follows: Open -> Acknowledged -> Resolved/WontFix.
- `AdminResponseSentAt` is set when an admin sends an email response to the reporter.
- `FeedbackApiController` uses API key authentication, not session auth — it is not exempt from membership checks because it uses `ControllerBase`, not `Controller`.

## Triggers

- When an admin sends a response, an email is queued to the reporter via the email outbox.
- FeedbackReport audit action `FeedbackResponseSent` is logged when an admin responds.

## Cross-Section Dependencies

- **Admin**: GitHub issue linking (`GitHubIssueNumber`) connects feedback to the external issue tracker.
- **Email**: Response emails are queued through the EmailOutboxMessage system.
