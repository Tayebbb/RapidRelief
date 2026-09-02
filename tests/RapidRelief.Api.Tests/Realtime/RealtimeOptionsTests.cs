using Microsoft.Extensions.Configuration;
using RapidRelief.Api.Features.Realtime;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>D-032/D-034 — config binding for the tri-state mode and the retention knobs.</summary>
public sealed class RealtimeOptionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Missing_section_yields_hub_mode_and_documented_defaults()
    {
        var options = RealtimeOptions.Read(Config());

        Assert.Equal(RealtimeMode.Hub, options.Mode);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(6, options.RetentionSweepHours);
        Assert.Equal(60, options.PollSecondsConnected);
        Assert.Equal(5, options.PollSecondsDisconnected);
    }

    [Theory]
    [InlineData("Hub", RealtimeMode.Hub)]
    [InlineData("PollingOnly", RealtimeMode.PollingOnly)]
    [InlineData("pollingonly", RealtimeMode.PollingOnly)]
    [InlineData("OFF", RealtimeMode.Off)]
    public void Mode_is_parsed_case_insensitively(string configured, RealtimeMode expected)
    {
        var options = RealtimeOptions.Read(Config(("Realtime:Mode", configured)));

        Assert.Equal(expected, options.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nonsense")]
    public void Unknown_mode_falls_back_to_hub(string configured)
    {
        var options = RealtimeOptions.Read(Config(("Realtime:Mode", configured)));

        Assert.Equal(RealtimeMode.Hub, options.Mode);
    }

    [Fact]
    public void Configured_values_win()
    {
        var options = RealtimeOptions.Read(Config(
            ("Realtime:RetentionDays", "7"),
            ("Realtime:RetentionSweepHours", "0.25"),
            ("Realtime:PollSecondsConnected", "30"),
            ("Realtime:PollSecondsDisconnected", "2")));

        Assert.Equal(7, options.RetentionDays);
        Assert.Equal(0.25, options.RetentionSweepHours);
        Assert.Equal(30, options.PollSecondsConnected);
        Assert.Equal(2, options.PollSecondsDisconnected);
    }

    [Fact]
    public void Non_positive_values_fall_back_to_defaults()
    {
        var options = RealtimeOptions.Read(Config(
            ("Realtime:RetentionDays", "0"),
            ("Realtime:RetentionSweepHours", "-3"),
            ("Realtime:PollSecondsConnected", "0"),
            ("Realtime:PollSecondsDisconnected", "-1")));

        Assert.Equal(30, options.RetentionDays);
        Assert.Equal(6, options.RetentionSweepHours);
        Assert.Equal(60, options.PollSecondsConnected);
        Assert.Equal(5, options.PollSecondsDisconnected);
    }
}
