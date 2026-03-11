namespace Humans.Domain.Enums;

/// <summary>
/// Priority level for a team role slot.
/// </summary>
public enum SlotPriority
{
    /// <summary>
    /// Must be filled — critical for team function.
    /// </summary>
    Critical = 0,

    /// <summary>
    /// Should be filled if possible.
    /// </summary>
    Important = 1,

    /// <summary>
    /// Helpful but not essential.
    /// </summary>
    NiceToHave = 2
}
