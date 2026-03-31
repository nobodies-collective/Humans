# Tickets — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| TicketAdmin, Board, Admin | View ticket orders and attendees; access ticket dashboard |
| TicketAdmin, Admin | Trigger ticket sync; manage discount codes; export ticket data |
| Admin only | Manage ticket sync configuration; manual vendor API operations |

## Invariants

- `TicketController` requires `RoleGroups.TicketAdminBoardOrAdmin` at the controller level.
- Sync/management actions require `RoleGroups.TicketAdminOrAdmin` or `RoleNames.Admin`.
- TicketOrder and TicketAttendee records are synced from the external ticket vendor — not manually created.
- TicketOrder is enriched with Stripe fee data (PaymentMethod, StripeFee, ApplicationFee) during sync.
- TicketOrder.MatchedUser and TicketAttendee.MatchedUser are auto-matched by email address.
- TicketSyncState is a singleton tracking sync operational state (last sync time, status).
- `TicketSyncJob` runs as a background job to pull order/attendee data from the vendor.

## Triggers

- When ticket sync runs, new orders/attendees are imported and existing ones are updated.
- Auto-matching runs during sync: orders/attendees are matched to Users by email.

## Cross-Section Dependencies

- **Campaigns**: TicketAdmin can generate discount codes via CampaignController.
- **Profiles**: TicketOrder/TicketAttendee auto-match against User emails.
- **Admin**: Sync configuration and manual operations are Admin only.
