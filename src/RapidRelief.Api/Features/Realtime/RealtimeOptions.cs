using Serilog;

namespace RapidRelief.Api.Features.Realtime;

/// <summary>D-032 tri-state: Hub (push + persist) · PollingOnly (persist only) · Off (no-op notifier).</summary>
public enum RealtimeMode
{
    Hub = 0,
    PollingOnly = 1,
    Off = 2,
}

/// <summary>Bound copy of the <c>Realtime</c> config section (D-032/D-034).</summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    public RealtimeMode Mode { get; init; } = RealtimeMode.Hub;

    public int RetentionDays { get; init; } = 30;

    public double RetentionSweepHours { get; init; } = 6;

    public int PollSecondsConnected { get; init; } = 60;

    public int PollSecondsDisconnected { get; init; } = 5;

    public static RealtimeOptions Read(IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var defaults = new RealtimeOptions();
        var configuredMode = section["Mode"];

        // Unknown/blank mode keeps realtime on the full-featured default rather than silently
        // disabling it — but a typo that was meant to disable the hub must be visible.
        if (!Enum.TryParse<RealtimeMode>(configuredMode, ignoreCase: true, out var mode))
        {
            mode = defaults.Mode;
            if (!string.IsNullOrWhiteSpace(configuredMode))
            {
                Log.Warning("Realtime:Mode '{Mode}' is not one of Hub/PollingOnly/Off — falling back to {Fallback}",
                    configuredMode, mode);
            }
        }

        return new RealtimeOptions
        {
            Mode = mode,
            RetentionDays = Positive(section.GetValue("RetentionDays", defaults.RetentionDays), defaults.RetentionDays),
            RetentionSweepHours = Positive(section.GetValue("RetentionSweepHours", defaults.RetentionSweepHours), defaults.RetentionSweepHours),
            PollSecondsConnected = Positive(section.GetValue("PollSecondsConnected", defaults.PollSecondsConnected), defaults.PollSecondsConnected),
            PollSecondsDisconnected = Positive(section.GetValue("PollSecondsDisconnected", defaults.PollSecondsDisconnected), defaults.PollSecondsDisconnected),
        };
    }

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;

    private static double Positive(double value, double fallback) => value > 0 ? value : fallback;
}
