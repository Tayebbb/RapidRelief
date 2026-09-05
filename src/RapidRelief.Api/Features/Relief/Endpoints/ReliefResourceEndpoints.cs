using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Relief.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Relief.Endpoints;

/// <summary>
/// Warehouse inventory behind the citizen relief queue: what is stocked, what is already
/// committed, and how much of the pending demand it can actually cover.
/// </summary>
public static class ReliefResourceEndpoints
{
    public const string BasePath = "/api/relief/resources";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath).RequireAuthorization(AuthPolicies.RequireGovernment);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
    }

    private static async Task<IResult> ListAsync(ReliefDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var resources = await db.Resources.AsNoTracking().OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(ct);

        // Open demand per category tells the operator whether stock is actually sufficient.
        var openStatuses = new[] { ReliefStatus.Pending, ReliefStatus.Approved, ReliefStatus.Allocated };
        var demand = await db.Requests.AsNoTracking()
            .Where(x => openStatuses.Contains(x.Status))
            .GroupBy(x => x.ReliefType)
            .Select(g => new { Type = g.Key, Quantity = g.Sum(x => x.QuantityRequested) })
            .ToListAsync(ct);

        var demandByType = demand
            .Where(x => Enum.TryParse<ResourceType>(x.Type, ignoreCase: true, out _))
            .GroupBy(x => Enum.Parse<ResourceType>(x.Type, ignoreCase: true))
            .ToDictionary(g => g.Key, g => (double)g.Sum(x => x.Quantity));

        var items = resources.Select(r => new ReliefResourceDto(
            r.Id, r.Name, r.Category, r.TotalQuantity, r.AllocatedQuantity,
            Math.Max(0, r.TotalQuantity - r.AllocatedQuantity),
            r.Unit, r.WarehouseLocation,
            demandByType.TryGetValue(r.Category, out var open) ? open : 0,
            r.UpdatedAtUtc)).ToList();

        var uncovered = demandByType
            .Where(d => !resources.Any(r => r.Category == d.Key))
            .Select(d => new ReliefResourceGapDto(d.Key, d.Value))
            .ToList();

        return Results.Ok(new ApiEnvelope<ReliefInventoryDto>(new ReliefInventoryDto(items, uncovered)));
    }

    private static async Task<IResult> CreateAsync(
        ReliefResourceRequest request,
        IValidator<ReliefResourceRequest> validator,
        ReliefDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var now = clock.GetUtcNow();
        var resource = new ReliefResource
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            Category = request.Category,
            TotalQuantity = request.TotalQuantity,
            AllocatedQuantity = request.AllocatedQuantity,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "Units" : request.Unit!.Trim(),
            WarehouseLocation = request.WarehouseLocation?.Trim() ?? string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Resources.Add(resource);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "Resource.Create", "ReliefResource", resource.Id.ToString(),
            $"Stocked {resource.TotalQuantity:0.##} {resource.Unit} of {resource.Name} ({resource.Category})", "Created"), ct);

        return Results.Created($"{BasePath}/{resource.Id}", new ApiEnvelope<ReliefResourceDto>(ToDto(resource)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        ReliefResourceRequest request,
        IValidator<ReliefResourceRequest> validator,
        ReliefDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var resource = await db.Resources.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (resource is null)
        {
            return Results.NotFound();
        }

        if (request.AllocatedQuantity > request.TotalQuantity)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Allocation exceeds stock",
                detail: "Allocated quantity cannot be greater than the total held in the warehouse.");
        }

        var before = $"{resource.TotalQuantity:0.##} total / {resource.AllocatedQuantity:0.##} allocated";
        resource.Name = request.Name!.Trim();
        resource.Category = request.Category;
        resource.TotalQuantity = request.TotalQuantity;
        resource.AllocatedQuantity = request.AllocatedQuantity;
        resource.Unit = string.IsNullOrWhiteSpace(request.Unit) ? resource.Unit : request.Unit!.Trim();
        resource.WarehouseLocation = request.WarehouseLocation?.Trim() ?? string.Empty;
        resource.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(null, string.Empty, string.Empty,
            "Resource.Update", "ReliefResource", resource.Id.ToString(),
            $"{resource.Name}: {before} → {resource.TotalQuantity:0.##} total / {resource.AllocatedQuantity:0.##} allocated", "Updated"), ct);

        return Results.Ok(new ApiEnvelope<ReliefResourceDto>(ToDto(resource)));
    }

    private static ReliefResourceDto ToDto(ReliefResource r) => new(
        r.Id, r.Name, r.Category, r.TotalQuantity, r.AllocatedQuantity,
        Math.Max(0, r.TotalQuantity - r.AllocatedQuantity), r.Unit, r.WarehouseLocation, 0, r.UpdatedAtUtc);

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "Relief inventory is unavailable while the database is offline.");
}

public sealed record ReliefResourceRequest(
    string? Name,
    ResourceType Category,
    double TotalQuantity,
    double AllocatedQuantity,
    string? Unit,
    string? WarehouseLocation);

public sealed record ReliefResourceDto(
    Guid Id,
    string Name,
    ResourceType Category,
    double TotalQuantity,
    double AllocatedQuantity,
    double AvailableQuantity,
    string Unit,
    string WarehouseLocation,
    double OpenDemand,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReliefResourceGapDto(ResourceType Category, double OpenDemand);

public sealed record ReliefInventoryDto(
    IReadOnlyList<ReliefResourceDto> Items,
    IReadOnlyList<ReliefResourceGapDto> UncoveredDemand);

public sealed class ReliefResourceValidator : AbstractValidator<ReliefResourceRequest>
{
    public ReliefResourceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.TotalQuantity).GreaterThanOrEqualTo(0).LessThanOrEqualTo(10_000_000);
        RuleFor(x => x.AllocatedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Unit).MaximumLength(30);
        RuleFor(x => x.WarehouseLocation).MaximumLength(200);
    }
}
