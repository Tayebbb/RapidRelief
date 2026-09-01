using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Shared.Contracts.Events;

public sealed record AlertPublished(Guid AlertId, string Title, string Body, Severity Severity,
    DisasterType? Type, DateTimeOffset ExpiresAtUtc) : EventBase;
