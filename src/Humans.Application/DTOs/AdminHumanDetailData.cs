using Humans.Domain.Entities;
using Humans.Domain.Enums;
using MemberApplication = Humans.Domain.Entities.Application;

namespace Humans.Application.DTOs;

public record AdminHumanDetailData(
    User User,
    Profile? Profile,
    IReadOnlyList<MemberApplication> Applications,
    int ConsentCount,
    IReadOnlyList<RoleAssignment> RoleAssignments,
    IReadOnlyList<AdminAuditEntry> AuditEntries,
    string? RejectedByName);

public record AdminAuditEntry(
    string Action,
    string Description,
    DateTime OccurredAt,
    string? ActorName,
    bool IsSystemAction);
