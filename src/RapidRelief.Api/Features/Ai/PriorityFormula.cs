using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// The single priority formula (blueprint F8): extracted verbatim from the rule-based
/// service so the Gemini path scores identically. Pure function of its inputs.
/// </summary>
public static class PriorityFormula
{
    public static double Compute(Severity severity, bool isSos, DateTimeOffset reportedAtUtc, DateTimeOffset nowUtc)
    {
        var ageHours = Math.Max((nowUtc - reportedAtUtc).TotalHours, 0);
        var recencyBonus = ageHours >= 6 ? 0 : 15 * (1 - ageHours / 6);
        return Math.Clamp(20 * (int)severity + (isSos ? 25 : 0) + recencyBonus, 0, 100);
    }
}
