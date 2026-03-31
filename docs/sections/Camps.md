# Camps — Section Invariants

> Version: 1.0 (draft — pending human review)

## Actors & Roles

| Actor | Access |
|-------|--------|
| Anonymous | Browse camps directory, view camp details and season details (public) |
| Any authenticated user | Register a new camp; edit camps they lead; manage leads, images, historical names on their camp |
| Camp lead | Edit camp details, manage season registrations, manage co-leads for their camp |
| CampAdmin, Admin | All camp lead capabilities on all camps; approve/reject season registrations; manage CampSettings |
| Admin only | Delete camps; manage global camp settings (public year, open seasons) |

## Invariants

- `CampController` has no controller-level `[Authorize]` — public pages (Index, Details, SeasonDetails) allow anonymous access.
- Mutating actions on `CampController` require `[Authorize]` per-action and check camp lead status OR `RoleChecks.IsCampAdmin(User)`.
- `CampAdminController` requires `RoleGroups.CampAdminOrAdmin` at the controller level.
- Admin-only actions within CampAdmin (e.g., deleting camps) require `RoleNames.Admin`.
- Camp has a unique `Slug` used for URL routing.
- CampSeason tracks per-year data; status follows: Pending -> Active/Full/Rejected/Withdrawn.
- CampLead has roles: Primary or CoLead. Only camp leads or CampAdmin can manage a camp.
- CampImage files are stored on disk; `CampImage` entity tracks metadata and display order.
- CampHistoricalName tracks previous names for camps that have been renamed.
- CampSettings is a singleton controlling which year is public and which seasons accept registrations.

## Triggers

- When a camp is registered, its initial season is created with Pending status.
- Season approval/rejection is done by CampAdmin via CampAdminController.

## Cross-Section Dependencies

- **Profiles**: Camp leads are linked to Users; lead assignment requires a valid user.
- **Admin**: CampSettings management is in CampAdminController (CampAdmin/Admin only).
