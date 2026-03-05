namespace Humans.Application.DTOs;

public record AdminDashboardData(
    int TotalMembers,
    int ActiveMembers,
    int PendingVolunteers,
    int PendingApplications,
    int PendingConsents,
    int TotalApplications,
    int ApprovedApplications,
    int RejectedApplications,
    int ColaboradorApplied,
    int AsociadoApplied);
