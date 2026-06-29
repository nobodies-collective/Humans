using Humans.Application.DTOs;
using NodaTime;

namespace Humans.Application.Interfaces.Tickets;

/// <summary>
/// Vendor-agnostic interface for ticket platform operations.
/// Implementations wrap vendor-specific APIs (e.g. TicketTailor).
/// </summary>
public interface ITicketVendorService : IApplicationService
{
    /// <summary>Fetch orders, optionally since a given timestamp.</summary>
    Task<IReadOnlyList<VendorOrderDto>> GetOrdersAsync(
        Instant? since, string eventId, CancellationToken ct = default);

    /// <summary>Fetch issued tickets, optionally since a given timestamp.</summary>
    Task<IReadOnlyList<VendorTicketDto>> GetIssuedTicketsAsync(
        Instant? since, string eventId, CancellationToken ct = default);

    /// <summary>Get high-level event summary (capacity, sold, remaining).</summary>
    Task<VendorEventSummaryDto> GetEventSummaryAsync(
        string eventId, CancellationToken ct = default);

    /// <summary>Generate discount codes via vendor API.</summary>
    Task<IReadOnlyList<string>> GenerateDiscountCodesAsync(
        DiscountCodeSpec spec, CancellationToken ct = default);

    /// <summary>Check redemption status of discount codes.</summary>
    Task<IReadOnlyList<DiscountCodeStatusDto>> GetDiscountCodeUsageAsync(
        IEnumerable<string> codes, CancellationToken ct = default);

    /// <summary>
    /// Record a check-in for an issued ticket at the vendor (TicketTailor
    /// <c>POST /v1/check_ins</c>). A best-effort mirror fired after a gate admit
    /// so the vendor dashboard / vendor check-in app stays consistent — the Gate
    /// section's own <c>gate_scan_events</c> remains the dedupe authority. Safe to retry.
    /// </summary>
    Task CreateCheckInAsync(
        string vendorTicketId, Instant occurredAt, CancellationToken ct = default);
}
