using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Features.Auth.Services;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Auth.Endpoints;

/// <summary>
/// Blueprint B5 endpoints 1–8. Uniform 401 for every credential failure (login AND refresh);
/// specifics go to AuthEvent/logs only. Refresh cookie per D-012: rr_refresh, HttpOnly,
/// SameSite=Strict, Path=/api/auth, Secure outside Development/Testing.
/// </summary>
public static class AuthEndpoints
{
    private const string CookieName = "rr_refresh";
    private const string CookiePath = "/api/auth";

    private static readonly string[] AllowedPhotoExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.AddEndpointFilter(CacheControlNoStoreFilter);

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        group.MapPost("/oauth/google-session", GoogleSessionAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        group.MapPost("/oauth/google-init", GoogleInitAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth");
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization();
        group.MapGet("/profile", GetProfileAsync)
            .RequireAuthorization();
        group.MapPut("/profile", UpdateProfileAsync)
            .RequireAuthorization();
        group.MapPost("/profile/photo", UploadPhotoAsync)
            .RequireAuthorization()
            .DisableAntiforgery(); // IFormFile endpoint, Bearer auth ⇒ CSRF n/a (risk 5)
        group.MapGet("/profile/photo", GetPhotoAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        UserManager<AppUser> userManager,
        ITokenService tokenService,
        IEventBus eventBus,
        DatabaseHealth databaseHealth,
        HttpContext httpContext,
        IHostEnvironment env,
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

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName!,
            PhoneNumber = request.PhoneNumber,
            EmergencyContact = request.EmergencyContact,
        };
        var created = await userManager.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            return created.ToValidationProblem();
        }

        var assignedRole = ResolveRegistrationRole(request.Role, env);

        var roleResult = await userManager.AddToRoleAsync(user, assignedRole);
        if (!roleResult.Succeeded)
        {
            // Compensate: a stranded role-less account could still log in but would carry no
            // role claims — delete it so the user can simply retry registration.
            await userManager.DeleteAsync(user);
            return roleResult.ToValidationProblem();
        }

        await eventBus.PublishAsync(new AuthEvent(user.Id, "Register", null), ct);

        var session = await MintSessionAsync(user, [assignedRole], tokenService, httpContext, env, ct);
        return Results.Created("/api/auth/profile", new ApiEnvelope<AuthSessionDto>(session));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IPasswordHasher<AppUser> passwordHasher,
        ITokenService tokenService,
        IEventBus eventBus,
        DatabaseHealth databaseHealth,
        HttpContext httpContext,
        IHostEnvironment env,
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

        var user = await userManager.FindByEmailAsync(request.Email!);
        if (user is null)
        {
            // Burn the same PBKDF2 cost a real password check would — unknown-email and
            // wrong-password 401s must be indistinguishable by response time too.
            BurnPasswordVerification(passwordHasher, request.Password!);
            await eventBus.PublishAsync(new AuthEvent(Guid.Empty, "LoginFailed", "UnknownEmail"), ct);
            return InvalidCredentials();
        }

        var signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
        if (!signIn.Succeeded)
        {
            await eventBus.PublishAsync(
                new AuthEvent(user.Id, "LoginFailed", signIn.IsLockedOut ? "LockedOut" : "WrongPassword"), ct);
            return InvalidCredentials();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToList();
        await eventBus.PublishAsync(new AuthEvent(user.Id, "Login", null), ct);

        var session = await MintSessionAsync(user, roles, tokenService, httpContext, env, ct);
        return Results.Ok(new ApiEnvelope<AuthSessionDto>(session));
    }

    private static async Task<IResult> RefreshAsync(
        ITokenService tokenService,
        DatabaseHealth databaseHealth,
        HttpContext httpContext,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var rawToken) ||
            string.IsNullOrWhiteSpace(rawToken))
        {
            DeleteRefreshCookie(httpContext, env);
            return InvalidCredentials();
        }

        var outcome = await tokenService.ValidateAndRotateAsync(rawToken, ct);
        if (!outcome.Succeeded)
        {
            DeleteRefreshCookie(httpContext, env); // stops client silent-refresh loops
            return InvalidCredentials();
        }

        SetRefreshCookie(httpContext, env, outcome.NewRawRefreshToken!, outcome.RefreshExpiresAtUtc!.Value);
        var profile = BuildProfile(outcome.User!, outcome.Roles!);
        return Results.Ok(new ApiEnvelope<AuthSessionDto>(
            new AuthSessionDto(outcome.AccessToken!, outcome.AccessExpiresAtUtc!.Value, profile)));
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        ITokenService tokenService,
        IEventBus eventBus,
        DatabaseHealth databaseHealth,
        HttpContext httpContext,
        IHostEnvironment env,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var rawToken) &&
            !string.IsNullOrWhiteSpace(rawToken))
        {
            await tokenService.RevokeByRawTokenAsync(rawToken, ct); // idempotent — missing row is fine
        }

        DeleteRefreshCookie(httpContext, env);
        if (TryGetUserId(principal, out var userId))
        {
            await eventBus.PublishAsync(new AuthEvent(userId, "Logout", null), ct);
        }
        else
        {
            loggerFactory.CreateLogger(nameof(AuthEndpoints))
                .LogDebug("Logout principal carried no parseable user id claim — AuthEvent skipped");
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var user = await LoadCallerAsync(principal, userManager);
        if (user is null)
        {
            return UserNotFound();
        }

        var profile = BuildProfile(user, (await userManager.GetRolesAsync(user)).ToList());
        return Results.Ok(new ApiEnvelope<UserProfileDto>(profile));
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest request,
        IValidator<UpdateProfileRequest> validator,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
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

        var user = await LoadCallerAsync(principal, userManager);
        if (user is null)
        {
            return UserNotFound();
        }

        // Email is immutable in F1 — only the three mutable profile fields are written.
        user.DisplayName = request.DisplayName!;
        user.PhoneNumber = request.PhoneNumber;
        user.EmergencyContact = request.EmergencyContact;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return updated.ToValidationProblem();
        }

        var profile = BuildProfile(user, (await userManager.GetRolesAsync(user)).ToList());
        return Results.Ok(new ApiEnvelope<UserProfileDto>(profile));
    }

    private static async Task<IResult> UploadPhotoAsync(
        IFormFile? file,
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        IFileStorage fileStorage,
        DatabaseHealth databaseHealth,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (file is null || file.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A non-empty multipart field named 'file' is required."],
            });
        }

        // Profile photos are image-only; IFileStorage keeps its broader whitelist (.pdf/.mp4)
        // for future features, so the narrower gate must live here at the endpoint.
        var fileName = file.FileName ?? string.Empty;
        var photoExtension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedPhotoExtensions.Contains(photoExtension))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [$"Only {string.Join(", ", AllowedPhotoExtensions)} photos are allowed."],
            });
        }

        var user = await LoadCallerAsync(principal, userManager);
        if (user is null)
        {
            return UserNotFound();
        }

        string newPath;
        try
        {
            await using var content = file.OpenReadStream();
            var stored = await fileStorage.SaveAsync(content, fileName, file.ContentType, ct);
            newPath = stored.Path;
        }
        catch (ArgumentException ex)
        {
            // LocalDiskFileStorage rejects non-whitelisted extensions and oversize streams.
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [ex.Message] });
        }

        var oldPath = user.PhotoPath;
        user.PhotoPath = newPath;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            await fileStorage.DeleteAsync(newPath, ct); // don't strand the new file
            return updated.ToValidationProblem();
        }

        if (!string.IsNullOrEmpty(oldPath))
        {
            await fileStorage.DeleteAsync(oldPath, ct); // best-effort replace semantics
        }

        var profile = BuildProfile(user, (await userManager.GetRolesAsync(user)).ToList());
        return Results.Ok(new ApiEnvelope<UserProfileDto>(profile));
    }

    private static async Task<IResult> GetPhotoAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> userManager,
        IFileStorage fileStorage,
        DatabaseHealth databaseHealth,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        var user = await LoadCallerAsync(principal, userManager);
        if (user is null || string.IsNullOrEmpty(user.PhotoPath))
        {
            return PhotoNotFound();
        }

        var stream = await fileStorage.OpenReadAsync(user.PhotoPath, ct);
        if (stream is null)
        {
            return PhotoNotFound();
        }

        // inline + fixed name: never reflect a stored path into a download prompt.
        var photoFileExtension = Path.GetExtension(user.PhotoPath).ToLowerInvariant();
        httpContext.Response.Headers.ContentDisposition = $"inline; filename=photo{photoFileExtension}";
        return Results.Stream(stream, ContentTypeFor(user.PhotoPath)); // D-015 authenticated read
    }

    // ---- helpers ----

    /// <summary>Auth responses carry credentials/PII — no browser or intermediary may cache them.</summary>
    internal static async ValueTask<object?> CacheControlNoStoreFilter(
        EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        invocationContext.HttpContext.Response.Headers.CacheControl = "no-store, private";
        return await next(invocationContext);
    }

    // Lazily hashed once per process with the DI-configured iteration count, so the dummy
    // verification burns exactly the same cost as a real one (a hardcoded literal would pin
    // a stale iteration count). Benign race: two threads may each hash once; both are valid.
    private static string? _dummyPasswordHash;

    private static void BurnPasswordVerification(IPasswordHasher<AppUser> passwordHasher, string password)
    {
        var dummyUser = new AppUser();
        _dummyPasswordHash ??= passwordHasher.HashPassword(dummyUser, "dummy-password");
        passwordHasher.VerifyHashedPassword(dummyUser, _dummyPasswordHash, password);
    }

    private static async Task<AuthSessionDto> MintSessionAsync(AppUser user, IReadOnlyList<string> roles,
        ITokenService tokenService, HttpContext httpContext, IHostEnvironment env, CancellationToken ct)
    {
        var (accessToken, accessExpires) = tokenService.CreateAccessToken(user, roles);
        var (rawRefresh, row) = await tokenService.IssueRefreshTokenAsync(user, inheritedAbsoluteExpiry: null, ct);
        SetRefreshCookie(httpContext, env, rawRefresh, row.ExpiresAtUtc);
        return new AuthSessionDto(accessToken, accessExpires, BuildProfile(user, roles));
    }

    private static UserProfileDto BuildProfile(AppUser user, IReadOnlyList<string> roles) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.PhoneNumber,
            user.EmergencyContact,
            HasPhoto: !string.IsNullOrEmpty(user.PhotoPath),
            roles);

    private static async Task<AppUser?> LoadCallerAsync(ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        TryGetUserId(principal, out var userId)
            ? await userManager.FindByIdAsync(userId.ToString())
            : null;

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    /// <summary>
    /// Rescuer is an operational role: it unlocks the dispatch queue, the team registry and every
    /// reporter's precise location. Letting an anonymous registration body choose it is
    /// self-service privilege escalation, so only Development and Testing honour the request —
    /// they need self-registered responders for the demo. Everywhere else an administrator
    /// promotes the account through PUT /api/auth/users/{id}/roles.
    /// </summary>
    private static string ResolveRegistrationRole(string? requestedRole, IHostEnvironment env)
        => string.Equals(requestedRole, Roles.Rescuer, StringComparison.OrdinalIgnoreCase)
           && (env.IsDevelopment() || env.IsEnvironment("Testing"))
            ? Roles.Rescuer
            : Roles.Citizen;

    /// <summary>
    /// Accepts a requested OAuth callback only when it stays on this deployment's own origin;
    /// anything else falls back to the canonical callback path.
    /// </summary>
    internal static string SameOriginCallback(string? requested, string origin)
    {
        var fallback = $"{origin}/auth/callback";
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }

        return Uri.TryCreate(requested, UriKind.Absolute, out var candidate)
               && Uri.TryCreate(origin, UriKind.Absolute, out var self)
               && candidate.Scheme == self.Scheme
               && string.Equals(candidate.Authority, self.Authority, StringComparison.OrdinalIgnoreCase)
            ? candidate.ToString()
            : fallback;
    }

    private static CookieOptions BuildCookieOptions(IHostEnvironment env, DateTimeOffset? expires)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            // D-012/D-010 gate: Testing's CookieContainer and Dev's plain HTTP both drop Secure cookies.
            Secure = !(env.IsDevelopment() || env.IsEnvironment("Testing")),
        };
        if (expires is not null)
        {
            options.Expires = expires;
        }
        return options;
    }

    private static void SetRefreshCookie(HttpContext httpContext, IHostEnvironment env, string rawToken, DateTimeOffset expiresAtUtc) =>
        httpContext.Response.Cookies.Append(CookieName, rawToken, BuildCookieOptions(env, expiresAtUtc));

    /// <summary>Delete must repeat Path + attributes or browsers keep the stale cookie (risk 11).</summary>
    private static void DeleteRefreshCookie(HttpContext httpContext, IHostEnvironment env) =>
        httpContext.Response.Cookies.Delete(CookieName, BuildCookieOptions(env, expires: null));

    /// <summary>Byte-identical body for EVERY credential failure — enumeration-proof (B5).</summary>
    private static IResult InvalidCredentials() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");

    private static IResult UserNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found");

    private static IResult PhotoNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No profile photo");

    /// <summary>Stored extension → content type (upload whitelist subset); never the client's claim.</summary>
    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };

    private static async Task<IResult> GoogleInitAsync(
        GoogleInitRequest request,
        IHttpClientFactory httpClientFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var client = httpClientFactory.CreateClient();
            var payload = new
            {
                provider = "google",
                // Never forward a caller-supplied callback verbatim: it is the address the OAuth
                // result is delivered to, so an attacker-chosen value turns this into an open
                // redirect that hands them the victim's sign-in.
                callbackURL = SameOriginCallback(request.CallbackUrl, origin),
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://ep-little-mountain-b3ttfx56.neonauth.c-4.ap-southeast-1.aws.neon.tech/neondb/auth/sign-in/social");
            msg.Headers.Add("Origin", origin);
            msg.Content = JsonContent.Create(payload);

            using var resp = await client.SendAsync(msg, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
                if (json.TryGetProperty("url", out var urlProp) && urlProp.GetString() is { Length: > 0 } u)
                {
                    return Results.Ok(new { url = u });
                }
            }
        }
        catch
        {
            // fallback
        }

        var fallbackUrl = "https://ep-little-mountain-b3ttfx56.neonauth.c-4.ap-southeast-1.aws.neon.tech/neondb/auth";
        return Results.Ok(new { url = fallbackUrl });
    }

    private static async Task<IResult> GoogleSessionAsync(
        GoogleSessionRequest request,
        UserManager<AppUser> userManager,
        ITokenService tokenService,
        IEventBus eventBus,
        DatabaseHealth databaseHealth,
        HttpContext httpContext,
        IHostEnvironment env,
        CancellationToken ct)
    {
        // SECURITY (audit 2026-09-03): this endpoint mints a full session from a caller-supplied
        // e-mail without verifying any provider token — an authentication bypass for every account.
        // Refused outside local dev until the Neon Auth session is validated server-side.
        if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found");
        }

        if (databaseHealth.PostgresAvailable != true)
        {
            return DatabaseUnavailable();
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest("Email is required");
        }

        var assignedRole = ResolveRegistrationRole(request.Role, env);

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email.Split('@')[0] : request.DisplayName,
                EmailConfirmed = true,
            };
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return createResult.ToValidationProblem();
            }

            var roleResult = await userManager.AddToRoleAsync(user, assignedRole);
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return roleResult.ToValidationProblem();
            }

            await eventBus.PublishAsync(new AuthEvent(user.Id, "RegisterGoogle", null), ct);
        }
        else
        {
            var userRoles = await userManager.GetRolesAsync(user);
            if (userRoles.Count == 0)
            {
                await userManager.AddToRoleAsync(user, assignedRole);
            }
            else if (!string.IsNullOrEmpty(request.Role) && !userRoles.Contains(assignedRole) && (assignedRole == Roles.Citizen || assignedRole == Roles.Rescuer))
            {
                await userManager.AddToRoleAsync(user, assignedRole);
            }
        }

        if (user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return Results.Unauthorized();
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var session = await MintSessionAsync(user, roles, tokenService, httpContext, env, ct);
        return Results.Ok(new ApiEnvelope<AuthSessionDto>(session));
    }

    internal static IResult DatabaseUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Database unavailable",
        detail: "The app is running in degraded mode (D-005): Postgres is unreachable, so database-backed endpoints are temporarily unavailable.");
}
