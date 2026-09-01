namespace RapidRelief.Client.Common.Realtime;

// Hand-mirrored F9 wire records (D-019 precedent). The Client never references the Api project;
// ClientWireContractTests pins these against the server records property-for-property.

public sealed record NotificationDto(
    Guid Id,
    string Topic,
    string Summary,
    string PayloadJson,
    string Audience,
    string? Role,
    Guid? UserId,
    DateTimeOffset CreatedAtUtc,
    bool IsRead);

public sealed record NotificationPage(
    IReadOnlyList<NotificationDto> Items,
    DateTimeOffset ServerTimeUtc,
    string? NextCursor);

public sealed record MarkedResponse(int Marked);

public sealed record UnreadCountResponse(int Count);
