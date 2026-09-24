using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Registry.Data;
using RapidRelief.Api.Features.Registry.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Registry.Endpoints;

public static class RegistryEndpoints
{
    public const string BasePath = "/api/registry";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .RequireRateLimiting("reports");

        // Hospitals
        group.MapGet("/hospitals", ListHospitalsAsync);
        group.MapGet("/hospitals/{id:guid}", GetHospitalAsync);
        group.MapPost("/hospitals", CreateHospitalAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapPut("/hospitals/{id:guid}", UpdateHospitalAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
        group.MapDelete("/hospitals/{id:guid}", DeleteHospitalAsync).RequireAuthorization(AuthPolicies.RequireGovernment);

        // Volunteers
        group.MapGet("/volunteers", ListVolunteersAsync);
        group.MapGet("/volunteers/{id:guid}", GetVolunteerAsync);
        group.MapPost("/volunteers", CreateVolunteerAsync);
        group.MapPut("/volunteers/{id:guid}", UpdateVolunteerAsync);
        group.MapDelete("/volunteers/{id:guid}", DeleteVolunteerAsync).RequireAuthorization(AuthPolicies.RequireGovernment);

        // NGOs
        group.MapGet("/ngos", ListNgosAsync);
        group.MapGet("/ngos/{id:guid}", GetNgoAsync);
        group.MapPost("/ngos", CreateNgoAsync);
        group.MapPut("/ngos/{id:guid}", UpdateNgoAsync);
        group.MapDelete("/ngos/{id:guid}", DeleteNgoAsync).RequireAuthorization(AuthPolicies.RequireGovernment);
    }

    // ==========================================
    // HOSPITALS
    // ==========================================

    private static async Task<IResult> ListHospitalsAsync(RegistryDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var hospitals = await db.Hospitals.AsNoTracking().OrderBy(h => h.Name).ToListAsync(ct);
        return Results.Ok(new ApiEnvelope<IReadOnlyList<HospitalDto>>(hospitals.Select(ToDto).ToList()));
    }

    private static async Task<IResult> GetHospitalAsync(Guid id, RegistryDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var hospital = await db.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);
        return hospital is null ? Results.NotFound() : Results.Ok(new ApiEnvelope<HospitalDto>(ToDto(hospital)));
    }

    private static async Task<IResult> CreateHospitalAsync(
        CreateHospitalRequest request,
        IValidator<CreateHospitalRequest> validator,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        TryGetUserId(context, out var userId);
        var now = clock.GetUtcNow();

        var hospital = new Hospital
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            TotalBeds = request.TotalBeds,
            AvailableBeds = request.AvailableBeds,
            IcuBeds = request.IcuBeds,
            AvailableIcuBeds = request.AvailableIcuBeds,
            HasEmergency = request.HasEmergency,
            Specialties = request.Specialties?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? [],
            ContactNumber = request.ContactNumber?.Trim() ?? string.Empty,
            Address = request.Address?.Trim() ?? string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Hospitals.Add(hospital);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.HospitalCreate", "Hospital", hospital.Id.ToString(),
            $"Registered hospital {hospital.Name}", "Created"), ct);

        return Results.Created($"{BasePath}/hospitals/{hospital.Id}", new ApiEnvelope<HospitalDto>(ToDto(hospital)));
    }

    private static async Task<IResult> UpdateHospitalAsync(
        Guid id,
        UpdateHospitalRequest request,
        IValidator<UpdateHospitalRequest> validator,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var hospital = await db.Hospitals.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hospital is null) return Results.NotFound();

        TryGetUserId(context, out var userId);
        var now = clock.GetUtcNow();

        hospital.Name = request.Name.Trim();
        hospital.Latitude = request.Latitude;
        hospital.Longitude = request.Longitude;
        hospital.TotalBeds = request.TotalBeds;
        hospital.AvailableBeds = request.AvailableBeds;
        hospital.IcuBeds = request.IcuBeds;
        hospital.AvailableIcuBeds = request.AvailableIcuBeds;
        hospital.HasEmergency = request.HasEmergency;
        hospital.Specialties = request.Specialties?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? [];
        hospital.ContactNumber = request.ContactNumber?.Trim() ?? string.Empty;
        hospital.Address = request.Address?.Trim() ?? string.Empty;
        hospital.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.HospitalUpdate", "Hospital", hospital.Id.ToString(),
            $"Updated hospital {hospital.Name}", "Updated"), ct);

        return Results.Ok(new ApiEnvelope<HospitalDto>(ToDto(hospital)));
    }

    private static async Task<IResult> DeleteHospitalAsync(
        Guid id,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var hospital = await db.Hospitals.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hospital is null) return Results.NotFound();

        TryGetUserId(context, out var userId);

        db.Hospitals.Remove(hospital);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.HospitalDelete", "Hospital", id.ToString(),
            $"Deleted hospital {hospital.Name}", "Deleted"), ct);

        return Results.NoContent();
    }

    // ==========================================
    // VOLUNTEERS
    // ==========================================

    private static async Task<IResult> ListVolunteersAsync(RegistryDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var volunteers = await db.Volunteers.AsNoTracking().OrderBy(v => v.FullName).ToListAsync(ct);
        return Results.Ok(new ApiEnvelope<IReadOnlyList<VolunteerDto>>(volunteers.Select(ToDto).ToList()));
    }

    private static async Task<IResult> GetVolunteerAsync(Guid id, RegistryDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var volunteer = await db.Volunteers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        return volunteer is null ? Results.NotFound() : Results.Ok(new ApiEnvelope<VolunteerDto>(ToDto(volunteer)));
    }

    private static async Task<IResult> CreateVolunteerAsync(
        CreateVolunteerRequest request,
        IValidator<CreateVolunteerRequest> validator,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        TryGetUserId(context, out var userId);
        var now = clock.GetUtcNow();

        var volunteer = new Volunteer
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId ?? userId,
            FullName = request.FullName.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Skills = request.Skills?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? [],
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Available" : request.Status.Trim(),
            ContactNumber = request.ContactNumber?.Trim() ?? string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Volunteers.Add(volunteer);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.VolunteerCreate", "Volunteer", volunteer.Id.ToString(),
            $"Registered volunteer {volunteer.FullName}", "Created"), ct);

        return Results.Created($"{BasePath}/volunteers/{volunteer.Id}", new ApiEnvelope<VolunteerDto>(ToDto(volunteer)));
    }

    private static async Task<IResult> UpdateVolunteerAsync(
        Guid id,
        UpdateVolunteerRequest request,
        IValidator<UpdateVolunteerRequest> validator,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var volunteer = await db.Volunteers.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (volunteer is null) return Results.NotFound();

        TryGetUserId(context, out var userId);
        var now = clock.GetUtcNow();

        volunteer.FullName = request.FullName.Trim();
        volunteer.Latitude = request.Latitude;
        volunteer.Longitude = request.Longitude;
        volunteer.Skills = request.Skills?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            volunteer.Status = request.Status.Trim();
        }
        volunteer.ContactNumber = request.ContactNumber?.Trim() ?? string.Empty;
        volunteer.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.VolunteerUpdate", "Volunteer", volunteer.Id.ToString(),
            $"Updated volunteer {volunteer.FullName}", "Updated"), ct);

        return Results.Ok(new ApiEnvelope<VolunteerDto>(ToDto(volunteer)));
    }

    private static async Task<IResult> DeleteVolunteerAsync(
        Guid id,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var volunteer = await db.Volunteers.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (volunteer is null) return Results.NotFound();

        TryGetUserId(context, out var userId);

        db.Volunteers.Remove(volunteer);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.VolunteerDelete", "Volunteer", id.ToString(),
            $"Deleted volunteer {volunteer.FullName}", "Deleted"), ct);

        return Results.NoContent();
    }

    // ==========================================
    // NGOS
    // ==========================================

    private static async Task<IResult> ListNgosAsync(RegistryDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var ngos = await db.Ngos.AsNoTracking().OrderBy(n => n.Name).ToListAsync(ct);
        return Results.Ok(new ApiEnvelope<IReadOnlyList<NgoDto>>(ngos.Select(ToDto).ToList()));
    }

    private static async Task<IResult> GetNgoAsync(Guid id, RegistryDbContext db, DatabaseHealth health, CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var ngo = await db.Ngos.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        return ngo is null ? Results.NotFound() : Results.Ok(new ApiEnvelope<NgoDto>(ToDto(ngo)));
    }

    private static async Task<IResult> CreateNgoAsync(
        CreateNgoRequest request,
        IValidator<CreateNgoRequest> validator,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        TryGetUserId(context, out var userId);
        var now = clock.GetUtcNow();

        var ngo = new Ngo
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            FocusAreas = request.FocusAreas?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? [],
            ContactPerson = request.ContactPerson?.Trim() ?? string.Empty,
            ContactEmail = request.ContactEmail?.Trim() ?? string.Empty,
            ContactNumber = request.ContactNumber?.Trim() ?? string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Ngos.Add(ngo);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.NgoCreate", "Ngo", ngo.Id.ToString(),
            $"Registered NGO {ngo.Name}", "Created"), ct);

        return Results.Created($"{BasePath}/ngos/{ngo.Id}", new ApiEnvelope<NgoDto>(ToDto(ngo)));
    }

    private static async Task<IResult> UpdateNgoAsync(
        Guid id,
        UpdateNgoRequest request,
        IValidator<UpdateNgoRequest> validator,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

        var ngo = await db.Ngos.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (ngo is null) return Results.NotFound();

        TryGetUserId(context, out var userId);
        var now = clock.GetUtcNow();

        ngo.Name = request.Name.Trim();
        ngo.Latitude = request.Latitude;
        ngo.Longitude = request.Longitude;
        ngo.FocusAreas = request.FocusAreas?.Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? [];
        ngo.ContactPerson = request.ContactPerson?.Trim() ?? string.Empty;
        ngo.ContactEmail = request.ContactEmail?.Trim() ?? string.Empty;
        ngo.ContactNumber = request.ContactNumber?.Trim() ?? string.Empty;
        ngo.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.NgoUpdate", "Ngo", ngo.Id.ToString(),
            $"Updated NGO {ngo.Name}", "Updated"), ct);

        return Results.Ok(new ApiEnvelope<NgoDto>(ToDto(ngo)));
    }

    private static async Task<IResult> DeleteNgoAsync(
        Guid id,
        RegistryDbContext db,
        IAuditTrail audit,
        DatabaseHealth health,
        HttpContext context,
        CancellationToken ct)
    {
        if (health.PostgresAvailable != true) return DatabaseUnavailable();

        var ngo = await db.Ngos.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (ngo is null) return Results.NotFound();

        TryGetUserId(context, out var userId);

        db.Ngos.Remove(ngo);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditRecord(userId, string.Empty, string.Empty,
            "Registry.NgoDelete", "Ngo", id.ToString(),
            $"Deleted NGO {ngo.Name}", "Deleted"), ct);

        return Results.NoContent();
    }

    // ==========================================
    // HELPERS & DTOS
    // ==========================================

    private static HospitalDto ToDto(Hospital h) => new(
        h.Id, h.Name, h.Latitude, h.Longitude, h.TotalBeds, h.AvailableBeds,
        h.IcuBeds, h.AvailableIcuBeds, h.HasEmergency, h.Specialties.AsReadOnly(),
        h.ContactNumber, h.Address, h.CreatedAtUtc, h.UpdatedAtUtc);

    private static VolunteerDto ToDto(Volunteer v) => new(
        v.Id, v.UserId, v.FullName, v.Latitude, v.Longitude, v.Skills.AsReadOnly(),
        v.Status, v.ContactNumber, v.CreatedAtUtc, v.UpdatedAtUtc);

    private static NgoDto ToDto(Ngo n) => new(
        n.Id, n.Name, n.Latitude, n.Longitude, n.FocusAreas.AsReadOnly(),
        n.ContactPerson, n.ContactEmail, n.ContactNumber, n.CreatedAtUtc, n.UpdatedAtUtc);

    private static bool TryGetUserId(HttpContext context, out Guid userId)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): registry data is temporarily unavailable.");
}
