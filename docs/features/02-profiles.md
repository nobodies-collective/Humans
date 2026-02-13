# Profiles

## Business Context

Members need to maintain personal information for organizational records while protecting privacy. The system distinguishes between legal names (restricted access) and public "burner names" used within the community. Location data supports event planning and regional coordination.

## User Stories

### US-2.1: View My Profile
**As a** member
**I want to** view my complete profile information
**So that** I can verify what the organization has on record

**Acceptance Criteria:**
- Profile displays all personal information
- Shows membership status badge
- Displays list of team memberships
- Shows consent status for legal documents

### US-2.2: Edit Personal Information
**As a** member
**I want to** update my personal details
**So that** the organization has accurate contact information

**Acceptance Criteria:**
- Can edit burner name, legal name, phone, bio
- Can update location with Google Places autocomplete
- Changes are timestamped (UpdatedAt)
- Validation prevents invalid data

### US-2.3: Set Burner Name
**As a** member
**I want to** set a public "burner name" separate from my legal name
**So that** my real identity is protected in public contexts

**Acceptance Criteria:**
- Burner name displayed in team listings and public views
- Legal name only visible to member and board members
- User's display name syncs with burner name

### US-2.4: Location Autocomplete
**As a** member
**I want to** enter my location using autocomplete
**So that** I can easily specify my city without manual entry

**Acceptance Criteria:**
- Google Places autocomplete suggestions as user types
- Selecting a place captures: city, country, coordinates
- Stored coordinates enable future map features
- PlaceId stored for reference

## Data Model

### Profile Entity
```
Profile
├── Id: Guid
├── UserId: Guid (FK → User, 1:1)
├── BurnerName: string? (256)
├── FirstName: string (256) [legal]
├── LastName: string (256) [legal]
├── Pronouns: string? (50)
├── DateOfBirth: LocalDate?
├── City: string? (256)
├── CountryCode: string? (2)
├── Latitude: double?
├── Longitude: double?
├── PlaceId: string? (256) [Google Places ID]
├── Bio: string? (4000)
├── EmergencyContactName: string? (256) [board only]
├── EmergencyContactPhone: string? (50) [board only]
├── EmergencyContactRelationship: string? (100) [board only]
├── AdminNotes: string? (4000) [admin only]
├── IsSuspended: bool
├── CreatedAt: Instant
└── UpdatedAt: Instant
```

## Emergency Contact

Members can optionally provide emergency contact information for safety at events.

### Fields
- **EmergencyContactName** (string, max 256) — Name of the emergency contact person
- **EmergencyContactPhone** (string, max 50) — Phone number
- **EmergencyContactRelationship** (string, max 100) — Relationship (e.g., "Partner", "Parent")

### Visibility Rules
- **Profile owner**: Can view and edit their own emergency contact on their profile
- **Board/Admin**: Can view emergency contact on the Admin Member Detail page
- **Other members**: Cannot see emergency contact information (not shown on public profile views)

### GDPR
Emergency contact fields are marked `[PersonalData]` and included in the data export (`ExportData`).

## Membership Status

Profile includes a computed `MembershipStatus` property:

| Status | Description | Visual |
|--------|-------------|--------|
| **Active** | Has roles + all consents signed | Green badge |
| **Inactive** | Has roles but missing consents | Yellow badge |
| **Suspended** | Admin-suspended | Red badge |
| **None** | No active roles | Gray badge |

## Privacy Model

### Data Visibility Matrix

| Field | Member | Other Members | Lead | Board | Admin |
|-------|--------|---------------|----------|-------|-------|
| Burner Name | Yes | Yes | Yes | Yes | Yes |
| Legal Name | Yes | No | No | Yes | Yes |
| Emails (UserEmail) | Per-field visibility | Per-field visibility | Per-field visibility | Yes | Yes |
| City/Country | Yes | Yes | Yes | Yes | Yes |
| Coordinates | No | No | No | Yes | Yes |
| Bio | Yes | Yes | Yes | Yes | Yes |
| Emergency Contact | Yes | No | No | Yes | Yes |
| Admin Notes | No | No | No | No | Yes |

## Location Capture Flow

```
┌──────────────┐
│  User types  │
│  location    │
└──────┬───────┘
       │
┌──────▼───────────────┐
│  Google Places API   │
│  Autocomplete        │
└──────┬───────────────┘
       │
┌──────▼───────────────┐
│  User selects        │
│  suggestion          │
└──────┬───────────────┘
       │
┌──────▼───────────────┐
│  Fetch place details │
│  (gmp-select event)  │
└──────┬───────────────┘
       │
┌──────▼───────────────┐
│  Extract & store:    │
│  - City              │
│  - CountryCode       │
│  - Latitude          │
│  - Longitude         │
│  - PlaceId           │
└──────────────────────┘
```

## Validation Rules

| Field | Validation |
|-------|------------|
| FirstName | Required, max 256 chars |
| LastName | Required, max 256 chars |
| BurnerName | Optional, max 256 chars |
| Bio | Optional, max 4000 chars |
| CountryCode | Optional, ISO 3166-1 alpha-2 |

## Admin Capabilities

1. **View Any Profile**: Full access to all profile fields
2. **Suspend Member**: Set IsSuspended = true with AdminNotes
3. **Unsuspend Member**: Clear suspension status
4. **Edit Admin Notes**: Internal notes not visible to member

## Related Features

- [Authentication](01-authentication.md) - Profile created after first login
- [Volunteer Status](05-volunteer-status.md) - Computed from profile approval + consents
- [Contact Fields](10-contact-fields.md) - Granular contact info visibility
- [Administration](09-administration.md) - Admin member management
- [Profile Pictures & Birthdays](14-profile-pictures-birthdays.md) - Custom profile pictures and birthday calendar
