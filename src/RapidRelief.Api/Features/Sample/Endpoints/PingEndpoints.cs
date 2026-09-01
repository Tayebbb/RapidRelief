using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Sample.Data;
using RapidRelief.Api.Features.Sample.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Sample.Endpoints;

public sealed record CreatePingRequest(string? Message);

/// <summary>Validated EXPLICITLY in the endpoint (B6 step 4 — never auto-validation).</summary>
public sealed class CreatePingValidator : AbstractValidator<CreatePingRequest>
{
    public CreatePingValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(500);
    }
}

public sealed record PingDto(Guid Id, string Message, DateTimeOffset CreatedAtUtc);

public static class PingEndpoints
{
    private const int MaxPage = 1_000_000;
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/sample");

        group.MapPost("/pings", CreatePingAsync).RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapGet("/pings", GetPingsAsync).AllowAnonymous();
        group.MapGet("/pings/{id:guid}", GetPingByIdAsync).AllowAnonymous();
    }

    private static async Task<IResult> CreatePingAsync(
        CreatePingRequest request,
        IValidator<CreatePingRequest> validator,
        SampleDbContext db,
        IEventBus eventBus,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var ping = new Ping
        {
            Id = Guid.NewGuid(),
            Message = request.Message!,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Pings.Add(ping);
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(new PingCreated(ping.Id, ping.Message), ct);

        var dto = new PingDto(ping.Id, ping.Message, ping.CreatedAtUtc);
        return Results.Created($"/api/sample/pings/{ping.Id}", new ApiEnvelope<PingDto>(dto));
    }

    private static async Task<IResult> GetPingsAsync(
        SampleDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50)
    {
        // GET is DB-backed, so it is 503-gated too (D-005: only STUB-backed reads survive degraded mode).
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        // Paging clamp convention (docs/api-conventions.md): page 1–1,000,000, pageSize 1–200,
        // BEFORE any math — unclamped int.MaxValue overflows (page-1)*pageSize into a 500.
        page = Math.Clamp(page, 1, MaxPage);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var totalCount = await db.Pings.CountAsync(ct);
        var items = await db.Pings
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PingDto(p.Id, p.Message, p.CreatedAtUtc))
            .ToListAsync(ct);

        var result = new PagedResult<PingDto>(items, page, pageSize, totalCount);
        return Results.Ok(new ApiEnvelope<PagedResult<PingDto>>(result));
    }

    private static async Task<IResult> GetPingByIdAsync(
        Guid id,
        SampleDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var ping = await db.Pings.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (ping is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ping not found");
        }

        return Results.Ok(new ApiEnvelope<PingDto>(new PingDto(ping.Id, ping.Message, ping.CreatedAtUtc)));
    }

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");
}
