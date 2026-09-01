using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RapidRelief.Client.Common.Auth;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Features.Auth;

/// <summary>
/// Session endpoint client (blueprint B10.2). Owns a handler-free HttpClient: the rr_refresh
/// cookie is the credential (the browser attaches it same-origin), and refreshing outside the
/// main handler chain makes recursion/stampedes impossible (risk 10). Profile calls go through
/// the main HttpClient so the Bearer handler applies.
/// </summary>
public sealed class AuthApi
{
    internal const string DegradedMessage = "The server can't reach its database right now — try again shortly.";
    private const string OfflineMessage = "Can't reach the server — check your connection and try again.";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RefreshBudget = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly JwtAuthStateProvider _authState;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthApi(HttpClient handlerFreeClient, JwtAuthStateProvider authState)
    {
        _http = handlerFreeClient;
        _authState = authState;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/auth/login", request);
            return await ReadSessionAsync(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AuthResult.Fail(OfflineMessage);
        }
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/auth/register", request);
            return await ReadSessionAsync(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return AuthResult.Fail(OfflineMessage);
        }
    }

    /// <summary>
    /// Single-flight silent refresh (boot restore + proactive mid-session). 401 clears the session;
    /// degraded/offline outcomes leave the current state untouched and report false.
    /// </summary>
    public async Task<bool> TryRefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            // Whoever held the lock may have refreshed already — re-check expiry (B10.2).
            if (_authState.HasSession && _authState.ExpiresAtUtc - DateTimeOffset.UtcNow >= RefreshMargin)
            {
                return true;
            }

            using var cts = new CancellationTokenSource(RefreshBudget);
            using var response = await _http.PostAsync("api/auth/refresh", content: null, cts.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _authState.ClearSession(); // no/rotated-away cookie — stay anonymous
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                return false; // degraded (503) etc. — keep whatever session state exists
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AuthSessionDto>>(
                cancellationToken: cts.Token);
            if (envelope is null)
            {
                return false;
            }

            _authState.SetSession(envelope.Data.AccessToken, envelope.Data.ExpiresAtUtc);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false; // offline PWA boot must not crash (B10.2)
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Best-effort server-side revocation; the client session dies regardless.</summary>
    public async Task LogoutAsync()
    {
        try
        {
            // Logout is [Authorize] — send the Bearer explicitly; this client has no auth handler.
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            if (_authState.AccessToken is { } accessToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline or timed-out logout still clears the local session below.
        }
        finally
        {
            _authState.ClearSession();
        }
    }

    /// <summary>Parses a ValidationProblem "errors" dictionary (FluentValidation + Identity code keys).</summary>
    internal static async Task<IReadOnlyDictionary<string, string[]>?> ReadFieldErrorsAsync(
        HttpResponseMessage response)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var fieldErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in errors.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var messages = field.Value.EnumerateArray()
                    .Where(message => message.ValueKind == JsonValueKind.String)
                    .Select(message => message.GetString()!)
                    .ToArray();
                if (messages.Length > 0)
                {
                    fieldErrors[field.Name] = messages;
                }
            }

            return fieldErrors;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<AuthResult> ReadSessionAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AuthSessionDto>>();
            if (envelope is null)
            {
                return AuthResult.Fail("The server returned an unexpected response.");
            }

            _authState.SetSession(envelope.Data.AccessToken, envelope.Data.ExpiresAtUtc);
            return AuthResult.Ok();
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AuthResult.Fail("Invalid email or password."),
            HttpStatusCode.ServiceUnavailable => AuthResult.Fail(DegradedMessage),
            HttpStatusCode.BadRequest => AuthResult.Invalid(await ReadFieldErrorsAsync(response)),
            HttpStatusCode.TooManyRequests => AuthResult.Fail("Too many attempts — wait a minute and try again."),
            _ => AuthResult.Fail($"The request failed ({(int)response.StatusCode})."),
        };
    }
}

/// <summary>Client-local outcome for login/register — not a wire mirror.</summary>
public sealed record AuthResult(
    bool Succeeded, string? Error, IReadOnlyDictionary<string, string[]>? FieldErrors)
{
    public static AuthResult Ok() => new(true, null, null);

    public static AuthResult Fail(string error) => new(false, error, null);

    public static AuthResult Invalid(IReadOnlyDictionary<string, string[]>? fieldErrors) =>
        fieldErrors is null || fieldErrors.Count == 0
            ? Fail("The server rejected the request — check the form and try again.")
            : new(false, null, fieldErrors);
}
