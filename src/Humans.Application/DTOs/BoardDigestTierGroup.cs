namespace Humans.Application.DTOs;

/// <summary>
/// A group of approved humans for a single tier in the Board daily digest email.
/// </summary>
public record BoardDigestTierGroup(string TierLabel, IReadOnlyList<string> DisplayNames);
