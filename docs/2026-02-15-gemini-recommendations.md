# Architectural Recommendations (Pre-Production)

**Date:** Sunday, February 15, 2026
**Author:** Gemini CLI Agent

As the Profiles.net project approaches production, three key areas have been identified for refactoring. Implementing these changes now will improve long-term scalability, maintainability, and security.

---

## 1. Refactor Profile Picture Storage
**Priority:** High (Scalability & Performance)

Currently, `ProfilePictureData` is stored as a `byte[]` directly within the `profiles` table. While the project anticipates a small user base (~500), storing BLOBs in-line with metadata leads to database bloat. This can degrade performance for common operations like table scans, index rebuilds, and backups.

*   **Recommendation:** Decouple binary data from the profile metadata.
    *   **Option A (Immediate):** Move the binary data to a separate `ProfilePictures` table with a one-to-one relationship. This keeps the `profiles` table "lean" for standard queries.
    *   **Option B (Strategic):** Implement an abstraction for `IFileStorageService` and move pictures to an external provider (e.g., S3, Azure Blob Storage, or a dedicated volume).
*   **Impact of Waiting:** Changing the schema for a core entity after the database is populated and the application is live is significantly more complex and risky.

## 2. Modularize Infrastructure and Job Configuration
**Priority:** Medium (Maintainability)

The `src/Humans.Web/Program.cs` file is currently over 400 lines long and contains low-level infrastructure details, including complex Npgsql/NodaTime setup, conditional Google Workspace service registrations (Stub vs. Real), and Hangfire job schedules. This violates Clean Architecture principles by cluttering the "entry point" with implementation details.

*   **Recommendation:** Encapsulate infrastructure registrations into extension methods.
    *   Move service registrations to `Humans.Infrastructure/DependencyInjection.cs`.
    *   Create an extension method for Hangfire job scheduling (e.g., `app.UseRecurringJobs()`).
    *   The `Program.cs` should only call `builder.Services.AddInfrastructure(builder.Configuration)` and `app.UseInfrastructure()`.
*   **Impact of Waiting:** As more background jobs and external integrations are added, `Program.cs` will become a bottleneck for development and a source of merge conflicts.

## 3. Unify and Secure Membership Lifecycle Logic
**Priority:** Critical (Security & Governance)

There is currently duplicated logic for computing `MembershipStatus` between the `Profile` domain entity (`ComputeMembershipStatus`) and the `MembershipCalculator` service. Crucially, both implementations currently ignore the `IsApproved` flag. This allows a user to satisfy role and consent requirements and reach "Active" status without formal Board approval.

*   **Recommendation:** Consolidate membership logic into a single authoritative source.
    *   Enforce `IsApproved == true` as a mandatory gate for `MembershipStatus.Active`.
    *   Ensure all automated provisioning jobs (e.g., `SystemTeamSyncJob`) depend on this unified status.
*   **Impact of Waiting:** Automated provisioning to Google Workspace (Groups/Drive) relies on this status. Without the `IsApproved` gate, unvetted users could be granted access to sensitive resources as soon as they sign their legal documents.
