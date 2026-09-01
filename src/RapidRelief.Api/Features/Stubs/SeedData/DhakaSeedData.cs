using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Stubs.SeedData;

/// <summary>
/// The single deterministic demo dataset (blueprint B4): fixed GUIDs, fixed anchor time,
/// no Random / no DateTime.Now. Demo city Dhaka, center 23.8103, 90.4125.
/// </summary>
public static class DhakaSeedData
{
    /// <summary>Fixed anchor — all ReportedAtUtc values are offsets within the trailing 72 h of this.</summary>
    public static readonly DateTimeOffset AnchorUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly GeoPoint DhakaCenter = new(23.8103, 90.4125);

    /// <summary>The intentional near-duplicate pair (same Mirpur block, same type, 20 min apart) — F8's duplicate demo.</summary>
    public static readonly Guid NearDuplicateIncidentIdA = Guid.Parse("a0000000-0000-0000-0000-000000000005");
    public static readonly Guid NearDuplicateIncidentIdB = Guid.Parse("a0000000-0000-0000-0000-000000000006");

    private static Guid IncidentId(int n) => Guid.Parse($"a0000000-0000-0000-0000-0000000000{n:D2}");

    private static IncidentSummaryDto Incident(int n, DisasterType type, Severity severity, IncidentStatus status,
        double lat, double lon, string summary, double hoursBeforeAnchor, bool isSos, double? priority)
        => new(IncidentId(n), type, severity, status, new GeoPoint(lat, lon), summary,
            AnchorUtc.AddHours(-hoursBeforeAnchor), isSos, priority);

    // 28 incidents across Mirpur, Uttara, Gulshan, Mohammadpur, Lalbagh/Old Dhaka, Motijheel,
    // Dhanmondi, Badda, Khilgaon, Tejgaon, Savar, Keraniganj. Flood-heavy monsoon mix; all
    // severities 1–5; every status; 4 SOS; #5/#6 are the intentional near-duplicates.
    public static IReadOnlyList<IncidentSummaryDto> Incidents { get; } =
    [
        Incident(1, DisasterType.Flood, Severity.Severe, IncidentStatus.Verified,
            23.8210, 90.3665, "Mirpur 10 roundabout submerged, families moving to rooftops", 2, false, 82),
        Incident(2, DisasterType.Flood, Severity.Catastrophic, IncidentStatus.InProgress,
            23.7101, 90.3720, "Keraniganj lowlands under chest-deep water, children stranded", 5, true, 97),
        Incident(3, DisasterType.Fire, Severity.Severe, IncidentStatus.Assigned,
            23.7590, 90.3929, "Garment factory fire in Tejgaon, smoke spreading across the block", 3, false, 85),
        Incident(4, DisasterType.BuildingCollapse, Severity.Catastrophic, IncidentStatus.InProgress,
            23.8583, 90.2667, "Five-storey building collapse near Savar bus stand, people trapped", 8, true, 100),
        Incident(5, DisasterType.Flood, Severity.Moderate, IncidentStatus.Reported,
            23.8225, 90.3652, "Water rising fast in Mirpur 11 block C lanes", 1.0, false, null),
        Incident(6, DisasterType.Flood, Severity.Moderate, IncidentStatus.Reported,
            23.8235, 90.3660, "Mirpur 11 block C flooded, water entering ground-floor homes", 1.3333333333333333, false, null),
        Incident(7, DisasterType.Flood, Severity.Minor, IncidentStatus.Resolved,
            23.7461, 90.3742, "Dhanmondi lake overflow onto the walkway", 60, false, 35),
        Incident(8, DisasterType.Cyclone, Severity.Severe, IncidentStatus.Verified,
            23.8759, 90.3795, "Cyclone fringe winds tore roofs off Uttara sector 12 shacks", 20, false, 78),
        Incident(9, DisasterType.Fire, Severity.Moderate, IncidentStatus.Resolved,
            23.7189, 90.3882, "Kitchen fire spread to two homes in an Old Dhaka lane", 48, false, 55),
        Incident(10, DisasterType.Earthquake, Severity.Minor, IncidentStatus.Rejected,
            23.7331, 90.4172, "Tremor felt in Motijheel office towers, no damage found", 70, false, 20),
        Incident(11, DisasterType.Flood, Severity.Severe, IncidentStatus.Assigned,
            23.7806, 90.4266, "Merul Badda canal burst, ground floors flooding", 6, false, 80),
        Incident(12, DisasterType.Landslide, Severity.Moderate, IncidentStatus.Verified,
            23.7130, 90.3695, "Keraniganj embankment slope slid onto shanties after rain", 26, false, 60),
        Incident(13, DisasterType.Flood, Severity.Catastrophic, IncidentStatus.Assigned,
            23.7215, 90.3845, "Buriganga backflow drowning Kamrangirchar streets, elderly trapped", 4, true, 95),
        Incident(14, DisasterType.Fire, Severity.Catastrophic, IncidentStatus.Verified,
            23.8190, 90.3705, "Chemical warehouse fire in Mirpur, flames spreading toward homes", 12, false, 92),
        Incident(15, DisasterType.Flood, Severity.Minor, IncidentStatus.Reported,
            23.7522, 90.4263, "Khilgaon rail underpass waterlogged, rickshaws stuck", 9, false, null),
        Incident(16, DisasterType.Other, Severity.Minimal, IncidentStatus.Resolved,
            23.7925, 90.4078, "Fallen tree blocking a Gulshan 2 avenue lane", 66, false, 15),
        Incident(17, DisasterType.Cyclone, Severity.Moderate, IncidentStatus.Resolved,
            23.8700, 90.3850, "Billboard down on the Uttara highway service road", 30, false, 50),
        Incident(18, DisasterType.Flood, Severity.Moderate, IncidentStatus.Verified,
            23.7639, 90.3589, "Mohammadpur bus depot underwater, service halted", 14, false, 65),
        Incident(19, DisasterType.BuildingCollapse, Severity.Severe, IncidentStatus.Verified,
            23.8550, 90.2710, "Stairwell partially collapsed in a Savar apartment block", 18, false, 75),
        Incident(20, DisasterType.Flood, Severity.Severe, IncidentStatus.InProgress,
            23.7080, 90.3750, "River embankment breach flooding Keraniganj market", 7, true, 88),
        Incident(21, DisasterType.Fire, Severity.Minor, IncidentStatus.Rejected,
            23.7350, 90.4150, "Smoke reported in Motijheel, turned out to be trash burning", 36, false, 25),
        Incident(22, DisasterType.Landslide, Severity.Severe, IncidentStatus.Assigned,
            23.8600, 90.2650, "Brickfield spoil heap collapsed onto a workers' shed in Savar", 10, false, 83),
        Incident(23, DisasterType.Flood, Severity.Minimal, IncidentStatus.Reported,
            23.7940, 90.4100, "Gulshan lake path ankle-deep after the downpour", 11, false, null),
        Incident(24, DisasterType.Other, Severity.Moderate, IncidentStatus.InProgress,
            23.7610, 90.3950, "Gas leak smell near the Tejgaon industrial area", 2.5, false, 58),
        Incident(25, DisasterType.Flood, Severity.Moderate, IncidentStatus.Rejected,
            23.7830, 90.4240, "Badda flood report closed as already covered by an earlier ticket", 22, false, 30),
        Incident(26, DisasterType.Cyclone, Severity.Catastrophic, IncidentStatus.Reported,
            23.8800, 90.3760, "Storm surge warning for Uttara low areas, evacuation starting", 0.5, false, null),
        Incident(27, DisasterType.Flood, Severity.Minor, IncidentStatus.Verified,
            23.7500, 90.4290, "Khilgaon C block drains overflowing", 16, false, 42),
        Incident(28, DisasterType.Other, Severity.Severe, IncidentStatus.Verified,
            23.7660, 90.3610, "Mohammadpur water treatment failure, contamination risk", 40, false, 70),
    ];

    private static Guid ShelterId(int n) => Guid.Parse($"b0000000-0000-0000-0000-0000000000{n:D2}");

    // 8 shelters: schools/colleges doubling as cyclone shelters; #2 full, #5 closed.
    public static IReadOnlyList<ShelterSummaryDto> Shelters { get; } =
    [
        new(ShelterId(1), "Mirpur Govt High School Shelter", new GeoPoint(23.8150, 90.3680), 400, 120, true),
        new(ShelterId(2), "Uttara High School Cyclone Shelter", new GeoPoint(23.8720, 90.3830), 350, 350, true),
        new(ShelterId(3), "Dhanmondi Govt Boys' College Shelter", new GeoPoint(23.7455, 90.3760), 300, 80, true),
        new(ShelterId(4), "Motijheel Ideal School Shelter", new GeoPoint(23.7340, 90.4160), 500, 210, true),
        new(ShelterId(5), "Mohammadpur Model College Shelter", new GeoPoint(23.7650, 90.3600), 250, 0, false),
        new(ShelterId(6), "Khilgaon Govt Colony School Shelter", new GeoPoint(23.7530, 90.4250), 320, 45, true),
        new(ShelterId(7), "Savar Cantonment Public School Shelter", new GeoPoint(23.8570, 90.2690), 600, 380, true),
        new(ShelterId(8), "Lalbagh Kellar Mor Community Shelter", new GeoPoint(23.7200, 90.3870), 200, 60, true),
    ];

    private static Guid HospitalId(int n) => Guid.Parse($"c0000000-0000-0000-0000-0000000000{n:D2}");

    public static IReadOnlyList<HospitalSummaryDto> Hospitals { get; } =
    [
        new(HospitalId(1), "Dhaka Medical College Hospital", new GeoPoint(23.7256, 90.3970), 2300, 140, ["Trauma", "Burn", "General"]),
        new(HospitalId(2), "Kurmitola General Hospital", new GeoPoint(23.8250, 90.4000), 500, 60, ["General", "Respiratory"]),
        new(HospitalId(3), "Suhrawardy Medical College Hospital", new GeoPoint(23.7690, 90.3710), 850, 90, ["Trauma", "Orthopedics"]),
        new(HospitalId(4), "Mugda Medical College Hospital", new GeoPoint(23.7280, 90.4310), 500, 75, ["General", "Pediatrics"]),
        new(HospitalId(5), "Enam Medical College Hospital (Savar)", new GeoPoint(23.8440, 90.2570), 950, 110, ["Trauma", "Surgery"]),
        new(HospitalId(6), "United Hospital (Gulshan)", new GeoPoint(23.8040, 90.4150), 450, 30, ["Cardiology", "ICU"]),
    ];

    private static Guid VolunteerId(int n) => Guid.Parse($"d0000000-0000-0000-0000-0000000000{n:D2}");

    public static IReadOnlyList<VolunteerSummaryDto> Volunteers { get; } =
    [
        new(VolunteerId(1), "Arif Hossain", ["FirstAid", "Swimming"], true, new GeoPoint(23.8103, 90.4125)),
        new(VolunteerId(2), "Sumaiya Akter", ["Medical", "Triage"], true, new GeoPoint(23.7461, 90.3742)),
        new(VolunteerId(3), "Rakib Islam", ["Rescue", "RopeWork"], false, new GeoPoint(23.8223, 90.3654)),
        new(VolunteerId(4), "Nusrat Jahan", ["Cooking", "Logistics"], true, null),
        new(VolunteerId(5), "Tanvir Ahmed", ["Driving", "HeavyLifting"], true, new GeoPoint(23.7590, 90.3929)),
        new(VolunteerId(6), "Farzana Rahman", ["FirstAid", "Counseling"], false, null),
        new(VolunteerId(7), "Mehedi Hasan", ["Boating", "Swimming"], true, new GeoPoint(23.7101, 90.3720)),
        new(VolunteerId(8), "Sadia Islam", ["Radio", "Coordination"], true, new GeoPoint(23.8759, 90.3795)),
        new(VolunteerId(9), "Imran Kabir", ["Electrician", "Rescue"], true, new GeoPoint(23.8583, 90.2667)),
        new(VolunteerId(10), "Rima Chowdhury", ["Nursing", "FirstAid"], false, new GeoPoint(23.7331, 90.4172)),
    ];

    private static Guid NgoId(int n) => Guid.Parse($"e0000000-0000-0000-0000-0000000000{n:D2}");

    public static IReadOnlyList<NgoSummaryDto> Ngos { get; } =
    [
        new(NgoId(1), "BRAC Disaster Response", ["Flood Relief", "Shelter"], "response@brac.example"),
        new(NgoId(2), "Bidyanondo Foundation", ["Food", "Medical Camps"], "help@bidyanondo.example"),
        new(NgoId(3), "Anjuman Mufidul Islam", ["Burial Support", "Ambulance"], "contact@anjuman.example"),
        new(NgoId(4), "Sajida Foundation", ["Health", "Micro-relief"], "relief@sajida.example"),
        new(NgoId(5), "JAAGO Foundation", ["Youth Volunteers", "Education"], "volunteer@jaago.example"),
    ];

    /// <summary>Teams have no contract surface yet — data-only, consumed by F5/F6 once ITeamReadService is added additively.</summary>
    public sealed record RescueTeamSeed(Guid Id, string Name, string Speciality, bool IsAvailable, GeoPoint BaseLocation);

    private static Guid TeamId(int n) => Guid.Parse($"f0000000-0000-0000-0000-0000000000{n:D2}");

    public static IReadOnlyList<RescueTeamSeed> RescueTeams { get; } =
    [
        new(TeamId(1), "FSCD Mirpur Unit", "Water Rescue", true, new GeoPoint(23.8223, 90.3654)),
        new(TeamId(2), "FSCD Tejgaon Unit", "Fire Suppression", true, new GeoPoint(23.7590, 90.3929)),
        new(TeamId(3), "Savar Urban Search & Rescue", "Collapse Rescue", false, new GeoPoint(23.8583, 90.2667)),
        new(TeamId(4), "BDRCS Response Team Alpha", "Medical Evacuation", true, new GeoPoint(23.7256, 90.3970)),
        new(TeamId(5), "Coast Guard River Unit", "Boat Rescue", true, new GeoPoint(23.7101, 90.3720)),
        new(TeamId(6), "Army Engineer Detachment", "Heavy Lifting", false, new GeoPoint(23.8040, 90.4150)),
    ];
}
