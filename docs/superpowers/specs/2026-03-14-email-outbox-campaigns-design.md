# Email Outbox & Campaign System Design

**Date:** 2026-03-14
**Status:** Draft

## Problem

The Humans system sends emails inline via SMTP. If a send fails (429 rate limit, network error, etc.), the email is lost with no way to recover. Additionally, the org needs to send individualized discount codes to members (presale tickets) with guarantees about delivery tracking and the ability to send in waves for testing.

## Goals

1. **Reliable email delivery** — no email is lost on transient failure; all sends are tracked with retry
2. **Branded email template** — light polish to match the website's Renaissance/parchment aesthetic
3. **Campaign system** — import codes, assign to filtered humans in waves, track delivery, support resend
4. **Observability** — Prometheus metrics for queue depth, sent/failed counts; admin dashboard
5. **Unsubscribe** — campaign emails include unsubscribe mechanism per Google/Yahoo bulk sender requirements

## Non-Goals

- Priority lanes (transactional vs campaign) — FIFO is sufficient at ~500 users
- Lottery-based code assignment — future feature for low-income ticket system
- Email open/click tracking — out of scope
- AMP email support

## Architecture Overview

Three interconnected features built on a shared foundation:

```
┌─────────────────────────────────────────────────┐
│                   Callers                        │
│  (Services, Jobs, Controllers, CampaignService)  │
└──────────────────────┬──────────────────────────┘
                       │ IEmailService.Send*Async()
                       ▼
┌─────────────────────────────────────────────────┐
│            OutboxEmailService (NEW)              │
│  Implements IEmailService                        │
│  Renders email → writes to outbox table          │
└──────────────────────┬──────────────────────────┘
                       │ INSERT
                       ▼
┌─────────────────────────────────────────────────┐
│          email_outbox_messages (DB)              │
│  Status: Queued → Sent | Failed                  │
│  Crash recovery via PickedUpAt timeout           │
└──────────────────────┬──────────────────────────┘
                       │ Polled every 1 min
                       ▼
┌─────────────────────────────────────────────────┐
│         ProcessEmailOutboxJob (Hangfire)          │
│  Batch of 10 per cycle (10/min throttle)         │
│  Exponential backoff on failure, max 10 retries  │
└──────────────────────┬──────────────────────────┘
                       │ SMTP send
                       ▼
┌─────────────────────────────────────────────────┐
│           SmtpEmailService (EXISTING)            │
│  Now transport-only, called by processor         │
└─────────────────────────────────────────────────┘
```

## Data Model

### EmailOutboxMessage (NEW)

Every email flows through this table — transactional and campaign alike.

| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| RecipientEmail | string | Destination address |
| RecipientName | string? | Display name |
| Subject | string | Email subject line |
| HtmlBody | string | Fully rendered HTML |
| PlainTextBody | string? | Plain text fallback |
| TemplateName | string | e.g. "WelcomeVolunteer", "CampaignCode" |
| UserId | Guid? (FK→User) | Null for external recipients |
| CampaignGrantId | Guid? (FK→CampaignGrant) | Links campaign code delivery |
| ReplyTo | string? | Reply-to address (e.g., facilitated messages) |
| ExtraHeaders | string? | JSON dictionary for additional headers (List-Unsubscribe, etc.) |
| Status | EmailOutboxStatus enum | Queued, Sent, Failed (no Sending — see Crash Recovery) |
| CreatedAt | Instant | When queued |
| SentAt | Instant? | When delivered to SMTP |
| PickedUpAt | Instant? | When processor claimed this message (null = not yet picked up) |
| RetryCount | int | Increments on failure, default 0 |
| LastError | string? | Truncated to 4000 chars |
| NextRetryAt | Instant? | Exponential backoff: now + 2^RetryCount minutes |

**Indexes:**
- Status + NextRetryAt (for processor query)
- UserId (for user email history)
- CampaignGrantId (for campaign status tracking)

**Crash Recovery:** No `Sending` status. The processor uses a `PickedUpAt` timestamp instead (following the Google sync outbox pattern). The processor query selects messages where `SentAt IS NULL AND RetryCount < 10 AND (NextRetryAt IS NULL OR NextRetryAt <= now) AND (PickedUpAt IS NULL OR PickedUpAt < now - 5 minutes)`. If the process crashes mid-send, the message is automatically re-picked after the 5-minute timeout. This avoids the orphaned-state problem entirely.

**Retention:** Sent rows purged after 90 days by `CleanupEmailOutboxJob`. Failed rows retained until manually resolved.

### Campaign (NEW)

A named code distribution with email template.

| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| Title | string | "2026 Presale" |
| Description | string? | Internal notes |
| EmailSubject | string | Subject with `{{Name}}` placeholder |
| EmailBodyTemplate | string | HTML body with `{{Code}}`, `{{Name}}` placeholders |
| Status | CampaignStatus enum | Draft, Active, Completed |
| CreatedAt | Instant | |
| CreatedByUserId | Guid (FK→User) | Admin who created it |

### CampaignCode (NEW)

Imported code pool. Codes not referenced by a grant are available for assignment.

| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| CampaignId | Guid (FK→Campaign) | |
| Code | string | The actual code value from CSV |
| ImportedAt | Instant | When CSV was processed |

**Unique constraint:** (CampaignId, Code)

### CampaignGrant (NEW)

Links one code to one human within a campaign.

| Column | Type | Notes |
|--------|------|-------|
| Id | Guid (PK) | |
| CampaignId | Guid (FK→Campaign) | |
| CampaignCodeId | Guid (FK→CampaignCode) | Which code from the pool |
| UserId | Guid (FK→User) | Which human |
| AssignedAt | Instant | When code was assigned |
| LatestEmailStatus | EmailOutboxStatus? | Denormalized: Queued/Sent/Failed from latest outbox message |
| LatestEmailAt | Instant? | Denormalized: timestamp of latest email status change |

**Unique constraints:**
- (CampaignId, UserId) — one code per human per campaign; enables safe multi-wave sends
- (CampaignCodeId) — each code assigned at most once

**Send history:** Multiple EmailOutboxMessage rows can reference the same grant (initial send + resends/reminders).

### Entity Relationships

```
Campaign ──1:N──→ CampaignCode (imported pool)
Campaign ──1:N──→ CampaignGrant (assignments)
CampaignCode ──1:0..1──→ CampaignGrant (assigned or available)
CampaignGrant ──1:N──→ EmailOutboxMessage (send history)
User ──1:N──→ CampaignGrant (codes received)
User ──1:N──→ EmailOutboxMessage (email history)
```

## Feature 1: Email Outbox (Reliability)

### OutboxEmailService

New implementation of `IEmailService` that replaces `SmtpEmailService` in DI registration. Each of the 16 existing email methods:

1. Calls `IEmailRenderer` to render subject + HTML body (unchanged)
2. Wraps HTML in the email template (moved from SmtpEmailService)
3. Generates plain text fallback
4. Writes an `EmailOutboxMessage` row with Status = Queued
5. Records `emails_queued` metric
6. Returns immediately

### ProcessEmailOutboxJob (Hangfire Recurring)

Runs every 1 minute (Hangfire cron minimum). Follows the same pattern as `ProcessGoogleSyncOutboxJob`.

```
1. Check global pause flag (SyncServiceSettings) — if paused, return immediately
2. SELECT TOP(10) FROM email_outbox_messages
   WHERE SentAt IS NULL
     AND RetryCount < 10
     AND (NextRetryAt IS NULL OR NextRetryAt <= now)
     AND (PickedUpAt IS NULL OR PickedUpAt < now - 5 minutes)
   ORDER BY CreatedAt ASC
3. Set PickedUpAt = now for entire batch, SaveChanges
4. For each message:
   a. Connect to SMTP, send via MailKit (including ReplyTo and ExtraHeaders)
   b. On success: Status = Sent, SentAt = now, PickedUpAt = null
      On failure: Status = Failed, RetryCount++, LastError = message
                  NextRetryAt = now + 2^RetryCount minutes
                  PickedUpAt = null (release for future retry)
   c. Record metrics (sent/failed counter by template)
5. SaveChanges
```

**Crash recovery:** If the process crashes between steps 3 and 5, the `PickedUpAt` timestamp ensures messages are re-picked after 5 minutes. No messages are ever permanently orphaned.

### Throttle Configuration

```json
"Email": {
  "OutboxBatchSize": 10,
  "OutboxMaxRetries": 10,
  "OutboxRetentionDays": 90
}
```

Default: 10 per 1-minute cycle = 10/min. Google limit is 100/min; we stay at 10% to leave headroom. `OutboxIntervalSeconds` removed — Hangfire cron controls the interval (`*/1 * * * *`).

### Global Pause

Uses the existing `SyncServiceSettings` pattern (same as Google sync services). Add `EmailOutbox = 3` to the `SyncServiceType` enum. The processor checks `SyncMode` at the start of each batch: `SyncMode.None` = paused, `SyncMode.AddOnly` = active (AddAndRemove is unused but treated as active). Toggled via Admin UI. When paused (`SyncMode.None`):
- No emails are sent
- Queued emails remain in outbox
- Admin UI shows "Sending paused" indicator
- Campaigns can still queue emails (they just won't be sent until unpaused)

### SmtpEmailService Changes

Becomes transport-only. The public `IEmailService` methods are removed. A new internal interface is exposed for the processor:

```csharp
public interface IEmailTransport
{
    Task SendAsync(string recipientEmail, string? recipientName,
        string subject, string htmlBody, string? plainTextBody,
        string? replyTo = null,
        IDictionary<string, string>? extraHeaders = null,
        CancellationToken cancellationToken = default);
}
```

`SmtpEmailService` implements `IEmailTransport`. This handles SMTP connection, MimeMessage construction (including ReplyTo header), and sending. The `extraHeaders` parameter supports List-Unsubscribe for campaign emails.

## Feature 2: Email Template Polish

### Changes to WrapInTemplate

The existing template wrapper (currently in SmtpEmailService, moving to OutboxEmailService) gets enhanced:

**Added:**
- Dark header bar (#3d2b1f) with gold "Humans" wordmark and "NOBODIES COLLECTIVE" subtitle
- Gold accent border below header (#c9a96e, 3px)
- Warm parchment footer background (#f0e2c8) with border-top
- Consistent 24px horizontal padding in body area

**Unchanged:**
- Source Sans 3 body font (with Segoe UI, Roboto, sans-serif fallbacks)
- Georgia heading font (web-safe fallback for Cormorant Garamond)
- 600px max-width
- Aged ink text color (#3d2b1f)
- Gold link color (#8b6914)
- Environment banner (QA/Staging) positioned above the header bar

**Email client safety:**
- All styles inline (Gmail strips `<style>` blocks in body)
- No background images or SVG textures
- Web fonts degrade gracefully to system fonts
- `<style>` block in `<head>` for clients that support it (Apple Mail, Thunderbird)

### CTA Button Styling

Action links (sign-in, verify email, etc.) rendered as gold pill buttons:
```html
<a href="..." style="display:inline-block;background:#c9a96e;color:#3d2b1f;
   text-decoration:none;padding:10px 24px;border-radius:4px;font-weight:600;
   font-size:14px;">Sign in to Humans</a>
```

This is opt-in per email template — the renderer marks certain links as CTAs.

## Feature 3: Campaign System

### Campaign Workflow

```
Create (Draft) → Import Codes → Activate → Send Wave(s) → Complete
```

**Step 1: Create Campaign**
Admin enters title, description, email subject, and HTML body template with `{{Code}}` and `{{Name}}` placeholders. Campaign starts in Draft status.

**Step 2: Import Codes**
Admin uploads CSV file with one code per row (or single column). System creates `CampaignCode` rows. Multiple imports allowed (appends to pool). Duplicate codes within campaign are rejected.

**Step 3: Activate**
Status: Draft → Active. Required before sending. Validates that at least one code is imported and email template is set.

**Step 4: Send Wave** (repeatable)
1. Admin selects recipient filter:
   - All Active Humans
   - Role: Board / Leads
   - Tier: Colaborador / Asociado
   - Team: (select specific team)
2. System shows preview:
   - N humans match filter
   - Excludes M humans who already have a grant in this campaign
   - Excludes K humans who unsubscribed from campaigns
   - P codes available in pool (Q will remain after send)
3. On confirm (**single serializable transaction**):
   - Claim N available codes from pool (ORDER BY ImportedAt, Id) in one batch query
   - Verify enough codes are available; abort if not
   - Create all CampaignGrant rows
   - For each grant, render email (substitute `{{Code}}` and `{{Name}}` — **values are HTML-encoded** before substitution to prevent injection)
   - Create all EmailOutboxMessage rows with CampaignGrantId
   - Commit transaction
   - If unique constraint violation (concurrent double-click), return graceful error "Wave already in progress"
4. Dashboard updates as outbox processor delivers

**Step 5: Monitor & Resend**
Campaign detail page shows all grants with delivery status. Admin can:
- Resend to individual human (queues new outbox message for same grant)
- Retry all failed (re-queues all grants whose latest outbox message is Failed)

**Step 6: Complete**
Status: Active → Completed. Prevents further wave sends. Campaign + grants remain as permanent audit trail.

### Unsubscribe

**User preference:** `UnsubscribedFromCampaigns` boolean on User entity (or a dedicated preferences table). Default: false.

**Email headers:** Campaign emails include:
- `List-Unsubscribe: <mailto:unsubscribe@nobodies.team?subject=unsubscribe>, <https://humans.nobodies.team/unsubscribe/{token}>`
- `List-Unsubscribe-Post: List-Unsubscribe=One-Click`

**Footer link:** Campaign emails include "Don't want to receive these? [Unsubscribe](link)" in the footer.

**Endpoint:** `GET /unsubscribe/{token}` — shows confirmation page. `POST /unsubscribe/{token}` — sets the flag. Token is generated using ASP.NET `IDataProtectionProvider.CreateProtector("CampaignUnsubscribe").ToTimeLimitedDataProtector()` with a 90-day expiry, encoding the user ID. This reuses the project's existing Data Protection infrastructure (keys stored in DB via `PersistKeysToDbContext`).

**Campaign wave sends** automatically exclude users where `UnsubscribedFromCampaigns = true`.

## Admin UI

### Campaign Management (/Admin/Campaigns)

**Campaign List:** Table with title, status badge, code counts (total/assigned), sent/failed counts, creation date. "New Campaign" button.

**Campaign Detail (/Admin/Campaigns/{id}):**
- Header: title + status badge + action buttons (Send Wave, Import Codes, Complete)
- Stats cards: Codes Imported, Available, Sent, Failed
- Grants table: human name, code (monospace), assigned timestamp, email status + timestamp, resend action

**Send Wave Dialog:**
- Filter dropdown (All Active, Board, Leads, Colaborador, Asociado, specific Team)
- Live preview: recipient count, exclusions (already granted, unsubscribed), codes remaining
- Confirm button: "Confirm Send to N Humans"

### Email Outbox Dashboard (/Admin/EmailOutbox)

**Global controls:**
- Pause/Resume button (red when active, toggles global pause flag)
- Status indicator: "● Sending active" / "⏸ Sending paused"

**Stats cards:** Queued (current), Sent (24h), Failed (current), Throttle rate

**Message table:** Recent outbox messages with recipient, template name, status, timestamp, retry/discard actions for failed messages.

## Metrics (Prometheus)

Extends existing `IHumansMetrics` with new methods:

```csharp
// New methods on IHumansMetrics:
void RecordEmailQueued(string template);   // humans_email_queued_total counter
void RecordEmailFailed(string template);   // humans_email_failed_total counter
void SetEmailOutboxPending(int count);     // humans_email_outbox_pending gauge
// Existing RecordEmailSent(template) reused for humans_email_sent_total
```

| Metric | Type | Labels | Description |
|--------|------|--------|-------------|
| humans_email_queued_total | Counter | template | Emails added to outbox |
| humans_email_sent_total | Counter | template | Successfully delivered (existing metric) |
| humans_email_failed_total | Counter | template | Delivery failures |
| humans_email_outbox_pending | Gauge | — | Current queue depth (set by processor each cycle) |

## Migration Path

The outbox is a **breaking change** in email flow — all 16 email methods switch from inline SMTP to outbox writes. This is safe because:

1. OutboxEmailService implements the same IEmailService interface
2. Only the DI registration changes (SmtpEmailService → OutboxEmailService)
3. The processor uses SmtpEmailService internally for transport
4. If the processor isn't running, emails queue up harmlessly

**Rollback:** Swap DI registration back to SmtpEmailService. Queued-but-unsent emails would need manual processing.

## DI Registration

Environment-based registration following the existing Google stub pattern:

```csharp
// IEmailService: callers use this to queue emails
services.AddScoped<IEmailService, OutboxEmailService>();

// IEmailTransport: processor uses this to send
if (isStubMode)
    services.AddScoped<IEmailTransport, StubEmailTransport>(); // logs, no SMTP
else
    services.AddScoped<IEmailTransport, SmtpEmailTransport>();
```

`StubEmailTransport` replaces the old `StubEmailService` — it implements `IEmailTransport` and logs the send without connecting to SMTP. In dev/test, emails still flow through the outbox (testing the full pipeline) but the transport is a no-op.

## Template Substitution Security

Campaign email template substitution (`{{Code}}`, `{{Name}}`) must HTML-encode all values before insertion using `System.Net.WebUtility.HtmlEncode()`. This prevents HTML injection via user-controlled data (e.g., a display name containing `<script>`). Placeholder matching uses `StringComparison.Ordinal` per CODING_RULES.md.

The complete substitution vocabulary for campaign templates:
- `{{Code}}` — the assigned code value
- `{{Name}}` — the recipient's display name

Campaign activation validation should warn (not block) if the template body does not contain `{{Code}}`.

## Cleanup Job

`CleanupEmailOutboxJob` runs weekly (Sunday 03:00 UTC). Deletes outbox rows where `Status = Sent AND SentAt < now - RetentionDays`. Failed rows are never auto-deleted — admin must manually discard them via the outbox dashboard.

## Testing Strategy

- **Unit tests:** OutboxEmailService writes correct rows; ProcessEmailOutboxJob processes correctly with mock transport
- **Integration tests:** End-to-end campaign flow with test DB
- **QA testing:** Campaign workflow with real SMTP to a test mailbox. Send wave to Board (small group) first.
- **StubEmailTransport:** Used in dev/test — emails flow through outbox but transport is a no-op (logs only)
