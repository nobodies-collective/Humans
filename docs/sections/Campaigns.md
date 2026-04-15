# Campaigns — Section Invariants

## Concepts

- A **Campaign** is a bulk code distribution effort — discount codes are assigned to humans and delivered via email waves.
- A **Campaign Code** is an individual code belonging to a campaign. Codes are imported in bulk (CSV) or generated via the ticket vendor.
- A **Campaign Grant** records the assignment of a specific code to a specific human.
- A **Wave** is a batch email send targeting a group of humans (typically by team) who have been granted codes but not yet notified.

## Actors & Roles

| Actor | Capabilities |
|-------|-------------|
| TicketAdmin, Admin | View campaign details, generate discount codes via the ticket vendor |
| Admin | Full campaign management: create, edit, activate, complete campaigns. Import codes. Manage grants. Send campaign email waves |

## Invariants

- Campaign status follows: Draft then Active then Completed.
- Codes can only be generated or imported while the campaign is in Draft status.
- Each code is unique per campaign and can be assigned to at most one human.
- Campaign emails are queued through the email outbox system. Each grant tracks the status and timestamp of the most recent delivery attempt.
- Humans can unsubscribe from campaigns via a link in the email. Unsubscribed humans are excluded from future campaign sends.

## Negative Access Rules

- TicketAdmin **cannot** create, edit, activate, or complete campaigns. They can only view details and generate codes.
- Regular humans and other roles have no access to campaign management.
- There is no self-service view for humans to see their assigned codes (codes are delivered by email).

## Triggers

- When a campaign wave is sent, emails are queued to the outbox for each granted human who has not unsubscribed.
- When a human unsubscribes, their unsubscribe flag is set and they are excluded from all future campaign sends.

## Cross-Section Dependencies

- **Tickets**: TicketAdmin can generate discount codes for campaigns via the ticket vendor integration.
- **Email**: Campaign emails are delivered through the email outbox system.
- **Profiles**: Campaign grants link to a human. The unsubscribe flag lives on the human's account.

## Architecture — Current vs Target

See `docs/architecture/design-rules.md` for the full rules.

**Owning services:** `CampaignService`
**Owned tables:** `campaigns`, `campaign_codes`, `campaign_grants`

## Target Architecture Direction

> **Status:** This section currently follows the "services in Infrastructure, direct DbContext" model. It will be migrated to the repository/store/decorator pattern per [`../architecture/design-rules.md`](../architecture/design-rules.md). **Delete this block once the migration lands and this section's services live in `Humans.Application` with `*Repository.cs` impls in `Humans.Infrastructure/Repositories/`.**

### Target repositories

- **`ICampaignRepository`** — owns `campaigns`, `campaign_codes`, `campaign_grants`
  - Aggregate-local navs kept: `Campaign.Codes`, `Campaign.Grants`, `CampaignCode.Campaign`, `CampaignCode.Grant`, `CampaignGrant.Campaign`, `CampaignGrant.Code`
  - Cross-domain navs stripped: `Campaign.CreatedByUser` (Users), `CampaignGrant.User` (Users), `CampaignGrant.OutboxMessages` (Email)

### Current violations

Observed in this section's service code as of 2026-04-15:

- **Cross-domain `.Include()` calls:**
  - `CampaignService.cs:65` — `.Include(c => c.Grants).ThenInclude(g => g.User)` (Users)
  - `CampaignService.cs:350` — `.Include(u => u.UserEmails)` on `_dbContext.Users` (Users/Profiles)
  - `CampaignService.cs:452` — `.Include(g => g.User).ThenInclude(u => u.UserEmails)` (Users/Profiles)
  - `CampaignService.cs:535` — `.Include(g => g.User).ThenInclude(u => u.UserEmails)` (Users/Profiles)
- **Cross-section direct DbContext reads:**
  - `CampaignService.cs:147` — `_dbContext.Teams` (Teams) to build team options for the send-wave page
  - `CampaignService.cs:349` — `_dbContext.Users.Include(u => u.UserEmails)` (Users/Profiles) to select candidate humans for grant assignment
  - `CampaignService.cs:579` — `_dbContext.TeamMembers` (Teams) in `GetActiveTeamUserIdsAsync`
- **Inline `IMemoryCache` usage in service methods:** None found
- **Cross-domain nav properties on this section's entities:**
  - `Campaign.CreatedByUser` → `User` (Users)
  - `CampaignGrant.User` → `User` (Users)
  - `CampaignGrant.OutboxMessages` → `EmailOutboxMessage` (Email)

### Touch-and-clean guidance

Until this section is migrated end-to-end, when touching its code:

- Route team lookups through `ITeamService` instead of `_dbContext.Teams` / `_dbContext.TeamMembers` (`CampaignService.cs:147`, `:579`). A `GetTeamMemberUserIdsAsync(teamId)` call on `ITeamService` replaces both patterns.
- Route human/email lookups through `IUserService` + `IProfileService` (or a dedicated email-lookup method) instead of `_dbContext.Users.Include(u => u.UserEmails)` (`CampaignService.cs:349`, `:452`, `:535`). Batch user IDs in, get a dictionary of email addresses out.
- Drop `.ThenInclude(g => g.User)` on Grant queries (`CampaignService.cs:65`, `:452`, `:535`) — fetch grants via the repository, then resolve display data through user/profile services in a second call.
- Do not add new cross-domain navs to `Campaign`, `CampaignCode`, or `CampaignGrant`. When adding fields, keep them scalar or aggregate-local only.
