using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Shelters.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Shelters.Endpoints;

public static class SheltersEndpoints
{
    private const int MaxPage = 1_000_000;
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/shelters");

        group.MapPost("/", CreateShelterAsync).RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapGet("/", GetSheltersAsync).AllowAnonymous(); // Allowed for citizen
        group.MapGet("/{id:guid}", GetShelterByIdAsync).AllowAnonymous();
        group.MapPut("/{id:guid}", UpdateShelterAsync).RequireAuthorization(AuthPolicies.RequireAdmin);
        group.MapPatch("/{id:guid}/occupancy", UpdateOccupancyAsync).RequireAuthorization(AuthPolicies.RequireAdmin);
    }

    private static async Task<IResult> CreateShelterAsync(
        CreateShelterRequest request,
        IValidator<CreateShelterRequest> validator,
        OpsDbContext db,
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

        var shelter = new Shelter
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Location = new GeoPoint(request.Latitude, request.Longitude),
            Capacity = request.Capacity,
            CurrentOccupancy = request.CurrentOccupancy,
            Facilities = request.Facilities,
            Status = request.Status
        };

        db.Shelters.Add(shelter);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/shelters/{shelter.Id}", new ApiEnvelope<ShelterDto>(ShelterDto.FromEntity(shelter)));
    }

    private static async Task<IResult> GetSheltersAsync(
        OpsDbContext db,
        IShelterReadService readService,
        DatabaseHealth databaseHealth,
        double? lat,
        double? lng,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        if (lat.HasValue && lng.HasValue)
        {
            // F3 finder functionality, utilizing the IShelterReadService interface for stub resilience
            var origin = new GeoPoint(lat.Value, lng.Value);
            var nearest = await readService.GetNearestAsync(origin, pageSize, ct);
            
            // Paging is mocked out in nearest, but we wrap the result in PagedResult for consistent ApiEnvelope
            var result = new PagedResult<ShelterSummaryDto>(nearest, 1, nearest.Count, nearest.Count);
            return Results.Ok(new ApiEnvelope<PagedResult<ShelterSummaryDto>>(result));
        }

        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        page = Math.Clamp(page, 1, MaxPage);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var totalCount = await db.Shelters.CountAsync(ct);
        var items = await db.Shelters
            .OrderBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => ShelterDto.FromEntity(s))
            .ToListAsync(ct);

        var pagedResult = new PagedResult<ShelterDto>(items, page, pageSize, totalCount);
        return Results.Ok(new ApiEnvelope<PagedResult<ShelterDto>>(pagedResult));
    }

    private static async Task<IResult> GetShelterByIdAsync(
        Guid id,
        OpsDbContext db,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var shelter = await db.Shelters.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shelter is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Shelter not found");
        }

        return Results.Ok(new ApiEnvelope<ShelterDto>(ShelterDto.FromEntity(shelter)));
    }

    private static async Task<IResult> UpdateShelterAsync(
        Guid id,
        UpdateShelterRequest request,
        IValidator<UpdateShelterRequest> validator,
        OpsDbContext db,
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

        var shelter = await db.Shelters.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shelter is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Shelter not found");
        }

        shelter.Name = request.Name;
        shelter.Location = new GeoPoint(request.Latitude, request.Longitude);
        shelter.Capacity = request.Capacity;
        shelter.CurrentOccupancy = request.CurrentOccupancy;
        shelter.Facilities = request.Facilities;
        shelter.Status = request.Status;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new ApiEnvelope<ShelterDto>(ShelterDto.FromEntity(shelter)));
    }

    private static async Task<IResult> UpdateOccupancyAsync(
        Guid id,
        UpdateOccupancyRequest request,
        IValidator<UpdateOccupancyRequest> validator,
        OpsDbContext db,
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

        var shelter = await db.Shelters.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shelter is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Shelter not found");
        }

        // Must still validate cross-property even though Capacity is not being passed in
        if (request.CurrentOccupancy > shelter.Capacity)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { "CurrentOccupancy", ["CurrentOccupancy cannot be greater than Capacity."] }
            });
        }

        shelter.CurrentOccupancy = request.CurrentOccupancy;

        // Auto-update status based on occupancy logic (optional but helpful)
        if (shelter.CurrentOccupancy >= shelter.Capacity && shelter.Status == ShelterStatus.Open)
        {
            shelter.Status = ShelterStatus.Full;
        }
        else if (shelter.CurrentOccupancy < shelter.Capacity && shelter.Status == ShelterStatus.Full)
        {
            shelter.Status = ShelterStatus.Open;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new ApiEnvelope<ShelterDto>(ShelterDto.FromEntity(shelter)));
    }

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");
}
