# Event Bus (F0 — plan §8.6, D-006)

Hand-rolled in-process pub/sub — **never** add MediatR (commercial from v13, D-006). The contracts
live in `Shared/Contracts/Eventing`; the implementation is `Infrastructure/Eventing/InProcessEventBus`
(registered **scoped** in Program step 6).

## The pieces

- `IEvent` — `EventId` + `OccurredAtUtc`.
- `EventBase` — abstract record supplying both; every event derives from it.
- `IEventHandler<TEvent>` — `Task HandleAsync(TEvent evt, CancellationToken ct)`.
- `IEventBus` — `Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct)`.

Events are sealed records in `Shared/Contracts/Events` (architecture-tested — they may live nowhere
else) and are part of Contracts v1: additive-only changes (§4.6).

## Declaring an event

```csharp
// Shared/Contracts/Events/PingCreated.cs
public sealed record PingCreated(Guid PingId, string Message) : EventBase;
```

## Publishing (cross-module writes happen ONLY this way)

```csharp
// inside an endpoint/service — inject IEventBus
await eventBus.PublishAsync(new PingCreated(ping.Id, ping.Message), ct);
```

## Handling — register in YOUR module, in YOUR feature folder

```csharp
// Features/Sample/Handlers/PingCreatedLoggingHandler.cs
public sealed class PingCreatedLoggingHandler : IEventHandler<PingCreated>
{
    public Task HandleAsync(PingCreated evt, CancellationToken ct = default) { … }
}

// Features/Sample/SampleModule.cs → AddModule
services.AddScoped<IEventHandler<PingCreated>, PingCreatedLoggingHandler>();
```

Handlers are resolved from the **current scope**, so they can take scoped dependencies
(DbContexts included). Register scoped unless stateless.

## Failure isolation semantics (what "fire-and-forget" means here)

- Handlers run **sequentially, awaited**, each wrapped in try/catch.
- A throwing handler is logged (`LogError` with handler + event id) and **skipped — the next
  handler still runs and the publisher never sees the exception**.
- Zero registered handlers = silent success. A missing subscriber module breaks nothing (§1.5).
- No `Task.Run`/threads: isolation is at module level, not thread level — this keeps scoped
  services (DbContexts) safe from disposed-scope bugs.

## Testing

`Eventing/InProcessEventBusTests` covers ordering, isolation, and scoping; `SamplePingTests` proves
the publisher→handler flow end-to-end with a probe handler registered in the test factory.
