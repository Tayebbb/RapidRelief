using System.Net.Http.Json;
using RapidRelief.Client.Common.Ui;
using RapidRelief.Client.Features.Command;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Features.Registry;

public sealed record HospitalDto(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    int TotalBeds,
    int AvailableBeds,
    int IcuBeds,
    int AvailableIcuBeds,
    bool HasEmergency,
    IReadOnlyList<string> Specialties,
    string ContactNumber,
    string Address,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateHospitalRequest(
    string Name,
    double Latitude,
    double Longitude,
    int TotalBeds,
    int AvailableBeds,
    int IcuBeds,
    int AvailableIcuBeds,
    bool HasEmergency,
    List<string>? Specialties,
    string? ContactNumber,
    string? Address);

public sealed record UpdateHospitalRequest(
    string Name,
    double Latitude,
    double Longitude,
    int TotalBeds,
    int AvailableBeds,
    int IcuBeds,
    int AvailableIcuBeds,
    bool HasEmergency,
    List<string>? Specialties,
    string? ContactNumber,
    string? Address);

public sealed record VolunteerDto(
    Guid Id,
    Guid? UserId,
    string FullName,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> Skills,
    string Status,
    string ContactNumber,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateVolunteerRequest(
    string FullName,
    Guid? UserId,
    double? Latitude,
    double? Longitude,
    List<string>? Skills,
    string? Status,
    string? ContactNumber);

public sealed record UpdateVolunteerRequest(
    string FullName,
    double? Latitude,
    double? Longitude,
    List<string>? Skills,
    string? Status,
    string? ContactNumber);

public sealed record NgoDto(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> FocusAreas,
    string ContactPerson,
    string ContactEmail,
    string ContactNumber,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateNgoRequest(
    string Name,
    double Latitude,
    double Longitude,
    List<string>? FocusAreas,
    string? ContactPerson,
    string? ContactEmail,
    string? ContactNumber);

public sealed record UpdateNgoRequest(
    string Name,
    double Latitude,
    double Longitude,
    List<string>? FocusAreas,
    string? ContactPerson,
    string? ContactEmail,
    string? ContactNumber);

public sealed class RegistryClient(HttpClient http)
{
    private const string BasePath = "api/registry";

    // Hospitals
    public async Task<IReadOnlyList<HospitalDto>?> GetHospitalsAsync(CancellationToken ct = default)
        => await GetAsync<IReadOnlyList<HospitalDto>>($"{BasePath}/hospitals", ct);

    public async Task<HospitalDto?> GetHospitalAsync(Guid id, CancellationToken ct = default)
        => await GetAsync<HospitalDto>($"{BasePath}/hospitals/{id}", ct);

    public async Task<string?> CreateHospitalAsync(CreateHospitalRequest request, CancellationToken ct = default)
        => await PostAsync($"{BasePath}/hospitals", request, ct);

    public async Task<string?> UpdateHospitalAsync(Guid id, UpdateHospitalRequest request, CancellationToken ct = default)
        => await PutAsync($"{BasePath}/hospitals/{id}", request, ct);

    public async Task<string?> DeleteHospitalAsync(Guid id, CancellationToken ct = default)
        => await DeleteAsync($"{BasePath}/hospitals/{id}", ct);

    // Volunteers
    public async Task<IReadOnlyList<VolunteerDto>?> GetVolunteersAsync(CancellationToken ct = default)
        => await GetAsync<IReadOnlyList<VolunteerDto>>($"{BasePath}/volunteers", ct);

    public async Task<VolunteerDto?> GetVolunteerAsync(Guid id, CancellationToken ct = default)
        => await GetAsync<VolunteerDto>($"{BasePath}/volunteers/{id}", ct);

    public async Task<string?> CreateVolunteerAsync(CreateVolunteerRequest request, CancellationToken ct = default)
        => await PostAsync($"{BasePath}/volunteers", request, ct);

    public async Task<string?> UpdateVolunteerAsync(Guid id, UpdateVolunteerRequest request, CancellationToken ct = default)
        => await PutAsync($"{BasePath}/volunteers/{id}", request, ct);

    public async Task<string?> DeleteVolunteerAsync(Guid id, CancellationToken ct = default)
        => await DeleteAsync($"{BasePath}/volunteers/{id}", ct);

    // NGOs
    public async Task<IReadOnlyList<NgoDto>?> GetNgosAsync(CancellationToken ct = default)
        => await GetAsync<IReadOnlyList<NgoDto>>($"{BasePath}/ngos", ct);

    public async Task<NgoDto?> GetNgoAsync(Guid id, CancellationToken ct = default)
        => await GetAsync<NgoDto>($"{BasePath}/ngos/{id}", ct);

    public async Task<string?> CreateNgoAsync(CreateNgoRequest request, CancellationToken ct = default)
        => await PostAsync($"{BasePath}/ngos", request, ct);

    public async Task<string?> UpdateNgoAsync(Guid id, UpdateNgoRequest request, CancellationToken ct = default)
        => await PutAsync($"{BasePath}/ngos/{id}", request, ct);

    public async Task<string?> DeleteNgoAsync(Guid id, CancellationToken ct = default)
        => await DeleteAsync($"{BasePath}/ngos/{id}", ct);

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<T>>(url, ct);
            return envelope is null ? default : envelope.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return default;
        }
    }

    private async Task<string?> PostAsync(string url, object body, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(url, body, ct);
            return response.IsSuccessStatusCode ? null : await ProblemDetailReader.ReadAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }

    private async Task<string?> PutAsync(string url, object body, CancellationToken ct)
    {
        try
        {
            var response = await http.PutAsJsonAsync(url, body, ct);
            return response.IsSuccessStatusCode ? null : await ProblemDetailReader.ReadAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }

    private async Task<string?> DeleteAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await http.DeleteAsync(url, ct);
            return response.IsSuccessStatusCode ? null : await ProblemDetailReader.ReadAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }
}
