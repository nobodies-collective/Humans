# Campaigns — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Any authenticated user | View campaigns they have been granted codes for |
| TicketAdmin, Admin | Generate discount codes for campaigns |
| Admin only | Full CRUD on campaigns; import codes; manage waves; send campaign emails; manage campaign grants |

## Invariants

- `CampaignController` requires `[Authorize]` at the controller level.
- Most mutating actions require `RoleNames.Admin`. Code generation requires `RoleGroups.TicketAdminOrAdmin`.
- Campaign status follows: Draft -> Active -> Completed.
- CampaignCode is unique per campaign. Each code is assigned to at most one user via CampaignGrant.
- CampaignGrant tracks `LatestEmailStatus` and `LatestEmailAt` from the most recent delivery attempt.
- Campaign emails are queued through the EmailOutboxMessage system with `CampaignGrantId` link.
- Users can unsubscribe from campaigns via `/Unsubscribe/{token}` which sets `User.UnsubscribedFromCampaigns = true`.
- Unsubscribed users are excluded from future campaign sends.

## Triggers

- When a campaign wave is sent, emails are queued to the outbox for each granted user who hasn't unsubscribed.
- When a user unsubscribes, `UnsubscribedFromCampaigns` is set on their User record.

## Cross-Section Dependencies

- **Tickets**: TicketAdmin can generate discount codes for campaigns.
- **Email**: Campaign emails are delivered through the EmailOutboxMessage system.
- **Profiles**: CampaignGrant links to User. Unsubscribe flag lives on User entity.
