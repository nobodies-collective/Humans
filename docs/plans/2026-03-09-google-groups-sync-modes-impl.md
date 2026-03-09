# Google Groups, Sync Modes & Navigation Restructure — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Google Groups sync with configurable sync modes per service, a TeamsAdmin role, and restructure the navigation into separate Board/Admin areas with a tabbed sync status page.

**Architecture:** Extends existing Clean Architecture layers. New `SyncServiceSettings` entity + enums in Domain. Unified sync code path in `GoogleWorkspaceSyncService` (preview/add/add+remove). New `BoardController` split from `AdminController`. Tabbed sync status page at `/Teams/Sync`. Admin-only sync settings at `/Admin/SyncSettings`.

**Tech Stack:** ASP.NET Core 10, EF Core 10 (PostgreSQL), NodaTime, Bootstrap 5, Google Admin SDK, xUnit + NSubstitute + InMemory EF

**Design doc:** `docs/plans/2026-03-09-google-groups-sync-modes-design.md`

---

### Task 1: Domain — New Enums

**Files:**
- Create: `src/Humans.Domain/Enums/SyncMode.cs`
- Create: `src/Humans.Domain/Enums/SyncServiceType.cs`
- Create: `src/Humans.Domain/Enums/SyncAction.cs`
- Test: `tests/Humans.Domain.Tests/Enums/EnumStringStabilityTests.cs` (extend existing)

**Step 1: Create SyncMode enum**

```csharp
// src/Humans.Domain/Enums/SyncMode.cs
namespace Humans.Domain.Enums;

/// <summary>
/// Controls what automated sync jobs do for a given service.
/// </summary>
public enum SyncMode
{
    /// <summary>No automatic sync — jobs skip this service entirely.</summary>
    None = 0,

    /// <summary>Automated jobs only add missing members.</summary>
    AddOnly = 1,

    /// <summary>Automated jobs add missing and remove extra members.</summary>
    AddAndRemove = 2
}
```

**Step 2: Create SyncServiceType enum**

```csharp
// src/Humans.Domain/Enums/SyncServiceType.cs
namespace Humans.Domain.Enums;

/// <summary>
/// Identifies an external sync service.
/// </summary>
public enum SyncServiceType
{
    GoogleDrive = 0,
    GoogleGroups = 1,
    Discord = 2
}
```

**Step 3: Create SyncAction enum**

```csharp
// src/Humans.Domain/Enums/SyncAction.cs
namespace Humans.Domain.Enums;

/// <summary>
/// What action to take during a sync operation.
/// Used as a parameter in sync methods — not persisted.
/// </summary>
public enum SyncAction
{
    /// <summary>Compute diff only, make no changes.</summary>
    Preview = 0,

    /// <summary>Compute diff and execute adds only.</summary>
    AddOnly = 1,

    /// <summary>Compute diff and execute adds + removes.</summary>
    AddAndRemove = 2
}
```

**Step 4: Add new enums to string stability tests**

Open `tests/Humans.Domain.Tests/Enums/EnumStringStabilityTests.cs` and add test cases for `SyncMode`, `SyncServiceType`, and `SyncAction` following the existing pattern. The test ensures enum string values don't change (since they're stored as strings in the DB).

Example additions:

```csharp
[Theory]
[InlineData(SyncMode.None, "None")]
[InlineData(SyncMode.AddOnly, "AddOnly")]
[InlineData(SyncMode.AddAndRemove, "AddAndRemove")]
public void SyncMode_StringValues_AreStable(SyncMode value, string expected)
{
    value.ToString().Should().Be(expected);
}

[Theory]
[InlineData(SyncServiceType.GoogleDrive, "GoogleDrive")]
[InlineData(SyncServiceType.GoogleGroups, "GoogleGroups")]
[InlineData(SyncServiceType.Discord, "Discord")]
public void SyncServiceType_StringValues_AreStable(SyncServiceType value, string expected)
{
    value.ToString().Should().Be(expected);
}
```

**Step 5: Run tests**

Run: `dotnet test tests/Humans.Domain.Tests --filter "EnumStringStability" -v minimal`
Expected: All pass

**Step 6: Commit**

```
feat: add SyncMode, SyncServiceType, SyncAction enums
```

---

### Task 2: Domain — SyncServiceSettings Entity + TeamsAdmin + GoogleGroupPrefix

**Files:**
- Create: `src/Humans.Domain/Entities/SyncServiceSettings.cs`
- Modify: `src/Humans.Domain/Constants/RoleNames.cs:28` — add TeamsAdmin
- Modify: `src/Humans.Domain/Entities/Team.cs:44` — add GoogleGroupPrefix + GoogleGroupEmail

**Step 1: Create SyncServiceSettings entity**

```csharp
// src/Humans.Domain/Entities/SyncServiceSettings.cs
using NodaTime;
using Humans.Domain.Enums;

namespace Humans.Domain.Entities;

/// <summary>
/// Per-service sync mode configuration. Controls what automated sync jobs do.
/// </summary>
public class SyncServiceSettings
{
    public Guid Id { get; init; }

    /// <summary>
    /// Which external service this setting applies to.
    /// </summary>
    public SyncServiceType ServiceType { get; init; }

    /// <summary>
    /// Current sync mode for automated jobs.
    /// </summary>
    public SyncMode SyncMode { get; set; } = SyncMode.None;

    /// <summary>
    /// When the mode was last changed.
    /// </summary>
    public Instant UpdatedAt { get; set; }

    /// <summary>
    /// Who last changed the mode. Null for seed data.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>
    /// Navigation property to the user who last changed the setting.
    /// </summary>
    public User? UpdatedByUser { get; set; }
}
```

**Step 2: Add TeamsAdmin to RoleNames**

In `src/Humans.Domain/Constants/RoleNames.cs`, after line 28 (`VolunteerCoordinator`), add:

```csharp
    /// <summary>
    /// Teams Administrator — can manage all teams, approve membership, assign leads,
    /// and configure Google Group prefixes system-wide.
    /// </summary>
    public const string TeamsAdmin = "TeamsAdmin";
```

**Step 3: Add GoogleGroupPrefix to Team**

In `src/Humans.Domain/Entities/Team.cs`, after `SystemTeamType` (line 44), add:

```csharp
    /// <summary>
    /// Google Group email prefix (before @nobodies.team). Null means no group for this team.
    /// </summary>
    public string? GoogleGroupPrefix { get; set; }

    /// <summary>
    /// Full Google Group email address, or null if no prefix is set.
    /// </summary>
    public string? GoogleGroupEmail => GoogleGroupPrefix != null
        ? $"{GoogleGroupPrefix}@nobodies.team"
        : null;
```

**Step 4: Build to verify**

Run: `dotnet build src/Humans.Domain`
Expected: Success

**Step 5: Commit**

```
feat: add SyncServiceSettings entity, TeamsAdmin role, GoogleGroupPrefix on Team
```

---

### Task 3: EF Configuration + Migration

**Files:**
- Create: `src/Humans.Infrastructure/Data/Configurations/SyncServiceSettingsConfiguration.cs`
- Modify: `src/Humans.Infrastructure/Data/HumansDbContext.cs:39` — add DbSet
- Modify: `src/Humans.Infrastructure/Data/Configurations/TeamConfiguration.cs:66-69` — add GoogleGroupPrefix config + ignore GoogleGroupEmail
- Create: migration file (auto-generated)

**Step 1: Create SyncServiceSettingsConfiguration**

```csharp
// src/Humans.Infrastructure/Data/Configurations/SyncServiceSettingsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using Humans.Domain.Entities;
using Humans.Domain.Enums;

namespace Humans.Infrastructure.Data.Configurations;

public class SyncServiceSettingsConfiguration : IEntityTypeConfiguration<SyncServiceSettings>
{
    private static readonly Instant SeedTimestamp = Instant.FromUtc(2026, 3, 9, 0, 0, 0);

    public void Configure(EntityTypeBuilder<SyncServiceSettings> builder)
    {
        builder.ToTable("sync_service_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ServiceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.SyncMode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasIndex(s => s.ServiceType)
            .IsUnique();

        builder.HasOne(s => s.UpdatedByUser)
            .WithMany()
            .HasForeignKey(s => s.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Seed one row per service type, all defaulting to None
        builder.HasData(
            new
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000001"),
                ServiceType = SyncServiceType.GoogleDrive,
                SyncMode = SyncMode.None,
                UpdatedAt = SeedTimestamp,
            },
            new
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000002"),
                ServiceType = SyncServiceType.GoogleGroups,
                SyncMode = SyncMode.None,
                UpdatedAt = SeedTimestamp,
            },
            new
            {
                Id = Guid.Parse("00000000-0000-0000-0002-000000000003"),
                ServiceType = SyncServiceType.Discord,
                SyncMode = SyncMode.None,
                UpdatedAt = SeedTimestamp,
            });
    }
}
```

**Step 2: Add DbSet to HumansDbContext**

In `src/Humans.Infrastructure/Data/HumansDbContext.cs`, after line 39 (`BoardVotes`), add:

```csharp
    public DbSet<SyncServiceSettings> SyncServiceSettings => Set<SyncServiceSettings>();
```

**Step 3: Add GoogleGroupPrefix to TeamConfiguration**

In `src/Humans.Infrastructure/Data/Configurations/TeamConfiguration.cs`:

After the `SystemTeamType` property config (line 38), add:

```csharp
        builder.Property(t => t.GoogleGroupPrefix)
            .HasMaxLength(64);
```

After the `SystemTeamType` index (line 66), add:

```csharp
        builder.HasIndex(t => t.GoogleGroupPrefix)
            .IsUnique()
            .HasFilter("\"GoogleGroupPrefix\" IS NOT NULL");
```

After `builder.Ignore(t => t.IsSystemTeam)` (line 69), add:

```csharp
        builder.Ignore(t => t.GoogleGroupEmail);
```

In the seed data anonymous objects (lines 72-132), add `GoogleGroupPrefix = (string?)null` to each seed entry. EF requires all properties in seed anonymous objects.

**Step 4: Generate migration**

Run: `dotnet ef migrations add AddSyncSettingsAndGroupPrefix --project src/Humans.Infrastructure --startup-project src/Humans.Web`

**Step 5: Review migration**

Read the generated migration file. Verify it:
- Creates `sync_service_settings` table with correct columns
- Adds `GoogleGroupPrefix` column to `teams` table (nullable)
- Creates unique filtered index on `GoogleGroupPrefix`
- Seeds 3 rows into `sync_service_settings`

**Step 6: Apply migration locally**

Run: `dotnet ef database update --project src/Humans.Infrastructure --startup-project src/Humans.Web`

**Step 7: Build**

Run: `dotnet build Humans.slnx`
Expected: Success

**Step 8: Commit**

```
feat: add sync_service_settings table and GoogleGroupPrefix column on teams
```

---

### Task 4: Application Layer — DTOs and Interface Changes

**Files:**
- Modify: `src/Humans.Application/DTOs/ResourceSyncDiff.cs` — enhance with per-member detail and linked teams
- Create: `src/Humans.Application/Interfaces/ISyncSettingsService.cs`
- Modify: `src/Humans.Application/Interfaces/IGoogleSyncService.cs:28-42` — replace 3 methods with unified API

**Step 1: Enhance ResourceSyncDiff with member-level detail**

Replace contents of `src/Humans.Application/DTOs/ResourceSyncDiff.cs`:

```csharp
using Humans.Domain.Enums;

namespace Humans.Application.DTOs;

/// <summary>
/// Sync status of a single member relative to a resource.
/// </summary>
public record MemberSyncStatus(
    string Email,
    string DisplayName,
    MemberSyncState State,
    List<string> TeamNames);

/// <summary>
/// Whether a member is correctly synced, missing, or extra.
/// </summary>
public enum MemberSyncState
{
    Correct,
    Missing,
    Extra
}

/// <summary>
/// Describes the drift between expected (DB) and actual (Google) state for a single resource.
/// </summary>
public class ResourceSyncDiff
{
    public Guid ResourceId { get; init; }
    public string ResourceName { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string? GoogleId { get; init; }
    public string? Url { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>All teams that link to this resource.</summary>
    public List<string> LinkedTeams { get; init; } = [];

    /// <summary>Per-member sync status (correct, missing, extra).</summary>
    public List<MemberSyncStatus> Members { get; init; } = [];

    // Convenience properties
    public List<string> MembersToAdd => Members
        .Where(m => m.State == MemberSyncState.Missing)
        .Select(m => m.Email).ToList();
    public List<string> MembersToRemove => Members
        .Where(m => m.State == MemberSyncState.Extra)
        .Select(m => m.Email).ToList();
    public bool IsInSync => !Members.Any(m => m.State != MemberSyncState.Correct) && ErrorMessage == null;
}

/// <summary>
/// Aggregated result of syncing/previewing resources of a given type.
/// </summary>
public class SyncPreviewResult
{
    public List<ResourceSyncDiff> Diffs { get; init; } = [];
    public int TotalResources => Diffs.Count;
    public int InSyncCount => Diffs.Count(d => d.IsInSync);
    public int DriftCount => Diffs.Count(d => !d.IsInSync && d.ErrorMessage == null);
    public int ErrorCount => Diffs.Count(d => d.ErrorMessage != null);
}
```

**Step 2: Create ISyncSettingsService**

```csharp
// src/Humans.Application/Interfaces/ISyncSettingsService.cs
using Humans.Domain.Entities;
using Humans.Domain.Enums;

namespace Humans.Application.Interfaces;

/// <summary>
/// Manages per-service sync mode settings.
/// </summary>
public interface ISyncSettingsService
{
    /// <summary>Get all sync settings.</summary>
    Task<List<SyncServiceSettings>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get sync mode for a specific service.</summary>
    Task<SyncMode> GetModeAsync(SyncServiceType serviceType, CancellationToken ct = default);

    /// <summary>Update sync mode for a service.</summary>
    Task UpdateModeAsync(SyncServiceType serviceType, SyncMode mode, Guid actorUserId, CancellationToken ct = default);
}
```

**Step 3: Update IGoogleSyncService**

In `src/Humans.Application/Interfaces/IGoogleSyncService.cs`, replace the three methods at lines 23-42 (`SyncResourcePermissionsAsync`, `SyncAllResourcesAsync`, `PreviewSyncAllAsync`) with:

```csharp
    /// <summary>
    /// Unified sync entry point. Computes diff for all active resources of the given type,
    /// then optionally executes adds/removes based on the action.
    /// Used by preview, manual actions, and scheduled jobs.
    /// </summary>
    Task<SyncPreviewResult> SyncResourcesByTypeAsync(
        GoogleResourceType resourceType,
        SyncAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync a single resource. Same logic as SyncResourcesByTypeAsync but for one resource.
    /// </summary>
    Task<ResourceSyncDiff> SyncSingleResourceAsync(
        Guid resourceId,
        SyncAction action,
        CancellationToken cancellationToken = default);
```

Add `using Humans.Domain.Enums;` to the top of the file if not already present.

**Step 4: Build to check compilation**

Run: `dotnet build Humans.slnx`

This will fail because `GoogleWorkspaceSyncService`, `StubGoogleSyncService`, `AdminController`, and `GoogleResourceReconciliationJob` reference the removed methods. That's expected — we fix those in the next tasks. For now, just confirm the interface and DTOs are correctly defined.

**Step 5: Commit**

```
feat: add ISyncSettingsService, update IGoogleSyncService with unified sync API, enhance DTOs
```

---

### Task 5: Infrastructure — SyncSettingsService + GoogleWorkspaceSettings Enhancement

**Files:**
- Create: `src/Humans.Infrastructure/Services/SyncSettingsService.cs`
- Modify: `src/Humans.Infrastructure/Configuration/GoogleWorkspaceSettings.cs:49-72` — add missing group settings
- Modify: `src/Humans.Web/Extensions/InfrastructureServiceCollectionExtensions.cs` — register ISyncSettingsService
- Modify: `src/Humans.Web/appsettings.json:25-36` — update GroupSettings defaults
- Test: `tests/Humans.Application.Tests/Services/SyncSettingsServiceTests.cs`

**Step 1: Write SyncSettingsService tests**

```csharp
// tests/Humans.Application.Tests/Services/SyncSettingsServiceTests.cs
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Testing;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;
using Humans.Infrastructure.Services;

namespace Humans.Application.Tests.Services;

public class SyncSettingsServiceTests : IDisposable
{
    private readonly HumansDbContext _dbContext;
    private readonly FakeClock _clock;
    private readonly SyncSettingsService _service;

    public SyncSettingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<HumansDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new HumansDbContext(options);
        _clock = new FakeClock(Instant.FromUtc(2026, 3, 9, 12, 0));

        // Seed the settings (InMemory doesn't run HasData)
        _dbContext.SyncServiceSettings.AddRange(
            new SyncServiceSettings
            {
                Id = Guid.NewGuid(),
                ServiceType = SyncServiceType.GoogleDrive,
                SyncMode = SyncMode.None,
                UpdatedAt = _clock.GetCurrentInstant()
            },
            new SyncServiceSettings
            {
                Id = Guid.NewGuid(),
                ServiceType = SyncServiceType.GoogleGroups,
                SyncMode = SyncMode.None,
                UpdatedAt = _clock.GetCurrentInstant()
            },
            new SyncServiceSettings
            {
                Id = Guid.NewGuid(),
                ServiceType = SyncServiceType.Discord,
                SyncMode = SyncMode.None,
                UpdatedAt = _clock.GetCurrentInstant()
            });
        _dbContext.SaveChanges();

        _service = new SyncSettingsService(_dbContext, _clock);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSettings()
    {
        var result = await _service.GetAllAsync();
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetModeAsync_ReturnsNone_ByDefault()
    {
        var mode = await _service.GetModeAsync(SyncServiceType.GoogleDrive);
        mode.Should().Be(SyncMode.None);
    }

    [Fact]
    public async Task UpdateModeAsync_ChangesModeAndTracksActor()
    {
        var actorId = Guid.NewGuid();
        _clock.Advance(Duration.FromHours(1));

        await _service.UpdateModeAsync(SyncServiceType.GoogleDrive, SyncMode.AddOnly, actorId);

        var mode = await _service.GetModeAsync(SyncServiceType.GoogleDrive);
        mode.Should().Be(SyncMode.AddOnly);

        var setting = await _dbContext.SyncServiceSettings
            .FirstAsync(s => s.ServiceType == SyncServiceType.GoogleDrive);
        setting.UpdatedByUserId.Should().Be(actorId);
        setting.UpdatedAt.Should().Be(_clock.GetCurrentInstant());
    }

    [Fact]
    public async Task GetModeAsync_ReturnsNone_WhenServiceTypeNotFound()
    {
        // Remove all settings
        _dbContext.SyncServiceSettings.RemoveRange(_dbContext.SyncServiceSettings);
        await _dbContext.SaveChangesAsync();

        var mode = await _service.GetModeAsync(SyncServiceType.GoogleDrive);
        mode.Should().Be(SyncMode.None);
    }

    public void Dispose() => _dbContext.Dispose();
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Humans.Application.Tests --filter "SyncSettingsService" -v minimal`
Expected: Build failure (SyncSettingsService doesn't exist yet)

**Step 3: Create SyncSettingsService**

```csharp
// src/Humans.Infrastructure/Services/SyncSettingsService.cs
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using Humans.Infrastructure.Data;

namespace Humans.Infrastructure.Services;

public class SyncSettingsService : ISyncSettingsService
{
    private readonly HumansDbContext _dbContext;
    private readonly IClock _clock;

    public SyncSettingsService(HumansDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<List<SyncServiceSettings>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbContext.SyncServiceSettings
            .Include(s => s.UpdatedByUser)
            .OrderBy(s => s.ServiceType)
            .ToListAsync(ct);
    }

    public async Task<SyncMode> GetModeAsync(SyncServiceType serviceType, CancellationToken ct = default)
    {
        var setting = await _dbContext.SyncServiceSettings
            .FirstOrDefaultAsync(s => s.ServiceType == serviceType, ct);
        return setting?.SyncMode ?? SyncMode.None;
    }

    public async Task UpdateModeAsync(SyncServiceType serviceType, SyncMode mode, Guid actorUserId, CancellationToken ct = default)
    {
        var setting = await _dbContext.SyncServiceSettings
            .FirstOrDefaultAsync(s => s.ServiceType == serviceType, ct)
            ?? throw new InvalidOperationException($"No sync setting found for {serviceType}");

        setting.SyncMode = mode;
        setting.UpdatedAt = _clock.GetCurrentInstant();
        setting.UpdatedByUserId = actorUserId;
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

**Step 4: Enhance GroupSettings**

In `src/Humans.Infrastructure/Configuration/GoogleWorkspaceSettings.cs`, replace the `GroupSettings` class (lines 49-72) with:

```csharp
/// <summary>
/// Default settings for Google Groups created by the system.
/// </summary>
public class GroupSettings
{
    public string WhoCanJoin { get; set; } = "INVITED_CAN_JOIN";
    public string WhoCanViewMembership { get; set; } = "ALL_MEMBERS_CAN_VIEW";
    public string WhoCanContactOwner { get; set; } = "ALL_MANAGERS_CAN_CONTACT";
    public string WhoCanPostMessage { get; set; } = "ANYONE_CAN_POST";
    public string WhoCanViewGroup { get; set; } = "ALL_MEMBERS_CAN_VIEW";
    public string WhoCanModerateMembers { get; set; } = "OWNERS_AND_MANAGERS";
    public bool AllowExternalMembers { get; set; } = true;
}
```

**Step 5: Update appsettings.json Groups section**

In `src/Humans.Web/appsettings.json`, update the `"Groups"` block to:

```json
    "Groups": {
      "WhoCanJoin": "INVITED_CAN_JOIN",
      "WhoCanViewMembership": "ALL_MEMBERS_CAN_VIEW",
      "WhoCanContactOwner": "ALL_MANAGERS_CAN_CONTACT",
      "WhoCanPostMessage": "ANYONE_CAN_POST",
      "WhoCanViewGroup": "ALL_MEMBERS_CAN_VIEW",
      "WhoCanModerateMembers": "OWNERS_AND_MANAGERS",
      "AllowExternalMembers": true
    }
```

**Step 6: Register ISyncSettingsService in DI**

In `src/Humans.Web/Extensions/InfrastructureServiceCollectionExtensions.cs`, add near the other service registrations (before the Google credentials conditional block):

```csharp
        services.AddScoped<ISyncSettingsService, SyncSettingsService>();
```

Add `using Humans.Infrastructure.Services;` if not already present, and `using Humans.Application.Interfaces;`.

**Step 7: Run tests**

Run: `dotnet test tests/Humans.Application.Tests --filter "SyncSettingsService" -v minimal`
Expected: All pass

**Step 8: Commit**

```
feat: add SyncSettingsService and enhance GroupSettings configuration
```

---

### Task 6: Infrastructure — Unified Sync Code Path in GoogleWorkspaceSyncService

**Files:**
- Modify: `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs` — replace `SyncResourcePermissionsAsync`, `SyncAllResourcesAsync`, `PreviewSyncAllAsync` with `SyncResourcesByTypeAsync`, `SyncSingleResourceAsync`
- Modify: `src/Humans.Infrastructure/Services/StubGoogleSyncService.cs` — update to match new interface

**This is the largest single task. The key principle: one method computes diffs, the `SyncAction` parameter controls what gets executed.**

**Step 1: Implement SyncResourcesByTypeAsync in GoogleWorkspaceSyncService**

Replace `PreviewSyncAllAsync` (around line 541), `SyncAllResourcesAsync` (around line 436), and `SyncResourcePermissionsAsync` (around line 361) with the new methods. Keep the existing private helper methods (`ListDrivePermissionsAsync`, `ListGroupMembersAsync`, etc.) as they are.

The new `SyncResourcesByTypeAsync` should:

1. Load all active resources of the given `GoogleResourceType`, including `Team.Members` and member `User` navigation
2. **For Drive resources**: group by `GoogleId` to handle multi-team sharing. For each unique GoogleId:
   - Compute expected emails = union of all linked teams' active members' emails
   - Collect linked team names
   - Use existing `ListDrivePermissionsAsync` to get current state
   - Build `MemberSyncStatus` list (Correct/Missing/Extra)
   - If action != Preview: execute adds (and removes if AddAndRemove)
3. **For Group resources**: one per team. For each:
   - Expected emails = team's active members' emails
   - Use existing `ListGroupMembersAsync` to get current state
   - Build `MemberSyncStatus` list
   - If action != Preview: execute adds (and removes if AddAndRemove)
4. Return `SyncPreviewResult` with all diffs

The new `SyncSingleResourceAsync` should:
1. Load the single resource by ID with team members
2. For Drive: find all resources with same GoogleId, compute union of expected members
3. For Group: expected = team members
4. Same diff + optional execution logic
5. Return single `ResourceSyncDiff`

**Step 2: Remove the `// Removal disabled` guards**

The removal code is currently stubbed out. Now that sync mode controls this behavior, the actual removal code should work when `action == SyncAction.AddAndRemove`. Use the existing `RemoveUserFromGroupAsync` and Drive permission deletion logic.

**Step 3: Update StubGoogleSyncService**

Replace the 3 stub methods to match the new interface. Stubs return empty `SyncPreviewResult`/`ResourceSyncDiff` and log the call.

**Step 4: Build**

Run: `dotnet build Humans.slnx`
Expected: Will still fail on `AdminController` and `GoogleResourceReconciliationJob` (they reference old methods). That's OK — fixed in next tasks.

**Step 5: Commit**

```
feat: implement unified sync code path with SyncAction parameter
```

---

### Task 7: Infrastructure — Google Group Creation with Settings

**Files:**
- Modify: `src/Humans.Infrastructure/Services/GoogleWorkspaceSyncService.cs` — update `ProvisionTeamGroupAsync` to apply `GroupSettings`

**Step 1: Update ProvisionTeamGroupAsync**

The existing method creates a group but doesn't apply the configured `GroupSettings` (this is todo P1-13). Inject `IOptions<GoogleWorkspaceSettings>` into the constructor (it may already be there). After creating the group via the Admin SDK `Directory.Groups.Insert`, add a call to the Groups Settings API to apply settings from `_settings.Groups`:

```csharp
// After creating the group, apply settings
var groupSettings = new Google.Apis.Groupssettings.v1.Data.Groups
{
    WhoCanJoin = _settings.Groups.WhoCanJoin,
    WhoCanViewMembership = _settings.Groups.WhoCanViewMembership,
    WhoCanContactOwner = _settings.Groups.WhoCanContactOwner,
    WhoCanPostMessage = _settings.Groups.WhoCanPostMessage,
    WhoCanViewGroup = _settings.Groups.WhoCanViewGroup,
    WhoCanModerateMembers = _settings.Groups.WhoCanModerateMembers,
    AllowExternalMembers = _settings.Groups.AllowExternalMembers ? "true" : "false",
};
```

Note: The Google Groups Settings API uses a separate service (`GroupssettingsService`). You may need to add `Google.Apis.Groupssettings.v1` NuGet package and initialize a `GroupssettingsService` alongside the existing `DirectoryService`. Check if it's already available.

**Step 2: Add logic for group prefix lifecycle**

Create a new method (or add to an existing service):

```csharp
/// <summary>
/// Called when GoogleGroupPrefix is set on a team.
/// Creates or links the Google Group.
/// </summary>
Task EnsureTeamGroupAsync(Guid teamId, CancellationToken ct = default);
```

Logic:
1. Load team with GoogleResources
2. If active Group resource already exists, done
3. Check if group exists in Google (`Directory.Groups.Get(email)`)
4. If exists: create `GoogleResource` row pointing to it
5. If not exists: call `ProvisionTeamGroupAsync` to create it

**Step 3: Build and verify**

Run: `dotnet build src/Humans.Infrastructure`

**Step 4: Commit**

```
feat: apply GroupSettings on group creation and add group prefix lifecycle
```

---

### Task 8: Infrastructure — Mode-Gated Reconciliation Job

**Files:**
- Modify: `src/Humans.Infrastructure/Jobs/GoogleResourceReconciliationJob.cs` — inject ISyncSettingsService, gate by mode
- Test: `tests/Humans.Application.Tests/Jobs/GoogleResourceReconciliationJobTests.cs` (new)

**Step 1: Write tests**

```csharp
// tests/Humans.Application.Tests/Jobs/GoogleResourceReconciliationJobTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NodaTime.Testing;
using NSubstitute;
using Humans.Application.DTOs;
using Humans.Application.Interfaces;
using Humans.Domain.Enums;
using Humans.Infrastructure.Jobs;
using Humans.Infrastructure.Services;

namespace Humans.Application.Tests.Jobs;

public class GoogleResourceReconciliationJobTests
{
    private readonly IGoogleSyncService _syncService = Substitute.For<IGoogleSyncService>();
    private readonly ISyncSettingsService _settingsService = Substitute.For<ISyncSettingsService>();
    private readonly HumansMetricsService _metrics;
    private readonly GoogleResourceReconciliationJob _job;

    public GoogleResourceReconciliationJobTests()
    {
        _metrics = new HumansMetricsService();
        var clock = new FakeClock(Instant.FromUtc(2026, 3, 9, 3, 0));
        _job = new GoogleResourceReconciliationJob(
            _syncService, _settingsService, _metrics,
            NullLogger<GoogleResourceReconciliationJob>.Instance, clock);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsDrive_WhenModeIsNone()
    {
        _settingsService.GetModeAsync(SyncServiceType.GoogleDrive).Returns(SyncMode.None);
        _settingsService.GetModeAsync(SyncServiceType.GoogleGroups).Returns(SyncMode.None);

        await _job.ExecuteAsync();

        await _syncService.DidNotReceive().SyncResourcesByTypeAsync(
            Arg.Any<GoogleResourceType>(), Arg.Any<SyncAction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddOnly_WhenModeIsAddOnly()
    {
        _settingsService.GetModeAsync(SyncServiceType.GoogleDrive).Returns(SyncMode.AddOnly);
        _settingsService.GetModeAsync(SyncServiceType.GoogleGroups).Returns(SyncMode.None);
        _syncService.SyncResourcesByTypeAsync(Arg.Any<GoogleResourceType>(), Arg.Any<SyncAction>(), Arg.Any<CancellationToken>())
            .Returns(new SyncPreviewResult());

        await _job.ExecuteAsync();

        await _syncService.Received(1).SyncResourcesByTypeAsync(
            GoogleResourceType.DriveFolder, SyncAction.AddOnly, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CallsAddAndRemove_ForGroups()
    {
        _settingsService.GetModeAsync(SyncServiceType.GoogleDrive).Returns(SyncMode.None);
        _settingsService.GetModeAsync(SyncServiceType.GoogleGroups).Returns(SyncMode.AddAndRemove);
        _syncService.SyncResourcesByTypeAsync(Arg.Any<GoogleResourceType>(), Arg.Any<SyncAction>(), Arg.Any<CancellationToken>())
            .Returns(new SyncPreviewResult());

        await _job.ExecuteAsync();

        await _syncService.Received(1).SyncResourcesByTypeAsync(
            GoogleResourceType.Group, SyncAction.AddAndRemove, Arg.Any<CancellationToken>());
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Humans.Application.Tests --filter "GoogleResourceReconciliationJob" -v minimal`
Expected: Build failure

**Step 3: Update GoogleResourceReconciliationJob**

Replace the contents of `src/Humans.Infrastructure/Jobs/GoogleResourceReconciliationJob.cs`:

```csharp
using Microsoft.Extensions.Logging;
using NodaTime;
using Humans.Application.Interfaces;
using Humans.Domain.Enums;
using Humans.Infrastructure.Services;

namespace Humans.Infrastructure.Jobs;

/// <summary>
/// Scheduled job that reconciles Google resources based on per-service sync mode settings.
/// </summary>
public class GoogleResourceReconciliationJob
{
    private readonly IGoogleSyncService _googleSyncService;
    private readonly ISyncSettingsService _syncSettingsService;
    private readonly HumansMetricsService _metrics;
    private readonly ILogger<GoogleResourceReconciliationJob> _logger;
    private readonly IClock _clock;

    public GoogleResourceReconciliationJob(
        IGoogleSyncService googleSyncService,
        ISyncSettingsService syncSettingsService,
        HumansMetricsService metrics,
        ILogger<GoogleResourceReconciliationJob> logger,
        IClock clock)
    {
        _googleSyncService = googleSyncService;
        _syncSettingsService = syncSettingsService;
        _metrics = metrics;
        _logger = logger;
        _clock = clock;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Google resource reconciliation at {Time}", _clock.GetCurrentInstant());

        try
        {
            await SyncServiceAsync(SyncServiceType.GoogleDrive, GoogleResourceType.DriveFolder, cancellationToken);
            await SyncServiceAsync(SyncServiceType.GoogleGroups, GoogleResourceType.Group, cancellationToken);

            _metrics.RecordJobRun("google_resource_reconciliation", "success");
            _logger.LogInformation("Completed Google resource reconciliation");
        }
        catch (Exception ex)
        {
            _metrics.RecordJobRun("google_resource_reconciliation", "failure");
            _logger.LogError(ex, "Error during Google resource reconciliation");
            throw;
        }
    }

    private async Task SyncServiceAsync(SyncServiceType serviceType, GoogleResourceType resourceType, CancellationToken ct)
    {
        var mode = await _syncSettingsService.GetModeAsync(serviceType, ct);
        if (mode == SyncMode.None)
        {
            _logger.LogInformation("Skipping {ServiceType} sync — mode is None", serviceType);
            return;
        }

        var action = mode switch
        {
            SyncMode.AddOnly => SyncAction.AddOnly,
            SyncMode.AddAndRemove => SyncAction.AddAndRemove,
            _ => SyncAction.Preview
        };

        _logger.LogInformation("Syncing {ServiceType} resources with action {Action}", serviceType, action);
        await _googleSyncService.SyncResourcesByTypeAsync(resourceType, action, ct);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test tests/Humans.Application.Tests --filter "GoogleResourceReconciliationJob" -v minimal`
Expected: All pass

**Step 5: Commit**

```
feat: make reconciliation job respect per-service sync mode settings
```

---

### Task 9: Authorization — TeamsAdmin Wiring

**Files:**
- Modify: `src/Humans.Web/Authorization/MembershipRequiredFilter.cs:39-40` — add TeamsAdmin bypass
- Modify: `src/Humans.Infrastructure/Services/TeamResourceService.cs:392-408` — add TeamsAdmin to CanManageTeamResourcesAsync
- Modify: `src/Humans.Infrastructure/Services/TeamService.cs` — add TeamsAdmin to CanUserApproveRequestsForTeamAsync
- Modify: `src/Humans.Web/Controllers/AdminController.cs:595-597,618-620,1047-1063` — add TeamsAdmin to CanManageRole available roles

**Step 1: Add TeamsAdmin to MembershipRequiredFilter bypass**

In `src/Humans.Web/Authorization/MembershipRequiredFilter.cs`, modify lines 39-40 to add TeamsAdmin:

```csharp
        if (user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.Board) ||
            user.IsInRole(RoleNames.TeamsAdmin) ||
            user.IsInRole(RoleNames.ConsentCoordinator) || user.IsInRole(RoleNames.VolunteerCoordinator))
```

**Step 2: Add TeamsAdmin to CanManageTeamResourcesAsync**

In `src/Humans.Infrastructure/Services/TeamResourceService.cs`, in `CanManageTeamResourcesAsync` (around line 392), after the Board member check, add:

```csharp
    // TeamsAdmin can always manage resources
    var isTeamsAdmin = await IsUserTeamsAdminAsync(userId, ct);
    if (isTeamsAdmin)
    {
        return true;
    }
```

Add the helper method:

```csharp
private async Task<bool> IsUserTeamsAdminAsync(Guid userId, CancellationToken ct)
{
    var now = _clock.GetCurrentInstant();
    return await _dbContext.RoleAssignments.AnyAsync(
        ra => ra.UserId == userId &&
              ra.RoleName == RoleNames.TeamsAdmin &&
              ra.ValidFrom <= now &&
              (ra.ValidTo == null || ra.ValidTo > now), ct);
}
```

(Check if `_clock` and `_dbContext` are available — `TeamResourceService` may need them injected. If `_teamService` has an equivalent method, use that instead.)

**Step 3: Add TeamsAdmin to CanUserApproveRequestsForTeamAsync**

In `src/Humans.Infrastructure/Services/TeamService.cs`, find `CanUserApproveRequestsForTeamAsync` and add a TeamsAdmin check alongside the Admin/Board checks.

**Step 4: Add TeamsAdmin to CanManageRole available roles**

In `src/Humans.Web/Controllers/AdminController.cs`, update `AddRole` GET and POST methods (lines 595-597 and 618-620) to include `RoleNames.TeamsAdmin` in the available roles arrays:

```csharp
// Admin can assign all roles including TeamsAdmin
AvailableRoles = User.IsInRole(RoleNames.Admin)
    ? [RoleNames.Admin, RoleNames.Board, RoleNames.TeamsAdmin, RoleNames.ConsentCoordinator, RoleNames.VolunteerCoordinator]
    : [RoleNames.Board, RoleNames.TeamsAdmin, RoleNames.ConsentCoordinator, RoleNames.VolunteerCoordinator]
```

Also update `CanManageRole` to allow Board to manage TeamsAdmin:

```csharp
return string.Equals(roleName, RoleNames.Board, StringComparison.Ordinal) ||
       string.Equals(roleName, RoleNames.TeamsAdmin, StringComparison.Ordinal) ||
       string.Equals(roleName, RoleNames.ConsentCoordinator, StringComparison.Ordinal) ||
       string.Equals(roleName, RoleNames.VolunteerCoordinator, StringComparison.Ordinal);
```

**Step 5: Build**

Run: `dotnet build Humans.slnx`

**Step 6: Commit**

```
feat: wire TeamsAdmin role into authorization checks
```

---

### Task 10: Web — BoardController Split from AdminController

**Files:**
- Create: `src/Humans.Web/Controllers/BoardController.cs`
- Modify: `src/Humans.Web/Controllers/AdminController.cs` — remove Board routes, update to Admin-only
- Move/create: `src/Humans.Web/Views/Board/` directory with moved views
- Modify: `src/Humans.Web/Authorization/MembershipRequiredFilter.cs:15-26` — add "Board" to exempt controllers

**Step 1: Create BoardController**

Move these actions from `AdminController` to a new `BoardController`:
- `Index` (dashboard) → `Board/Index`
- `Humans` → `Board/Humans`
- `HumanDetail` → `Board/Humans/{id}`
- `Applications` → `Board/Applications`
- `ApplicationDetail` → `Board/Applications/{id}`
- `SuspendHuman`, `UnsuspendHuman`, `ApproveVolunteer`, `RejectSignup` → `Board/Humans/{id}/...`
- `Roles`, `AddRole`, `EndRole` → `Board/Roles/...`
- `AuditLog`, `CheckDriveActivity` → `Board/AuditLog/...`
- `Teams`, `CreateTeam`, `EditTeam`, `DeleteTeam` → `Board/Teams/...`
- `HumanGoogleSyncAudit` → `Board/Humans/{id}/GoogleSyncAudit`

`BoardController` should have `[Authorize(Roles = "Board,Admin")]` and `[Route("Board")]`.

Copy the constructor dependencies that these actions need.

**Step 2: Refactor AdminController**

Keep only Admin-only actions in `AdminController`:
- `PurgeHuman` → remains at `Admin/Humans/{id}/Purge`
- `SyncSystemTeams` → `Admin/SyncSystemTeams`
- `Configuration` → `Admin/Configuration`
- `EmailPreview` → `Admin/EmailPreview`
- `DbVersion` → `Admin/DbVersion`

Update `AdminController`'s class-level `[Authorize(Roles = "Admin")]` (was `"Board,Admin"`).

Remove `CanManageRole` from `AdminController` — move it to `BoardController`.

**Step 3: Move views**

Create `src/Humans.Web/Views/Board/` directory. Move/copy the relevant views from `Views/Admin/`:
- `Index.cshtml` → `Views/Board/Index.cshtml`
- `Humans.cshtml`, `HumanDetail.cshtml` → `Views/Board/`
- `Applications.cshtml`, `ApplicationDetail.cshtml` → `Views/Board/`
- `Roles.cshtml`, `AddRole.cshtml` → `Views/Board/`
- `AuditLog.cshtml` → `Views/Board/`
- `Teams.cshtml`, `CreateTeam.cshtml`, `EditTeam.cshtml` → `Views/Board/`
- `GoogleSyncAudit.cshtml` → `Views/Board/`

Update any `asp-controller="Admin"` references within these views to `asp-controller="Board"`. Also update `Url.Action` calls that reference Admin actions.

**Step 4: Add "Board" to exempt controllers**

In `MembershipRequiredFilter.cs`, add `"Board"` to `ExemptControllers` (it has its own `[Authorize(Roles = ...)]` gate):

```csharp
"Board",       // Has its own Roles = "Board,Admin" gate
```

**Step 5: Build and verify**

Run: `dotnet build Humans.slnx`

**Step 6: Test navigation**

Run: `dotnet run --project src/Humans.Web` and verify Board routes work.

**Step 7: Commit**

```
refactor: split Board routes from AdminController into BoardController
```

---

### Task 11: Web — Navigation Restructure

**Files:**
- Modify: `src/Humans.Web/Views/Shared/_Layout.cshtml:59-76` — rename nav items, add Board/Admin split
- Update localization resource files for new nav strings if needed

**Step 1: Update nav section**

Replace lines 59-76 in `_Layout.cshtml` with:

```html
                        @if (User.IsInRole("ConsentCoordinator") || User.IsInRole("VolunteerCoordinator") || User.IsInRole("Admin") || User.IsInRole("Board"))
                        {
                            <li class="nav-item">
                                <a class="nav-link" asp-area="" asp-controller="OnboardingReview" asp-action="Index">@Localizer["Nav_Review"] @await Component.InvokeAsync("NavBadges", new { queue = "review" })</a>
                            </li>
                        }
                        @if (User.IsInRole("Board") || User.IsInRole("Admin"))
                        {
                            <li class="nav-item">
                                <a class="nav-link" asp-area="" asp-controller="OnboardingReview" asp-action="BoardVoting">@Localizer["Nav_Voting"] @await Component.InvokeAsync("NavBadges", new { queue = "voting" })</a>
                            </li>
                        }
                        @if (User.IsInRole("Board") || User.IsInRole("Admin"))
                        {
                            <li class="nav-item">
                                <a class="nav-link" asp-area="" asp-controller="Board" asp-action="Index">@Localizer["Nav_Board"]</a>
                            </li>
                        }
                        @if (User.IsInRole("Admin"))
                        {
                            <li class="nav-item">
                                <a class="nav-link" asp-area="" asp-controller="Admin" asp-action="Index">@Localizer["Nav_Admin"]</a>
                            </li>
                        }
```

Also update line 42 to include TeamsAdmin:

```csharp
var isActiveMember = User.HasClaim("ActiveMember", "true") || User.IsInRole("Admin") || User.IsInRole("Board") || User.IsInRole("TeamsAdmin");
```

**Step 2: Update localization strings**

In the localization resource files, add/update:
- `Nav_OnboardingReview` → rename key to `Nav_Review` (or update value, depending on approach)
- `Nav_BoardVoting` → rename key to `Nav_Voting`
- Add `Nav_Board` = "Board"
- Keep `Nav_Admin` = "Admin"

Check the resource files location and update accordingly.

**Step 3: Build and verify**

Run: `dotnet build Humans.slnx`

**Step 4: Commit**

```
refactor: restructure navigation — Review, Voting, Board, Admin split
```

---

### Task 12: Web — Admin-Only Pages (Sync Settings + System Pages)

**Files:**
- Create: `src/Humans.Web/Views/Admin/Index.cshtml` (new admin dashboard)
- Create: `src/Humans.Web/Views/Admin/SyncSettings.cshtml`
- Create: `src/Humans.Web/Models/SyncSettingsViewModels.cs`
- Modify: `src/Humans.Web/Controllers/AdminController.cs` — add SyncSettings GET/POST actions

**Step 1: Create SyncSettings view model**

```csharp
// src/Humans.Web/Models/SyncSettingsViewModels.cs
using Humans.Domain.Enums;

namespace Humans.Web.Models;

public class SyncSettingsViewModel
{
    public List<SyncServiceSettingViewModel> Settings { get; set; } = [];
}

public class SyncServiceSettingViewModel
{
    public SyncServiceType ServiceType { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public SyncMode CurrentMode { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
}
```

**Step 2: Add SyncSettings actions to AdminController**

```csharp
[HttpGet("SyncSettings")]
public async Task<IActionResult> SyncSettings([FromServices] ISyncSettingsService syncSettingsService)
{
    var settings = await syncSettingsService.GetAllAsync();
    var viewModel = new SyncSettingsViewModel
    {
        Settings = settings.Select(s => new SyncServiceSettingViewModel
        {
            ServiceType = s.ServiceType,
            ServiceName = s.ServiceType.ToString(),
            CurrentMode = s.SyncMode,
            UpdatedAt = s.UpdatedAt.ToDateTimeUtc(),
            UpdatedByName = s.UpdatedByUser?.DisplayName
        }).ToList()
    };
    return View(viewModel);
}

[HttpPost("SyncSettings")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateSyncSetting(
    [FromServices] ISyncSettingsService syncSettingsService,
    SyncServiceType serviceType, SyncMode mode)
{
    var currentUser = await _userManager.GetUserAsync(User);
    if (currentUser == null) return Unauthorized();

    await syncSettingsService.UpdateModeAsync(serviceType, mode, currentUser.Id);

    TempData["SuccessMessage"] = $"Sync mode for {serviceType} updated to {mode}.";
    return RedirectToAction(nameof(SyncSettings));
}
```

**Step 3: Create SyncSettings view**

Create `src/Humans.Web/Views/Admin/SyncSettings.cshtml` — a simple page with a dropdown per service and a save button. Use Bootstrap cards, one per service with the mode dropdown and last-updated info.

**Step 4: Update Admin Index view**

Create a new `src/Humans.Web/Views/Admin/Index.cshtml` that serves as the Admin dashboard with links to:
- Sync Settings
- System Health (existing Configuration page)
- System Team Sync trigger
- Hangfire dashboard link
- GDPR Purge info
- Email Preview

**Step 5: Build and verify**

Run: `dotnet build Humans.slnx`

**Step 6: Commit**

```
feat: add Admin sync settings page and admin dashboard
```

---

### Task 13: Web — Sync Status Page at /Teams/Sync

**Files:**
- Create: `src/Humans.Web/Models/TeamSyncViewModels.cs`
- Modify: `src/Humans.Web/Controllers/TeamController.cs` — add Sync action
- Create: `src/Humans.Web/Views/Teams/Sync.cshtml`
- Delete: `src/Humans.Web/Views/Admin/GoogleSync.cshtml` (replaced)

**Step 1: Create TeamSync view models**

```csharp
// src/Humans.Web/Models/TeamSyncViewModels.cs
using Humans.Application.DTOs;
using Humans.Domain.Enums;

namespace Humans.Web.Models;

public class TeamSyncViewModel
{
    public bool CanExecuteActions { get; set; }
}
```

The actual sync data (diffs) is loaded via AJAX per-tab, so the initial page model is minimal.

**Step 2: Add API endpoints for AJAX diff loading**

Add to `TeamController`:

```csharp
[HttpGet("Sync")]
[Authorize(Roles = "TeamsAdmin,Board,Admin")]
public IActionResult Sync()
{
    var viewModel = new TeamSyncViewModel
    {
        CanExecuteActions = User.IsInRole("Admin")
    };
    return View(viewModel);
}

[HttpGet("Sync/Preview/{resourceType}")]
[Authorize(Roles = "TeamsAdmin,Board,Admin")]
public async Task<IActionResult> SyncPreview(GoogleResourceType resourceType)
{
    var result = await _googleSyncService.SyncResourcesByTypeAsync(resourceType, SyncAction.Preview);
    return Json(result);
}

[HttpPost("Sync/Execute/{resourceId}")]
[Authorize(Roles = "Admin")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SyncExecute(Guid resourceId, [FromQuery] SyncAction action)
{
    var result = await _googleSyncService.SyncSingleResourceAsync(resourceId, action);
    return Json(result);
}

[HttpPost("Sync/ExecuteAll/{resourceType}")]
[Authorize(Roles = "Admin")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SyncExecuteAll(GoogleResourceType resourceType, [FromQuery] SyncAction action)
{
    var result = await _googleSyncService.SyncResourcesByTypeAsync(resourceType, action);
    return Json(result);
}
```

Add `IGoogleSyncService` to `TeamController`'s constructor.

**Step 3: Create the Sync view**

Create `src/Humans.Web/Views/Teams/Sync.cshtml` with:
- Bootstrap tabs: Drives | Groups | Discord (disabled)
- Each tab has stat cards (populated by JS after AJAX loads)
- "Show changes only" toggle (default on)
- Per-resource cards with expandable member detail table
- Admin-only action buttons (hidden via `Model.CanExecuteActions`)
- JavaScript for:
  - Loading diff data via fetch to `/Teams/Sync/Preview/DriveFolder` etc.
  - Rendering resource cards with member tables
  - Toggling "show changes only" filter
  - Confirmation modals for actions
  - POST for execute actions with CSRF token

**Step 4: Add "Sync Status" button to Teams index**

In `TeamController.Index`, set a `CanViewSync` flag on the view model (true for TeamsAdmin/Board/Admin). In the Teams index view, add a "Sync Status" button in the top-right when `CanViewSync` is true.

**Step 5: Remove old GoogleSync view**

Delete `src/Humans.Web/Views/Admin/GoogleSync.cshtml`.

Remove the `GoogleSync` and `GoogleSyncApply` actions from `AdminController` (they moved to TeamController).

The `GoogleSyncResourceAudit` action should move to `BoardController` or `TeamController` as appropriate.

**Step 6: Build and verify**

Run: `dotnet build Humans.slnx`

**Step 7: Commit**

```
feat: add tabbed sync status page at /Teams/Sync
```

---

### Task 14: Web — Team Edit with GoogleGroupPrefix

**Files:**
- Modify: `src/Humans.Web/Models/TeamViewModels.cs:154-168` — add GoogleGroupPrefix to EditTeamViewModel
- Modify: `src/Humans.Web/Views/Board/EditTeam.cshtml` — add group prefix field
- Modify: `src/Humans.Web/Controllers/BoardController.cs` (EditTeam actions) — handle prefix save + group provisioning
- Modify: `src/Humans.Application/Interfaces/ITeamService.cs` — add UpdateTeamAsync overload or parameter for prefix
- Modify: `src/Humans.Infrastructure/Services/TeamService.cs` — implement

**Step 1: Add GoogleGroupPrefix to EditTeamViewModel**

```csharp
// In EditTeamViewModel, add:
[StringLength(64)]
[RegularExpression(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", ErrorMessage = "Only lowercase letters, numbers, and hyphens allowed")]
public string? GoogleGroupPrefix { get; set; }

public string? GoogleGroupEmail { get; set; }  // Display only
```

**Step 2: Update EditTeam GET action**

Add `GoogleGroupPrefix = team.GoogleGroupPrefix` and `GoogleGroupEmail = team.GoogleGroupEmail` to the view model.

**Step 3: Update EditTeam POST action**

Pass `model.GoogleGroupPrefix` to the service. If the prefix changed from null to a value, trigger `EnsureTeamGroupAsync`. If changed from a value to null, soft-deactivate the group resource.

**Step 4: Update EditTeam view**

Add a form field for "Google Group" with `@nobodies.team` suffix shown as static text. Only visible to TeamsAdmin/Board/Admin. Show current group email if set.

**Step 5: Authorization check**

Only TeamsAdmin, Board, and Admin can see/edit the GoogleGroupPrefix field. Leads cannot.

**Step 6: Build and test**

Run: `dotnet build Humans.slnx`

**Step 7: Commit**

```
feat: add GoogleGroupPrefix field to team edit page
```

---

### Task 15: Update Feature Docs + Final Cleanup

**Files:**
- Modify: `docs/features/07-google-integration.md` — document sync modes, unified code path, group prefix lifecycle
- Modify: `docs/features/06-teams.md` — document TeamsAdmin role, GoogleGroupPrefix
- Modify: `docs/features/09-administration.md` — document Board/Admin split, sync settings page
- Modify: `docs/features/08-background-jobs.md` — document mode-gated reconciliation
- Modify: `todos.md` — mark P1-13 as resolved

**Step 1: Update each feature doc**

Update the docs to reflect the new reality. Key additions:
- Sync modes (None/AddOnly/AddAndRemove) per service
- TeamsAdmin role capabilities
- GoogleGroupPrefix and group lifecycle
- Board vs Admin area split with new URLs
- /Teams/Sync page replaces /Admin/GoogleSync

**Step 2: Update todos.md**

Move P1-13 (apply group settings during provisioning) to completed with commit reference.

**Step 3: Run full test suite**

Run: `dotnet test Humans.slnx`
Expected: All pass

**Step 4: Run full build**

Run: `dotnet build Humans.slnx`
Expected: Success with no warnings

**Step 5: Commit**

```
docs: update feature specs for sync modes, TeamsAdmin, and Board/Admin split
```

---

## Dependency Graph

```
Task 1 (Enums) ─────────────────┐
Task 2 (Entities + Role) ───────┤
                                 ├─> Task 3 (EF + Migration)
                                 │
Task 4 (DTOs + Interfaces) ─────┤
                                 ├─> Task 5 (SyncSettingsService + GroupSettings)
                                 ├─> Task 6 (Unified sync code path) ──> Task 7 (Group creation)
                                 ├─> Task 8 (Reconciliation job)
                                 │
Task 9 (Authorization wiring) ──┤
                                 ├─> Task 10 (BoardController split)
                                 ├─> Task 11 (Nav restructure)
                                 ├─> Task 12 (Admin sync settings page)
                                 ├─> Task 13 (Sync status page)
                                 ├─> Task 14 (Team edit + prefix)
                                 │
Task 15 (Docs + cleanup) ───────┘
```

Tasks 1-3 must be sequential (domain → config → migration).
Tasks 4-8 can be partially parallelized (interfaces → implementations).
Tasks 9-14 depend on earlier tasks but can be done in any order.
Task 15 is last.
