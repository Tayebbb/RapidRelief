namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>D-053 taxonomy — declaration order IS the scan order; first hit wins.</summary>
internal enum CannedCategory
{
    Earthquake,
    BuildingCollapse,
    Cyclone,
    Landslide,
    Fire,
    Flood,
    General,
}

/// <summary>
/// D-053 deterministic canned guidance: the same question always yields the same category
/// and the same text. Used for every degrade path (no key, breaker open, timeout, non-2xx,
/// malformed response, safety block, empty-after-sanitize).
/// </summary>
internal static class CannedSafetyResponses
{
    // Declaration order IS the scan order (D-053): "fire near the flooded road" must resolve
    // the same way on every evaluation, so a demo never sees two answers to one question.
    private static readonly (CannedCategory Category, string[] Words)[] KeywordMap =
    [
        (CannedCategory.Earthquake, ["earthquake", "quake", "tremor", "shaking"]),
        (CannedCategory.BuildingCollapse, ["collapse", "trapped", "rubble", "debris"]),
        (CannedCategory.Cyclone, ["cyclone", "storm", "surge", "wind"]),
        (CannedCategory.Landslide, ["landslide", "mudslide", "hill"]),
        (CannedCategory.Fire, ["fire", "smoke", "burning", "burn"]),
        (CannedCategory.Flood, ["flood", "drown", "waterlogg", "water rising"]),
    ];

    private static readonly Dictionary<CannedCategory, string> Texts = new()
    {
        [CannedCategory.Earthquake] = string.Join('\n',
            "While the ground shakes, drop down, cover your head and neck, and hold on under sturdy furniture.",
            "Stay away from windows, glass, tall shelves and outside walls.",
            "Once the shaking stops, leave by the stairs — never use a lift.",
            "Outside, move to open ground away from buildings, walls and power lines.",
            "Expect aftershocks and do not go back into a damaged building.",
            "Call 999 now if anyone's life is at risk."),
        [CannedCategory.BuildingCollapse] = string.Join('\n',
            "Do not enter or climb on a collapsed structure — it can shift again without warning.",
            "If you are trapped, cover your mouth and nose and tap on a pipe or wall instead of shouting.",
            "Save your phone battery and send your location by text if a call will not connect.",
            "Keep clear of dust clouds, hanging concrete and exposed electrical cables.",
            "Tell rescuers how many people are missing and where they were last seen.",
            "Call 999 now if anyone's life is at risk."),
        [CannedCategory.Cyclone] = string.Join('\n',
            "Move indoors to the strongest building you can reach, away from windows and doors.",
            "Shelter in an inner room on a lower floor and keep a mattress or table nearby for cover.",
            "Keep drinking water, dry food, a torch and your medicines within reach.",
            "Never go outside during the calm eye of the storm — the wind returns from the other side.",
            "Stay well away from the coast and from anything a storm surge could reach.",
            "Call 999 now if anyone's life is at risk."),
        [CannedCategory.Landslide] = string.Join('\n',
            "Move away from the slope at once, sideways and across the path of the slide.",
            "Do not stand above or below a cracked hillside, and stay out of narrow valleys and gullies.",
            "Listen for cracking wood, shifting boulders or a sudden change in a stream's water level.",
            "Do not go back for belongings; more of the slope can give way after heavy rain.",
            "Keep clear of broken power lines and blocked drains around the debris.",
            "Call 999 now if anyone's life is at risk."),
        [CannedCategory.Fire] = string.Join('\n',
            "Get everyone out first and stay out — never go back inside for belongings.",
            "Stay low under the smoke and move to the nearest safe exit; never use a lift.",
            "Feel a door before opening it; if it is hot, find another way out.",
            "If your clothes catch fire, stop, drop to the ground and roll to smother the flames.",
            "Cool a burn under clean running water for twenty minutes and cover it loosely.",
            "Call 999 now if anyone's life is at risk."),
        [CannedCategory.Flood] = string.Join('\n',
            "Move to higher ground now and take your phone, medicines and drinking water with you.",
            "Never walk, swim or drive through moving water — knee-deep water can sweep an adult away.",
            "If you can reach them safely, switch off electricity at the mains before water reaches sockets.",
            "Treat all flood water as contaminated and drink only boiled or bottled water.",
            "Avoid submerged roads, open drains and anything touching a fallen power line.",
            "Call 999 now if anyone's life is at risk."),
        [CannedCategory.General] = string.Join('\n',
            "Move away from the immediate danger first and take your phone with you.",
            "Warn the people around you and help anyone who cannot move on their own.",
            "Go to higher, open, well-lit ground and stay clear of damaged buildings and power lines.",
            "Keep drinking water, medicines and a torch with you, and save your phone battery.",
            "Tell the emergency services exactly where you are and what you can see.",
            "Call 999 now if anyone's life is at risk."),
    };

    public static CannedCategory CategoryFor(string? question)
    {
        var text = question ?? string.Empty;
        foreach (var (category, keywords) in KeywordMap)
        {
            if (keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return category;
            }
        }

        return CannedCategory.General;
    }

    public static string TextFor(string? question) => Texts[CategoryFor(question)];

    public static AssistantAnswer For(string? question, int latencyMs) => new(
        TextFor(question), Provider: "Canned", Truncated: false, latencyMs,
        TokensUsed: null, FinishReason: null);
}
