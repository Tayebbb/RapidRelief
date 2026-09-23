namespace RapidRelief.Api.Features.Rescue.Services;

/// <summary>
/// Configuration for the Government AI Auto-Dispatch Engine.
/// Decides whether and under what conditions high-severity incidents are automatically
/// assigned to the best-suited rescue team upon AI assessment completion.
/// </summary>
public sealed record AutoDispatchOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Priority score (0-100) at or above which an incident triggers automatic team dispatch.
    /// Default is 70.0 (covering High-to-Critical band).
    /// </summary>
    public double MinPriorityScore { get; init; } = 70.0;

    /// <summary>
    /// When true, any incident with an emergency SOS flag immediately triggers auto-dispatch
    /// regardless of the numerical priority score.
    /// </summary>
    public bool AlwaysDispatchOnSos { get; init; } = true;

    /// <summary>
    /// Maximum search radius in kilometers for eligible rescue units.
    /// </summary>
    public double MaxUsefulDistanceKm { get; init; } = 25.0;

    /// <summary>
    /// Minimum composite suitability score (0.0 to 1.0) required for a team to be automatically dispatched.
    /// Teams below this threshold will not be auto-dispatched, leaving the incident for human coordinator review.
    /// </summary>
    public double MinSuitabilityScore { get; init; } = 0.45;

    public static AutoDispatchOptions Read(IConfiguration config)
    {
        var section = config.GetSection("Rescue:AutoDispatch");
        return new AutoDispatchOptions
        {
            Enabled = section.GetValue("Enabled", true),
            MinPriorityScore = section.GetValue("MinPriorityScore", 70.0),
            AlwaysDispatchOnSos = section.GetValue("AlwaysDispatchOnSos", true),
            MaxUsefulDistanceKm = section.GetValue("MaxUsefulDistanceKm", 25.0),
            MinSuitabilityScore = section.GetValue("MinSuitabilityScore", 0.45),
        };
    }
}
