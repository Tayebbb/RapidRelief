using System.Threading.Channels;

namespace RapidRelief.Api.Features.Ai.Pipeline;

/// <summary>
/// Bounded work queue (D-021): capacity from Ai:Pipeline:ChannelCapacity, DropWrite when
/// full so a flood of reports can never wedge report POSTs behind AI latency.
/// </summary>
internal static class AiChannel
{
    public static Channel<AiWorkItem> Create(int capacity, ILogger logger)
        => Channel.CreateBounded<AiWorkItem>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            },
            item => logger.LogError(
                "AI pipeline channel full — dropped analysis work for incident {IncidentId}",
                item.Request.IncidentId));
}
