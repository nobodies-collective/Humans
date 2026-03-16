using Humans.Domain.Entities;
using NodaTime;

namespace Humans.Application.Interfaces;

/// <summary>
/// Calculates urgency scores for shifts to prioritize volunteer signup.
/// </summary>
public interface IShiftUrgencyService
{
    /// <summary>
    /// Gets shifts ranked by urgency score, with optional filtering.
    /// </summary>
    Task<IReadOnlyList<UrgentShift>> GetUrgentShiftsAsync(
        Guid eventSettingsId, int? limit = null,
        Guid? departmentId = null, LocalDate? date = null);

    /// <summary>
    /// Calculates the urgency score for a single shift.
    /// </summary>
    double CalculateScore(Shift shift, int confirmedCount);
}

/// <summary>
/// A shift with its computed urgency score and fill status.
/// </summary>
public record UrgentShift(
    Shift Shift,
    double UrgencyScore,
    int ConfirmedCount,
    int RemainingSlots,
    string DepartmentName);
