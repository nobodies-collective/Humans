# Tickets — Target Shape

Written fresh each section-doctor run (Phase 3c), before any scan. History rows at the bottom.

## What the section does

The org sells event tickets on an outside vendor's site. This section mirrors the vendor's
orders, issued tickets and gate scans into Humans, and works out which human each ticket
belongs to by matching the attendee email against members' verified emails. From that mirror it
answers three audiences. Members see whether they hold a ticket, who is on it, and can hand a
ticket they hold to another member — the ticket team completes the swap with the vendor and
both people are emailed. The ticket team sees sales, revenue, fees, VAT and donations, who has
not bought yet, which discount codes were redeemed, a live roster of who is on site, and can
provision accounts for buyers who are not yet members. The rest of the app asks it one thing:
does this person hold a ticket, and what are they holding. Holding a ticket also becomes the
member's yearly participation record.

## The shapes

| Shape | Members | Notes |
|---|---|---|
| Mirror the vendor | `TicketSyncService.SyncAsync` (+ full re-sync), `ticket-sync` job, `POST /Tickets/Sync`, `POST /Tickets/FullResync`, `TicketSyncState` | One pipeline: fetch orders + check-ins → upsert → email-match → Stripe fee enrichment → VAT split → code redemption → participation reconcile. Incremental by `LastSyncAt` cursor |
| "Does this human hold a ticket" | `ITicketServiceRead` (`GetTicketOrdersAsync`, `GetUserTicketHoldingsAsync`, `[SurfaceBudget(2)]`) fed by `ITicketRepository.HasEventTicketAsync` and the private `ComputeUserTicketCountAsync` | One question over two match paths (`MatchedUserId`, then verified-email fallback); one projection callers derive from |
| Member holds & transfers | `/Tickets/Transfers` (Index, Confirm, Submit, Cancel), `<vc:my-ticket-stubs>`, `<vc:ticket-holdings>`, `<vc:ticket-stub>`, `<vc:member-ticket-status>`, `<vc:guest-ticket-orders>` | One wizard + one stub renderer reused by homepage, profile and wizard |
| Admin transfer processing | `/Tickets/Admin/Transfers` (Index?tab, Detail/{id}, Decide with action ∈ process/retry/approve/reject), `ITicketTransferQueue.GetPendingCountAsync` | One state machine: Pending → Approved / Rejected / Cancelled; vendor void-to-hold + reissue is the automated path, mark-successful the manual one |
| Reporting | `/Tickets` (dashboard), `/Tickets/Orders`, `/Tickets/Attendees`, `/Tickets/Codes`, `/Tickets/SalesAggregates`, `/Tickets/WhoHasntBought`, two CSV exports, `/Tickets/GateList` (placeholder) | Paged list ×3 (search/sort/filter), aggregates ×2, one "who hasn't" cross-join with Users/Teams/Governance |
| Onsite & gate tooling | `/Tickets/Admin/Onsite`, `/Tickets/Admin/Gate` (set/rotate gate-terminal password); barcode → stub for Scanner/Gate is a projection over `GetTicketOrdersAsync` | One roster join, one credential rotation |
| Contact import | `/Tickets/Admin/Contacts` (preview → apply) | Plan/apply over unmatched attendees: attach verified / replace unverified / create user |
| Participation | `IUserParticipationBackfillService` via `/Tickets/Participation/Backfill` (Admin only) | CSV backfill; the reconcile itself lives in the sync pipeline |
| Discount codes for Campaigns | `ITicketDiscountCodes` | Vendor port pass-through so Campaigns never names the vendor |
| GDPR | `IUserDataContributor` export; `ITicketPiiErasure` tombstone scrub | Buyer/attendee names + emails tombstoned, rows kept for finance |
| Cache seam | `ITicketCacheInvalidator` (poked by sync, merge fold, transfer transitions) | Owned by the singleton decorator; three slices: Orders (warmed), UserHoldings (5 min), per-event vendor summary (IMemoryCache) |
| Health | `TicketVendorHealthCheck` | Vendor reachability probe |

## Structure

The layout these shapes imply:

- One vendor port (`ITicketVendorService` in `Contracts/`), one adapter project
  (`Humans.TicketTailor`), and the Tickets leaf never names the vendor.
- One sync service owning the pipeline end to end; one repository over the four tables
  (`ticket_orders`, `ticket_attendees`, `ticket_sync_state`, `ticket_transfer_requests`) plus one
  transfer repository over the request table's own reads and writes.
- One query service behind `ITicketService` (admin) and `ITicketServiceRead` (cross-section),
  wrapped once by a singleton caching decorator that also owns invalidation.
- One transfer service holding the state machine; the vendor writeback goes through it and
  nowhere else.
- Controllers per audience: admin dashboard, member transfer wizard, admin transfer queue,
  contacts import, gate credential, onsite roster. Each translates and formats only; the view
  models it needs are the service DTOs or thin projections of them, not parallel copies.
- Member-facing views localized through the section's own resx set; admin views unlocalized.

## Invariants

- Buyer-only matches never count as holding a ticket: `MatchedUserId` on an attendee row, or an
  attendee email equal to one of the user's verified emails, is the only way a person "has a
  ticket". A paid order in someone's name with no attendee for them does not.
- A gate scan stamps `CheckedInAt` and leaves `Status = Valid`; transferability requires both
  `Status == Valid` and `CheckedInAt == null`, and is enforced in the row flags, the confirm step
  and `CreateRequestAsync`, not only in the view.
- Only the Sender may cancel, only while Pending; only `TicketAdminOrAdmin` may process, retry,
  approve or reject; a `VoidSucceededIssueFailed` request accepts only retry or mark-successful.
- The manual approval path mutates no attendee rows; the automated path writes the swapped rows
  itself; every transition audits (`TicketTransferRequested/Cancelled/Approved/AutoFailed/Rejected`).
- Sync is idempotent and cursor-driven; a failed sync leaves `SyncStatus = Error` with the
  message on the singleton row, never a half-applied cursor.
- `ParticipationStatus.Attended` is write-once; sync derives Ticketed/Attended from attendee
  rows and removes Ticketed when no valid ticket remains.
- Erasure tombstones name/email on order and attendee rows and never deletes them; the vendor
  ids stay so finance and sync keep reconciling.
- `Board` reads every admin page but triggers no sync, export, decision or code generation;
  `ParticipationBackfill` and `FullResync` are Admin only; the gate-terminal account reaches
  only `ScannerAccess`.
- `TicketsConfigured == false` short-circuits every member card and the dashboard to the
  not-configured state; nothing else changes.

## Seams

- **Event label on the stub** is a constant (`"Elsewhere 2026 · Admit One"`, `TicketStub`
  view); the design reserves sourcing it from the active event. Not built.
- **`VendorStepsJson`** column is dormant by design until prod soak; the drop is a scheduled
  follow-up, not this section's to do ad hoc.
- **`/Tickets/GateList`** is a placeholder page whose function moved to `/Scanner/Tickets`; its
  removal is a nav decision for Peter.
- **`CacheKeys.TicketDashboardStats`** is a reserved, invalidator-only key for a dashboard
  cache that was never added.

## Deliberately not done

- No vendor-agnostic transfer abstraction beyond the port: the void-to-hold + reissue sequence
  is TicketTailor's, and the port exposes exactly those two calls.
- No read-through cache on the dashboard stats; on-demand staleness during sync is accepted.
- No pagination-free admin lists: orders/attendees are the one place the dataset is large
  enough that paging buys something.
- No concurrency tokens on the transfer request; the state machine tolerates a double click by
  re-checking status.
- No per-environment toggle for the automated transfer path; it is always offered.
- No separate Attendee aggregate root: `TicketTransferRequest` references the attendee with no
  inverse collection on purpose.
- No local ticket issuance of any kind; Humans never creates a ticket except as a transfer
  reissue from a held seat.

## Load-bearing weirdness

- **Email matching uses `NormalizingEmailComparer`** so gmail/googlemail aliases and dots collide;
  matching by raw string would silently split one person into two.
- **`HasEventTicketAsync` and `ComputeUserTicketCountAsync` fall back to verified emails** when
  `MatchedUserId` is null, because the sync only writes `MatchedUserId` on its own cadence and
  a member who just verified an email expects the homepage to update now.
- **The caching decorator is a Singleton wrapping a Scoped inner** via `WithInner`, because
  `TrackedCache` slices must outlive a request while the repository must not.
- **Every transfer transition invalidates the holdings cache and audits before any email**, and
  the automated path records the vendor diagnostic on the request before deciding whether to
  email, so a crash mid-flow leaves the admin a readable state rather than a silent Pending.
  Email sends are wrapped so a mail failure never rolls back a recorded decision.
- **The query service reads the transfer repository** to stamp the pending-transfer flag on a
  member's holdings; the transfer table has its own repository so the state machine's writes
  stay in one place, and the holdings read is a projection over it, not a second writer.
- **The gate-terminal account is a real user with no roles** and a rotating password, created
  lazily the first time a ticket admin sets its password from `/Tickets/Admin/Gate`; the
  password lives on the Identity row and rotation invalidates live gate sessions.
- **`TicketRepository` is a Singleton** over a context factory while the services are Scoped;
  the singleton caching decorator resolves its Scoped inner per call.
- **Contact import re-queries at apply time** rather than trusting the plan, because a sync can
  land between preview and apply.
- **Order-drift table** on the transfer queue exists because the manual path leaves the local
  rows stale until the next sync; it is the team's reconciliation aid, not a bug list.

## History

| Run | Date | Headline | PR |
|---|---|---|---|
| section-doctor | 2026-09-05 | First doctoring: invariant doc rebuilt against the code, narration purged, two dead resx keys cut, sync-cursor test pinned | peterdrier/Humans#pending |
