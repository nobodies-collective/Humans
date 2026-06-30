namespace Humans.Application.Services.Gate;

/// <summary>
/// Normalizes a scanned or typed barcode so the same physical ticket matches
/// regardless of surrounding whitespace or case differences between a
/// keyboard-wedge scan and manual entry. Applied symmetrically to both the
/// scanned value and the stored <c>TicketAttendee.Barcode</c> before comparison,
/// and used as the admit dedupe key.
/// </summary>
public static class GateBarcode
{
    public static string Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToUpperInvariant();
}
