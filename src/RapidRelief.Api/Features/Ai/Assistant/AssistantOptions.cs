namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>Bound copy of the <c>Ai:Assistant</c> config section (D-048/D-051/D-052).</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Ai:Assistant";

    /// <summary>Prose needs more room than F8's JSON budget (blueprint fact 1).</summary>
    public int MaxOutputTokens { get; init; } = 512;

    /// <summary>One turn = one user message + one model answer (D-048 window).</summary>
    public int HistoryTurns { get; init; } = 10;

    public int MaxSessionMessages { get; init; } = 50;

    public int MaxMessageLength { get; init; } = 1000;

    public int MaxAnswerLength { get; init; } = 1500;

    public int ShelterCount { get; init; } = 3;

    public int RetentionDays { get; init; } = 7;

    public double RetentionSweepHours { get; init; } = 6;

    public static AssistantOptions Read(IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        var defaults = new AssistantOptions();
        return new AssistantOptions
        {
            MaxOutputTokens = Positive(section.GetValue("MaxOutputTokens", defaults.MaxOutputTokens), defaults.MaxOutputTokens),
            HistoryTurns = Positive(section.GetValue("HistoryTurns", defaults.HistoryTurns), defaults.HistoryTurns),
            MaxSessionMessages = Positive(section.GetValue("MaxSessionMessages", defaults.MaxSessionMessages), defaults.MaxSessionMessages),
            MaxMessageLength = Positive(section.GetValue("MaxMessageLength", defaults.MaxMessageLength), defaults.MaxMessageLength),
            MaxAnswerLength = Positive(section.GetValue("MaxAnswerLength", defaults.MaxAnswerLength), defaults.MaxAnswerLength),
            ShelterCount = Positive(section.GetValue("ShelterCount", defaults.ShelterCount), defaults.ShelterCount),
            RetentionDays = Positive(section.GetValue("RetentionDays", defaults.RetentionDays), defaults.RetentionDays),
            RetentionSweepHours = Positive(section.GetValue("RetentionSweepHours", defaults.RetentionSweepHours), defaults.RetentionSweepHours),
        };
    }

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;

    private static double Positive(double value, double fallback) => value > 0 ? value : fallback;
}
